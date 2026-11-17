// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Collections.ObjectModel;
using System.Globalization;
using PixelEditor.Core;

namespace PixelEditor.ViewModels;

public sealed record RsiProjectResult(string Folder, string License, string Copyright, int FrameWidth, int FrameHeight);

/// <summary>A new RSI (a name.rsi folder with meta.json) or editing the metadata of an existing one: license, copyright, frame size.</summary>
public sealed class RsiProjectViewModel : DialogViewModel
{
    private readonly IDialogService _dialogs;
    private readonly bool _isNew;
    private string _parent, _name, _license, _copyright, _w, _h, _error = "";

    public RsiProjectViewModel(IDialogService dialogs, bool isNew, string parentFolder, string name, string license, string copyright, int w, int h)
    {
        _dialogs = dialogs;
        _isNew = isNew;
        _parent = parentFolder;
        _name = name;
        _license = license;
        _copyright = copyright;
        _w = w.ToString();
        _h = h.ToString();
        BrowseCommand = new RelayCommand(Browse);
        OkCommand = new RelayCommand(Ok);
    }

    public override string Title => _isNew ? Loc.T("rsi_dlg.new_rsi") : Loc.T("rsi_dlg.rsi_metadata");
    public bool IsNew => _isNew;
    public bool CanEditSize { get; init; } = true;
    public string OkText => _isNew ? Loc.T("new_doc.create") : Loc.T("colors.save");

    public string ParentFolder { get => _parent; set => SetProperty(ref _parent, value); }
    public string Name { get => _name; set { if (SetProperty(ref _name, value)) OnPropertyChanged(nameof(FullPath)); } }
    public string License { get => _license; set => SetProperty(ref _license, value); }
    public string Copyright { get => _copyright; set => SetProperty(ref _copyright, value); }
    public string FrameWidthText { get => _w; set => SetProperty(ref _w, value); }
    public string FrameHeightText { get => _h; set => SetProperty(ref _h, value); }
    public string Error { get => _error; private set => SetProperty(ref _error, value); }

    /// <summary>Where the RSI folder will be created.</summary>
    public string FullPath => string.IsNullOrWhiteSpace(_name) || string.IsNullOrWhiteSpace(_parent) ? "" : Path.Combine(_parent, FolderName(_name));

    public static string[] CommonLicenses { get; } = ["CC-BY-SA-3.0", "CC-BY-SA-4.0", "CC-BY-3.0", "CC-BY-4.0", "CC0-1.0", "AGPL-3.0-only", "MIT"];

    public RsiProjectResult? Result { get; private set; }
    public RelayCommand BrowseCommand { get; }
    public RelayCommand OkCommand { get; }

    private static string FolderName(string name) => name.EndsWith(".rsi", StringComparison.OrdinalIgnoreCase) ? name : name.Trim() + ".rsi";

    private void Browse()
    {
        var dir = _dialogs.PickFolder(Loc.T("rsi_dlg.folder_to_create_the_rsi"));
        if (dir != null) ParentFolder = dir;
    }

    private void Ok()
    {
        Error = "";
        if (_isNew && (string.IsNullOrWhiteSpace(_name) || _name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)) { Error = Loc.T("rsi_dlg.enter_a_folder_name_without"); return; }
        if (_isNew && string.IsNullOrWhiteSpace(_parent)) { Error = Loc.T("rsi_dlg.choose_the_folder_to_create"); return; }
        if (!int.TryParse(_w, out var w) || !int.TryParse(_h, out var h) || w < 1 || h < 1 || w > 1024 || h > 1024) { Error = Loc.T("rsi_dlg.frame_size_from_1_to"); return; }
        // license and copyright are optional: empty values are saved as empty strings
        Result = new RsiProjectResult(_isNew ? FullPath : _parent, (_license ?? "").Trim(), (_copyright ?? "").Trim(), w, h);
        Close(true);
    }
}

public sealed record RsiStateResult(string Name, int Directions, int Frames, double Delay, string[]? Rows = null, int FrameWidth = 0, int FrameHeight = 0,
    string[]? FrameNames = null, double[]? FrameDelays = null);

/// <summary>
/// A new sheet: a name, a row layout (1, 4 or 8 directions, or your own row names), frames (a number or a list with names and delays) and a frame size.
/// A ready layout can be taken from a template ("Walk: stand, step 1, step 2") and your own saved as a template for later.
/// For RSI only 1, 4 and 8 directions are available; for an animation sheet (Unity, Godot) rows can be named anything.
/// </summary>
public sealed class RsiStateViewModel : DialogViewModel
{
    private string _name = "state", _frames = "1", _delay = "0.1", _error = "", _custom = "idle, walk", _fw, _fh, _frameLines = "";
    private Choice _directions;
    private SheetTemplate? _template;
    private readonly string _title;
    private readonly bool _allowCustom;
    private readonly SheetTemplateLibrary? _library;
    private readonly IDialogService? _dialogs;
    private bool _syncing;            // the dialog itself is writing the delay, frame count and list fields: do not treat it as a user edit
    private double? _userDelay;       // a delay the user typed: it applies to every frame and survives choosing another template
    private double _lastDelay = 0.1;  // the last valid common delay, used by list lines that give none while the delay field is empty

    /// <param name="lastTemplateId">The template used last time (selected again when it is in the list).</param>
    /// <param name="lastDelay">The delay the user typed last time (applied to the template and the field).</param>
    public RsiStateViewModel(int frameWidth, int frameHeight, IEnumerable<string> existing, bool allowCustomRows = false, bool askSize = false, string? title = null,
        SheetTemplateLibrary? templates = null, IDialogService? dialogs = null, string? lastTemplateId = null, double? lastDelay = null)
    {
        _fw = frameWidth.ToString();
        _fh = frameHeight.ToString();
        _title = title ?? Loc.T("rsi_dlg.new_rsi_state");
        _allowCustom = allowCustomRows;
        _library = templates;
        _dialogs = dialogs;
        AskSize = askSize;
        Existing = existing.ToList();
        var choices = new List<Choice>
        {
            new("1", Loc.T("rsi_dlg.1_row_a_single_view")),
            new("4", Loc.T("rsi_dlg.4_rows_south_north_east")),
            new("8", Loc.T("rsi_dlg.8_rows_plus_diagonals_se")),
        };
        if (allowCustomRows) choices.Add(new Choice("custom", Loc.T("rsi_dlg.custom_rows_name_them_yourself")));
        DirectionChoices = choices;
        _directions = choices[allowCustomRows ? 3 : 1];
        if (allowCustomRows) _name = "hero";
        OkCommand = new RelayCommand(Ok);
        SaveTemplateCommand = new RelayCommand(SaveTemplate, () => _library != null && _dialogs != null);
        DeleteTemplateCommand = new RelayCommand(DeleteTemplate, () => _template is { IsSystem: false });
        ReloadTemplates();
        if (lastDelay is { } ld && ld > 0 && ld <= 60) { _userDelay = ld; _lastDelay = ld; _syncing = true; try { DelayText = FormatDelay(ld); } finally { _syncing = false; } }
        if (lastTemplateId != null && Templates.FirstOrDefault(t => t.Id == lastTemplateId) is { } last) SelectedTemplate = last;
    }

    public override string Title => _title;
    public bool AskSize { get; }
    public IReadOnlyList<string> Existing { get; }
    public IReadOnlyList<Choice> DirectionChoices { get; }

    // ---- layout templates ----

    public System.Collections.ObjectModel.ObservableCollection<SheetTemplate> Templates { get; } = new();
    public bool HasTemplates => _library != null;

    public SheetTemplate? SelectedTemplate
    {
        get => _template;
        set
        {
            if (value == null || !SetProperty(ref _template, value)) return;
            ApplyTemplate(value);
            DeleteTemplateCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(TemplateInfo));
        }
    }

    public string TemplateInfo => _template == null ? "" : $"{_template.Description} ({_template.Summary})";
    public RelayCommand SaveTemplateCommand { get; }
    public RelayCommand DeleteTemplateCommand { get; }

    private void ReloadTemplates(string? selectId = null)
    {
        Templates.Clear();
        if (_library == null) return;
        foreach (var t in _library.All().Where(t => _allowCustom || t.IsRsiCompatible)) Templates.Add(t);
        if (selectId != null) SelectedTemplate = Templates.FirstOrDefault(t => t.Id == selectId);
    }

    private void ApplyTemplate(SheetTemplate t)
    {
        var rows = t.Rows;
        bool standard = rows.Count is 1 or 4 or 8 && (rows.Count == 1 || rows.SequenceEqual(RsiLayout.DirectionNames(rows.Count)));
        if (standard && !(rows.Count == 1 && _allowCustom && rows[0] != "S"))
            SelectedDirections = DirectionChoices.First(c => c.Key == rows.Count.ToString());
        else if (_allowCustom)
        {
            SelectedDirections = DirectionChoices.First(c => c.Key == "custom");
            CustomRowsText = string.Join(", ", rows);
        }
        else if (rows.Count == 1) SelectedDirections = DirectionChoices.First(c => c.Key == "1");

        // the template fills the names and its own delays; a delay the user typed earlier wins over the template's
        var frames = _userDelay is { } ud ? t.Frames.Select(f => f with { Delay = ud }).ToList() : t.Frames.ToList();
        WriteFrames(frames);
    }

    // ---- the delay rule ----
    // A template fills rows, frame names and its delays. After that the delay field applies to ALL frames (and rewrites the list), a line edited by hand
    // wins for its frame, the frame count field resizes the list. A delay the user typed survives choosing another template. The field shows the
    // common delay when all frames agree and is empty (with a hint) when they differ: what is visible is what Create gives.

    private static string FormatDelay(double d) => d.ToString("0.####", CultureInfo.InvariantCulture);

    private static bool TryParseDelay(string? text, out double delay) =>
        double.TryParse((text ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out delay) && delay > 0 && delay <= 60;

    /// <summary>Writes the frames to the list, the frame count and the delay field (without counting it as a user edit).</summary>
    private void WriteFrames(IReadOnlyList<SheetFrame> frames)
    {
        _syncing = true;
        try
        {
            FramesText = frames.Count.ToString();
            FrameLinesText = string.Join("\n", frames.Select(f => $"{f.Name}, {FormatDelay(f.Delay)}"));
            ShowCommonDelay(frames);
        }
        finally { _syncing = false; }
        OnPropertyChanged(nameof(Summary));
    }

    private void ShowCommonDelay(IReadOnlyList<SheetFrame> frames)
    {
        bool same = frames.Count > 0 && frames.All(f => Math.Abs(f.Delay - frames[0].Delay) < 1e-9);
        if (same) _lastDelay = frames[0].Delay;
        var was = _syncing;
        _syncing = true;
        try { DelayText = same ? FormatDelay(frames[0].Delay) : ""; }
        finally { _syncing = was; }
        OnPropertyChanged(nameof(DelayInfo));
    }

    /// <summary>A hint under the delay field when the frames have different delays (the field is empty then).</summary>
    public string DelayInfo => _delay.Length == 0 && _frameLines.Trim().Length > 0 ? Loc.T("rsi_dlg.delay_differs_per_frame") : "";

    /// <summary>The delay the user typed (null: none); it is remembered for the next dialog.</summary>
    public double? UserDelay => _userDelay;

    private IEnumerable<(string Name, string? Delay)> SplitLines() =>
        _frameLines.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(line =>
        {
            var parts = line.Split([',', ';', '\t'], 2, StringSplitOptions.TrimEntries);
            return (parts[0], parts.Length == 2 && parts[1].Length > 0 ? parts[1] : null);
        });

    private void OnDelayTyped()
    {
        if (!TryParseDelay(_delay, out var d)) { OnPropertyChanged(nameof(DelayInfo)); return; }   // an invalid text is reported by Create
        _userDelay = d;
        _lastDelay = d;
        var lines = SplitLines().ToList();
        if (lines.Count > 0) WriteFrames(lines.Select(l => new SheetFrame(l.Name, d)).ToList());
        OnPropertyChanged(nameof(DelayInfo));
    }

    private void OnLinesTyped()
    {
        var parsed = ParseFrameLines(out var error);
        if (error != null) return;                                             // half-typed line: wait
        if (parsed == null)
        {
            // the list was cleared: the field must hold a delay again
            if (_delay.Trim().Length == 0) { _syncing = true; try { DelayText = FormatDelay(_lastDelay); } finally { _syncing = false; } }
            OnPropertyChanged(nameof(DelayInfo));
            return;
        }
        _syncing = true;
        try { FramesText = parsed.Count.ToString(); }
        finally { _syncing = false; }
        ShowCommonDelay(parsed);
    }

    private void OnFramesTyped()
    {
        if (!int.TryParse(_frames, out var n) || n < 1 || n > 64) return;
        var parsed = ParseFrameLines(out var error);
        if (parsed == null || error != null || parsed.Count == n) return;
        var frames = parsed.Take(n).ToList();
        double fill = frames.Count > 0 ? frames[^1].Delay : _lastDelay;
        for (int i = frames.Count; i < n; i++) frames.Add(new SheetFrame((i + 1).ToString(), fill));
        WriteFrames(frames);
    }

    // ---- fields ----

    public string Name { get => _name; set { if (SetProperty(ref _name, value)) Error = ""; } }

    public Choice SelectedDirections
    {
        get => _directions;
        set
        {
            if (value == null || !SetProperty(ref _directions, value)) return;
            OnPropertyChanged(nameof(IsCustomLayout));
            OnPropertyChanged(nameof(Summary));
        }
    }

    public bool IsCustomLayout => _directions.Key == "custom";

    /// <summary>Names of your own rows separated by commas: each becomes a separate animation.</summary>
    public string CustomRowsText { get => _custom; set { if (SetProperty(ref _custom, value)) OnPropertyChanged(nameof(Summary)); } }

    public string FramesText
    {
        get => _frames;
        set { if (SetProperty(ref _frames, value)) { OnPropertyChanged(nameof(Summary)); if (!_syncing) OnFramesTyped(); } }
    }

    /// <summary>The frame delay in seconds (comma or dot): typing it sets the delay of ALL frames, also after a template was chosen.</summary>
    public string DelayText
    {
        get => _delay;
        set { if (SetProperty(ref _delay, value ?? "") && !_syncing) OnDelayTyped(); }
    }

    /// <summary>
    /// Frames as a list, one per line: "name" or "name, delay in seconds". If filled, the number of frames is taken from here.
    /// Example: "stand, 0.5" "step 1, 0.15" "step 2, 0.15".
    /// </summary>
    public string FrameLinesText
    {
        get => _frameLines;
        set { if (SetProperty(ref _frameLines, value ?? "")) { OnPropertyChanged(nameof(Summary)); if (!_syncing) OnLinesTyped(); } }
    }

    public string FrameWidthText { get => _fw; set { if (SetProperty(ref _fw, value)) OnPropertyChanged(nameof(Summary)); } }
    public string FrameHeightText { get => _fh; set { if (SetProperty(ref _fh, value)) OnPropertyChanged(nameof(Summary)); } }
    public string Error { get => _error; private set => SetProperty(ref _error, value); }

    /// <summary>Sheet rows: the standard directions or the entered names.</summary>
    public IReadOnlyList<string> Rows => IsCustomLayout
        ? _custom.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
        : RsiLayout.DirectionNames(int.Parse(_directions.Key));

    private List<SheetFrame>? ParseFrameLines(out string? error)
    {
        error = null;
        var lines = _frameLines.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return null;
        // a line without its own delay takes the field; an empty field (frames differ) falls back to the last common delay
        double fallback = TryParseDelay(_delay, out var d) ? d : _lastDelay;
        var list = new List<SheetFrame>();
        foreach (var line in lines)
        {
            var parts = line.Split([',', ';', '\t'], 2, StringSplitOptions.TrimEntries);
            double delay = fallback;
            if (parts.Length == 2 && parts[1].Length > 0 && !TryParseDelay(parts[1], out delay))
            {
                error = Loc.T("rsi_dlg.frame_the_delay_in_seconds", parts[0]);
                return null;
            }
            list.Add(new SheetFrame(parts[0], delay));
        }
        return list;
    }

    /// <summary>What will result: the sheet size and the row captions.</summary>
    public string Summary
    {
        get
        {
            var lines = ParseFrameLines(out _);
            int f = lines?.Count ?? (int.TryParse(_frames, out var n) ? n : 0);
            if (f < 1 || !int.TryParse(_fw, out var w) || !int.TryParse(_fh, out var h)) return "";
            var rows = Rows;
            return Loc.T("rsi_dlg.sheet_px_rows_frames_per", w * f, h * Math.Max(1, rows.Count), string.Join(", ", rows), f, w, h);
        }
    }

    public RsiStateResult? Result { get; private set; }
    public RelayCommand OkCommand { get; }

    /// <summary>Frames for the template: from the list, otherwise by the frame count with one delay.</summary>
    private List<SheetFrame>? CurrentFrames(out string? error)
    {
        // a typed text that is not a delay is an error even when the list has its own delays: it must not be silently ignored
        if (_delay.Trim().Length > 0 && !TryParseDelay(_delay, out _)) { error = Loc.T("rsi_dlg.frame_delay_in_seconds_for"); return null; }
        var parsed = ParseFrameLines(out error);
        if (error != null) return null;
        if (parsed != null) return parsed;
        if (!int.TryParse(_frames, out var n) || n < 1 || n > 64) { error = Loc.T("rsi_dlg.frames_from_1_to_64"); return null; }
        if (!TryParseDelay(_delay, out var delay)) { error = Loc.T("rsi_dlg.frame_delay_in_seconds_for"); return null; }
        return Enumerable.Range(1, n).Select(i => new SheetFrame(i.ToString(), delay)).ToList();
    }

    private void SaveTemplate()
    {
        if (_library == null || _dialogs == null) return;
        var frames = CurrentFrames(out var err);
        if (frames == null) { Error = err ?? Loc.T("rsi_dlg.check_the_frames"); return; }
        var rows = Rows.ToList();
        if (rows.Count == 0) { Error = Loc.T("rsi_dlg.at_least_one_row_is_2"); return; }
        var dlg = new NameDescriptionViewModel(Loc.T("rsi_dlg.save_layout_template"), "", "",
            n => _library.All().Any(t => t.Name.Equals(n, StringComparison.CurrentCultureIgnoreCase)) ? Loc.T("rsi_dlg.a_template_with_this_name") : null,
            null, Loc.T("colors.save"), Loc.T("rsi_dlg.rows_frames_the_template_will", string.Join(", ", rows), frames.Count));
        if (_dialogs.ShowDialog(dlg) != true) return;
        try
        {
            var saved = _library.Save(dlg.Name, dlg.Description, rows, frames);
            if (!_allowCustom && !saved.IsRsiCompatible) { Error = Loc.T("rsi_dlg.saved_but_this_template_is"); return; }
            ReloadTemplates(saved.Id);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { Error = ex.Message; }
    }

    private void DeleteTemplate()
    {
        if (_library == null || _dialogs == null || _template is not { IsSystem: false } t) return;
        if (!_dialogs.ConfirmDestructive(Loc.T("rsi_dlg.delete_template"), Loc.T("rsi_dlg.delete_layout_template_sheets_created", t.Name))) return;
        _library.Delete(t.Id);
        _template = null;
        OnPropertyChanged(nameof(SelectedTemplate));
        OnPropertyChanged(nameof(TemplateInfo));
        ReloadTemplates();
        DeleteTemplateCommand.RaiseCanExecuteChanged();
    }

    private void Ok()
    {
        Error = "";
        var name = _name.Trim();
        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) { Error = Loc.T("rsi_dlg.enter_a_name_without_forbidden"); return; }
        if (Existing.Any(e => e.Equals(name, StringComparison.OrdinalIgnoreCase))) { Error = Loc.T("rsi_dlg.the_state_already_exists_in", name); return; }
        var frameList = CurrentFrames(out var frameError);
        if (frameList == null) { Error = frameError ?? Loc.T("rsi_dlg.check_the_frames"); return; }
        if (frameList.Count > 64) { Error = Loc.T("rsi_dlg.frames_from_1_to_64"); return; }
        int fw = 0, fh = 0;
        if (AskSize && (!int.TryParse(_fw, out fw) || !int.TryParse(_fh, out fh) || fw < 1 || fh < 1 || fw > 1024 || fh > 1024)) { Error = Loc.T("rsi_dlg.frame_size_from_1_to"); return; }

        string[]? rows = null;
        int dirs;
        if (IsCustomLayout)
        {
            var list = Rows;
            if (list.Count is < 1 or > 32) { Error = Loc.T("rsi_dlg.at_least_one_row_is"); return; }
            if (list.Any(r => r.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)) { Error = Loc.T("rsi_dlg.row_names_cannot_contain"); return; }
            if (list.Select(r => r.ToLowerInvariant()).Distinct().Count() != list.Count) { Error = Loc.T("rsi_dlg.row_names_must_not_repeat"); return; }
            rows = list.ToArray();
            dirs = rows.Length;
        }
        else dirs = int.Parse(_directions.Key);

        bool named = ParseFrameLines(out _) != null;
        Result = new RsiStateResult(name, dirs, frameList.Count, frameList[0].Delay, rows, fw, fh,
            named ? frameList.Select(f => f.Name).ToArray() : null, frameList.Select(f => f.Delay).ToArray());
        Close(true);
    }
}

public sealed record RsiOpenResult(string Folder, string State);

/// <summary>Open a state from an existing .rsi: choose a folder and a state from the list.</summary>
public sealed class RsiOpenViewModel : DialogViewModel
{
    private readonly IDialogService _dialogs;
    private string _folder = "", _error = "";
    private string? _selected;

    public RsiOpenViewModel(IDialogService dialogs, string startFolder = "")
    {
        _dialogs = dialogs;
        BrowseCommand = new RelayCommand(Browse);
        OkCommand = new RelayCommand(Ok, () => _selected != null);
        if (startFolder.Length > 0) Folder = startFolder;
    }

    public override string Title => Loc.T("rsi_dlg.open_rsi_state");
    public string Folder { get => _folder; set { if (SetProperty(ref _folder, value)) Reload(); } }
    public ObservableCollection<string> States { get; } = new();
    public string? SelectedState { get => _selected; set { if (SetProperty(ref _selected, value)) OkCommand.RaiseCanExecuteChanged(); } }
    public string Error { get => _error; private set => SetProperty(ref _error, value); }
    public RsiOpenResult? Result { get; private set; }
    public RelayCommand BrowseCommand { get; }
    public RelayCommand OkCommand { get; }

    private void Browse()
    {
        var dir = _dialogs.PickFolder(Loc.T("rsi_dlg.rsi_folder"));
        if (dir != null) Folder = dir;
    }

    private void Reload()
    {
        States.Clear();
        SelectedState = null;
        Error = "";
        var meta = Path.Combine(_folder, "meta.json");
        if (!File.Exists(meta)) { Error = _folder.Length == 0 ? "" : Loc.T("rsi_dlg.there_is_no_meta_json"); return; }
        try
        {
            foreach (var s in RsiMeta.Parse(File.ReadAllText(meta)).States) States.Add(s.Name);
            SelectedState = States.FirstOrDefault();
            if (States.Count == 0) Error = Loc.T("rsi_dlg.this_rsi_has_no_states");
        }
        catch (Exception ex) { Error = ex.Message; }
    }

    private void Ok()
    {
        if (_selected == null) return;
        Result = new RsiOpenResult(_folder, _selected);
        Close(true);
    }
}

public sealed record AnimationExportResult(string Folder, string BaseName, bool Aseprite, bool Godot, bool Gif, bool Simple, string GodotTexturePath,
    bool Unity = false, IReadOnlyList<string>? NoLoopRows = null)
{
    /// <summary>The settings to remember in the document (and to repeat with "Export again").</summary>
    public AnimationExportProfile ToProfile() => new(Folder, BaseName, Aseprite, Godot, Gif, Simple, Unity, GodotTexturePath, (NoLoopRows ?? []).ToArray());

    public static AnimationExportResult FromProfile(AnimationExportProfile p) =>
        new(p.Folder, p.BaseName, p.Aseprite, p.Godot, p.Gif, p.Simple, p.GodotTexturePath, p.Unity, p.NoLoop.ToList());
}

/// <summary>One animation row in the export dialog with its loop checkbox.</summary>
public sealed class LoopRow : ObservableObject
{
    private bool _loop;
    public LoopRow(string name, bool loop = true) { Name = name; _loop = loop; }
    public string Name { get; }
    public bool Loop { get => _loop; set => SetProperty(ref _loop, value); }
}

/// <summary>Animation export for engines: always the PNG sheet, plus descriptions for Unity (Aseprite JSON), Godot (.tres), a GIF per direction and a simple JSON.</summary>
public sealed class AnimationExportViewModel : DialogViewModel
{
    private readonly IDialogService _dialogs;
    private string _folder, _baseName, _godotPath, _error = "";
    private bool _ase = true, _godot = true, _gif = true, _simple, _unity;

    /// <summary>rows: names of the animations (sheet rows) for the loop checkboxes; profile: the last export settings of the document to pre-fill from.</summary>
    public AnimationExportViewModel(IDialogService dialogs, string folder, string baseName, string info, IReadOnlyList<string>? rows = null, AnimationExportProfile? profile = null)
    {
        _dialogs = dialogs;
        _folder = folder;
        _baseName = baseName;
        _godotPath = $"res://sprites/{baseName}.png";
        Info = info;
        if (profile != null)
        {
            if (!string.IsNullOrWhiteSpace(profile.Folder)) _folder = profile.Folder;
            if (!string.IsNullOrWhiteSpace(profile.BaseName)) _baseName = profile.BaseName;
            _godotPath = string.IsNullOrWhiteSpace(profile.GodotTexturePath) ? $"res://sprites/{_baseName}.png" : profile.GodotTexturePath;
            (_ase, _godot, _gif, _simple, _unity) = (profile.Aseprite, profile.Godot, profile.Gif, profile.Simple, profile.Unity);
        }
        var noLoop = new HashSet<string>(profile?.NoLoop ?? [], StringComparer.OrdinalIgnoreCase);
        foreach (var r in rows ?? []) Loops.Add(new LoopRow(r, !noLoop.Contains(r)));
        BrowseCommand = new RelayCommand(Browse);
        OkCommand = new RelayCommand(Ok);
    }

    public override string Title => Loc.T("rsi_dlg.animation_export");
    public string Info { get; }
    public string Folder { get => _folder; set => SetProperty(ref _folder, value); }
    public string BaseName { get => _baseName; set { if (SetProperty(ref _baseName, value)) GodotTexturePath = $"res://sprites/{value}.png"; } }
    public bool Aseprite { get => _ase; set => SetProperty(ref _ase, value); }
    public bool Godot { get => _godot; set => SetProperty(ref _godot, value); }
    public bool Gif { get => _gif; set => SetProperty(ref _gif, value); }
    public bool Simple { get => _simple; set => SetProperty(ref _simple, value); }
    /// <summary>The Unity editor script PixelEditorSheetImporter.cs (it reads the plain JSON, which is written automatically with it).</summary>
    public bool Unity { get => _unity; set => SetProperty(ref _unity, value); }
    /// <summary>Animations (sheet rows) with a loop flag each; unchecked ones do not loop in Godot and in the plain JSON.</summary>
    public ObservableCollection<LoopRow> Loops { get; } = new();
    /// <summary>What to do with the files in Unity and Godot (briefly, step by step).</summary>
    public string HowTo =>
        Loc.T("rsi_dlg.unity_drag_the_png_into") +
        Loc.T("rsi_dlg.select_the_frames_of_one") +
        Loc.T("rsi_dlg.unity_editor_script_drop_pixeleditorsheetimporter") +
        Loc.T("rsi_dlg.godot_4_put_the_png") +
        Loc.T("rsi_dlg.for_crisp_pixel_art_project");

    public string GodotTexturePath { get => _godotPath; set => SetProperty(ref _godotPath, value); }
    public string Error { get => _error; private set => SetProperty(ref _error, value); }
    public AnimationExportResult? Result { get; private set; }
    public RelayCommand BrowseCommand { get; }
    public RelayCommand OkCommand { get; }

    private void Browse()
    {
        var dir = _dialogs.PickFolder(Loc.T("rsi_dlg.export_folder"));
        if (dir != null) Folder = dir;
    }

    private void Ok()
    {
        if (string.IsNullOrWhiteSpace(_folder)) { Error = Loc.T("rsi_dlg.choose_a_folder"); return; }
        if (string.IsNullOrWhiteSpace(_baseName) || _baseName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) { Error = Loc.T("rsi_dlg.enter_a_file_name_without"); return; }
        Result = new AnimationExportResult(_folder, _baseName.Trim(), _ase, _godot, _gif, _simple, _godotPath, _unity,
            Loops.Where(l => !l.Loop).Select(l => l.Name).ToList());
        Close(true);
    }
}
