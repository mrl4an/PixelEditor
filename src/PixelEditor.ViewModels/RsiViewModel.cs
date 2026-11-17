// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using PixelEditor.Core;

namespace PixelEditor.ViewModels;

/// <summary>
/// Working with RSI: a new RSI (the program creates meta.json), the user fills the states on a sheet with direction and frame zones,
/// saving a state (the PNG and a record in meta.json) and animation export for Unity, Godot and GIF.
/// </summary>
public sealed class RsiViewModel : ObservableObject
{
    private readonly EditorSession _s;
    private string? _currentFolder;

    public RsiViewModel(EditorSession session)
    {
        _s = session;
        _s.DocumentChanged += RaiseState;
        _s.StateChanged += RaiseState;
        Loc.Instance.LanguageChanged += () => OnPropertyChanged(nameof(StatusLine));   // the RSI status line is assembled from texts and changes with the language
        NewProjectCommand = new RelayCommand(NewProject);
        NewStateCommand = new RelayCommand(NewState);
        OpenStateCommand = new RelayCommand(OpenState);
        SaveStateCommand = new RelayCommand(SaveState, () => _s.Doc.Rsi is { IsRsi: true });
        NewSheetCommand = new RelayCommand(NewAnimationSheet);
        EditMetaCommand = new RelayCommand(EditMeta, () => CurrentFolder != null);
        ExportAnimationCommand = new RelayCommand(ExportAnimation);
        ExportAgainCommand = new RelayCommand(ExportAgain, () => _s.Doc.ExportProfile != null && (_s.Doc.Rsi != null || _s.Doc.HasSheetGrid));
        OpenFolderCommand = new RelayCommand(() => { if (CurrentFolder != null) _s.Services.Shell.OpenFolder(CurrentFolder); }, () => CurrentFolder != null);
    }

    private IDialogService Dialogs => _s.Dialogs;

    /// <summary>The folder of the current .rsi (after "New RSI" or "Open"); null if no RSI is chosen yet.</summary>
    public string? CurrentFolder
    {
        get => _s.Doc.Rsi?.Folder ?? _currentFolder;
        private set { if (SetProperty(ref _currentFolder, value)) RaiseState(); }
    }

    public string StatusLine => _s.Doc.Rsi is { IsRsi: false } sheet
        ? Loc.T("rsi.animation_sheet_rows_frames", sheet.State, sheet.Directions, sheet.Frames)
        : _s.Doc.Rsi is { } b
        ? Loc.T("rsi.rsi_state_dir_fr", Path.GetFileName(b.Folder), b.State, b.Directions, b.Frames)
        : CurrentFolder != null ? Loc.T("rsi.rsi_create_or_open_a", Path.GetFileName(CurrentFolder)) : Loc.T("rsi.no_rsi_selected");

    public RelayCommand NewProjectCommand { get; }
    public RelayCommand NewStateCommand { get; }
    public RelayCommand NewSheetCommand { get; }
    public RelayCommand OpenStateCommand { get; }
    public RelayCommand SaveStateCommand { get; }
    public RelayCommand EditMetaCommand { get; }
    public RelayCommand ExportAnimationCommand { get; }
    /// <summary>Repeats the last animation export of this document without the dialog; available once the document has an export profile.</summary>
    public RelayCommand ExportAgainCommand { get; }
    public RelayCommand OpenFolderCommand { get; }

    private void RaiseState()
    {
        OnPropertyChanged(nameof(CurrentFolder));
        OnPropertyChanged(nameof(StatusLine));
        SaveStateCommand.RaiseCanExecuteChanged();
        EditMetaCommand.RaiseCanExecuteChanged();
        OpenFolderCommand.RaiseCanExecuteChanged();
        ExportAgainCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Delays per row: every row has the same frames (from the template or an identical delay).</summary>
    private static double[][] DelaysOf(RsiStateResult r) =>
        r.FrameDelays is { Length: > 0 } fd && fd.Length == r.Frames
            ? Enumerable.Range(0, r.Directions).Select(_ => fd.ToArray()).ToArray()
            : RsiState.UniformDelays(r.Directions, r.Frames, r.Delay);

    private static string MetaPath(string folder) => Path.Combine(folder, "meta.json");

    private RsiMeta? ReadMeta(string folder)
    {
        try { return RsiMeta.Parse(File.ReadAllText(MetaPath(folder))); }
        catch (Exception ex) { Dialogs.ShowError(Loc.T("rsi.could_not_read_meta_json") + ex.Message); return null; }
    }

    // ---- new RSI and metadata ----

    private void NewProject()
    {
        var dlg = new RsiProjectViewModel(Dialogs, isNew: true, _s.Settings.LastRsiParent ?? "", Loc.T("rsi.new"), "CC-BY-SA-3.0", "", 32, 32);
        if (Dialogs.ShowDialog(dlg) != true || dlg.Result is not { } r) return;
        try
        {
            if (File.Exists(MetaPath(r.Folder))) { Dialogs.ShowError(Loc.T("rsi.there_is_already_an_rsi")); return; }
            Directory.CreateDirectory(r.Folder);
            var meta = new RsiMeta { License = r.License, Copyright = r.Copyright, FrameWidth = r.FrameWidth, FrameHeight = r.FrameHeight };
            File.WriteAllText(MetaPath(r.Folder), meta.ToJson());
            _s.Settings.LastRsiParent = Path.GetDirectoryName(r.Folder);
            _s.Settings.Save();
            CurrentFolder = r.Folder;
            _s.Status(Loc.T("rsi.rsi_created_add_a_state", Path.GetFileName(r.Folder), r.FrameWidth, r.FrameHeight));
            // offer to create the first state at once: an empty RSI without states shows nothing
            if (Dialogs.Ask(MessageViewModel.Confirm(Loc.T("rsi.rsi_created"), Loc.T("rsi.the_folder_is_ready_create", Path.GetFileName(r.Folder)), Loc.T("rsi.create_state"), Loc.T("rsi.later"))) == MessageViewModel.Yes)
                NewState();
        }
        catch (Exception ex) { Dialogs.ShowError(ex); }
    }

    private void EditMeta()
    {
        if (CurrentFolder is not { } folder || ReadMeta(folder) is not { } meta) return;
        var dlg = new RsiProjectViewModel(Dialogs, isNew: false, folder, Path.GetFileName(folder), meta.License, meta.Copyright, meta.FrameWidth, meta.FrameHeight)
        {
            CanEditSize = meta.States.Count == 0,
        };
        if (Dialogs.ShowDialog(dlg) != true || dlg.Result is not { } r) return;
        if (meta.States.Count > 0 && (r.FrameWidth != meta.FrameWidth || r.FrameHeight != meta.FrameHeight))
        {
            Dialogs.ShowWarning(Loc.T("rsi.the_frame_size_cannot_be"), "RSI");
            return;
        }
        meta.License = r.License;
        meta.Copyright = r.Copyright;
        meta.FrameWidth = r.FrameWidth;
        meta.FrameHeight = r.FrameHeight;
        try { File.WriteAllText(MetaPath(folder), meta.ToJson()); _s.Status(Loc.T("rsi.rsi_metadata_saved")); }
        catch (Exception ex) { Dialogs.ShowError(ex); }
    }

    // ---- states ----

    private void NewState()
    {
        if (CurrentFolder is not { } folder)
        {
            Dialogs.ShowInfo(Loc.T("rsi.first_create_an_rsi_rsi"), "RSI");
            return;
        }
        if (ReadMeta(folder) is not { } meta) return;
        var dlg = new RsiStateViewModel(meta.FrameWidth, meta.FrameHeight, meta.States.Select(s => s.Name), templates: _s.Settings.SheetTemplates, dialogs: Dialogs,
            lastTemplateId: _s.Settings.LastSheetTemplateId, lastDelay: _s.Settings.LastSheetDelay);
        if (Dialogs.ShowDialog(dlg) != true || dlg.Result is not { } r) return;
        RememberLayoutChoice(dlg);
        if (!_s.ConfirmDiscard()) return;

        var binding = new RsiBinding(folder, r.Name, r.Directions, r.Frames, meta.FrameWidth, meta.FrameHeight, DelaysOf(r), null, r.FrameNames);
        OpenSheet(new Document(binding.SheetWidth, binding.SheetHeight), binding);
        _s.Status(Loc.T("rsi.state_draw_in_the_sheet", r.Name));
    }

    private void OpenState()
    {
        var dlg = new RsiOpenViewModel(Dialogs, CurrentFolder ?? "");
        if (Dialogs.ShowDialog(dlg) != true || dlg.Result is not { } r) return;
        if (ReadMeta(r.Folder) is not { } meta || meta.Find(r.State) is not { } state) return;
        if (!_s.ConfirmDiscard()) return;

        var binding = new RsiBinding(r.Folder, state.Name, state.Directions, state.Frames, meta.FrameWidth, meta.FrameHeight, state.Delays);
        Document doc;
        var png = Path.Combine(r.Folder, state.Name + ".png");
        var loaded = File.Exists(png) || _s.Images != null ? SafeLoad(png) : null;
        doc = loaded != null
            ? Document.FromImage(RsiLayout.Repack(loaded, meta.FrameWidth, meta.FrameHeight, state.Directions, state.Frames))
            : new Document(binding.SheetWidth, binding.SheetHeight); // there is no PNG yet: an empty sheet
        OpenSheet(doc, binding);
        CurrentFolder = r.Folder;
        _s.Status(Loc.T("rsi.opened_state_from", state.Name, Path.GetFileName(r.Folder)));
    }

    private PixelImage? SafeLoad(string path)
    {
        try { return _s.Images.Load(path); }
        catch { return null; }
    }

    /// <summary>An animation sheet without RSI: your own row names, frame size, frames. For Unity, Godot and GIF (animation export).</summary>
    private void NewAnimationSheet()
    {
        var dlg = new RsiStateViewModel(_s.Settings.LastSheetFrameWidth, _s.Settings.LastSheetFrameHeight, [], allowCustomRows: true, askSize: true, title: Loc.T("rsi.new_animation_sheet"),
            templates: _s.Settings.SheetTemplates, dialogs: Dialogs, lastTemplateId: _s.Settings.LastSheetTemplateId, lastDelay: _s.Settings.LastSheetDelay);
        if (Dialogs.ShowDialog(dlg) != true || dlg.Result is not { } r) return;
        RememberLayoutChoice(dlg, r);
        if (!_s.ConfirmDiscard()) return;
        var binding = new RsiBinding("", r.Name, r.Directions, r.Frames, r.FrameWidth, r.FrameHeight, DelaysOf(r), r.Rows, r.FrameNames);
        OpenSheet(new Document(binding.SheetWidth, binding.SheetHeight), binding);
        _s.Status(Loc.T("rsi.animation_sheet_draw_frames_in", r.Name));
    }

    /// <summary>The template, the typed delay and the frame size are offered again next time.</summary>
    private void RememberLayoutChoice(RsiStateViewModel dlg, RsiStateResult? r = null)
    {
        if (dlg.SelectedTemplate != null) _s.Settings.LastSheetTemplateId = dlg.SelectedTemplate.Id;
        _s.Settings.LastSheetDelay = dlg.UserDelay;
        if (r is { FrameWidth: > 0, FrameHeight: > 0 }) { _s.Settings.LastSheetFrameWidth = r.FrameWidth; _s.Settings.LastSheetFrameHeight = r.FrameHeight; }
        _s.Settings.Save();
    }

    private void OpenSheet(Document doc, RsiBinding binding)
    {
        doc.SheetCellWidth = binding.FrameWidth;
        doc.SheetCellHeight = binding.FrameHeight;
        doc.Rsi = binding;
        _currentFolder = binding.IsRsi ? binding.Folder : _currentFolder;
        _s.SetDocument(doc, null);
        _s.RaiseOverlaysChanged();
        RaiseState();
    }

    /// <summary>Writes the state PNG into the .rsi folder and updates its description in meta.json (the other states are not touched).</summary>
    private void SaveState()
    {
        _s.FlushPending();
        if (_s.Doc.Rsi is not { } b) return;
        var doc = _s.Doc;
        if (doc.Width != b.SheetWidth || doc.Height != b.SheetHeight)
        {
            Dialogs.ShowError(Loc.T("rsi.the_canvas_size_does_not", doc.Width, doc.Height, b.SheetWidth, b.SheetHeight) +
                              Loc.T("rsi.dir_fr_of_restore_the", b.Directions, b.Frames, b.FrameWidth, b.FrameHeight));
            return;
        }
        try
        {
            Directory.CreateDirectory(b.Folder);
            var meta = File.Exists(MetaPath(b.Folder)) ? RsiMeta.Parse(File.ReadAllText(MetaPath(b.Folder)))
                : new RsiMeta { FrameWidth = b.FrameWidth, FrameHeight = b.FrameHeight };
            if (meta.FrameWidth != b.FrameWidth || meta.FrameHeight != b.FrameHeight)
            {
                Dialogs.ShowError(Loc.T("rsi.the_frame_size_in_meta", meta.FrameWidth, meta.FrameHeight, b.FrameWidth, b.FrameHeight));
                return;
            }
            _s.Images.SavePng(doc.Flatten(), Path.Combine(b.Folder, b.State + ".png"));
            meta.Upsert(new RsiState { Name = b.State, Directions = b.Directions, Frames = b.Frames, Delays = b.Delays });
            File.WriteAllText(MetaPath(b.Folder), meta.ToJson());
            _s.Dirty = false;
            _s.Status(Loc.T("rsi.state_saved_to", b.State, Path.GetFileName(b.Folder)));
        }
        catch (Exception ex) { Dialogs.ShowError(ex); }
    }

    // ---- animation export ----

    /// <summary>What the current document offers for export: frame grid, delays, the base name and the row names (null: the standard directions).</summary>
    private sealed record ExportSource(int Fw, int Fh, int Frames, int Dirs, double[][] Delays, string Name, IReadOnlyList<string>? Rows)
    {
        /// <summary>Names shown in the dialog (one loop checkbox each): the clip name for a single row, otherwise the row labels.</summary>
        public IReadOnlyList<string> RowNames => Dirs == 1 ? [Name] : Rows ?? (Dirs <= 8 ? RsiLayout.DirectionNames(Dirs) : Enumerable.Range(1, Dirs).Select(i => $"row{i}").ToList());
    }

    private ExportSource? CurrentExportSource()
    {
        var doc = _s.Doc;
        if (doc.Rsi is { } b)
            return new ExportSource(b.FrameWidth, b.FrameHeight, b.Frames, b.Directions, b.Delays, b.State, b.RowLabels);
        if (doc.HasSheetGrid)
        {
            int fw = doc.SheetCellWidth, fh = doc.SheetCellHeight;
            int frames = Math.Max(1, doc.Width / fw), dirs = Math.Max(1, doc.Height / fh);
            var name = _s.ProjectPath != null ? Path.GetFileNameWithoutExtension(_s.ProjectPath) : "animation";
            return new ExportSource(fw, fh, frames, dirs, RsiState.UniformDelays(dirs, frames, 0.1), name, null);
        }
        return null;
    }

    private void ExportAnimation()
    {
        _s.FlushPending();
        var doc = _s.Doc;
        if (CurrentExportSource() is not { } src)
        {
            Dialogs.ShowInfo(Loc.T("rsi.nothing_to_export_set_the"), Loc.T("rsi_dlg.animation_export"));
            return;
        }

        var info = Loc.T("rsi.sheet_dir_fr_of", src.Frames * src.Fw, src.Dirs * src.Fh, src.Dirs, src.Frames, src.Fw, src.Fh);
        var dlg = new AnimationExportViewModel(Dialogs, _s.Settings.LastExportFolder ?? "", src.Name, info, src.RowNames, doc.ExportProfile);
        if (Dialogs.ShowDialog(dlg) != true || dlg.Result is not { } r) return;
        try
        {
            ExportFiles(doc.Flatten(), r, src.Fw, src.Fh, src.Frames, src.Dirs, src.Delays, src.Name, src.Rows);
            doc.ExportProfile = r.ToProfile();   // remembered in the document (saved with the next project save)
            ExportAgainCommand.RaiseCanExecuteChanged();
            _s.Settings.LastExportFolder = r.Folder;
            _s.Settings.Save();
            _s.Status(Loc.T("rsi.animation_exported_to", r.Folder));
            _s.Services.Shell.OpenFolder(r.Folder);
        }
        catch (Exception ex) { Dialogs.ShowError(ex); }
    }

    /// <summary>"Export again": repeats the last export with the remembered settings, no dialog; the result goes to the status bar.</summary>
    private void ExportAgain()
    {
        _s.FlushPending();
        var doc = _s.Doc;
        if (doc.ExportProfile is not { } profile || CurrentExportSource() is not { } src)
        {
            Dialogs.ShowInfo(Loc.T("rsi.nothing_to_export_set_the"), Loc.T("rsi_dlg.animation_export"));
            return;
        }
        var r = AnimationExportResult.FromProfile(profile);
        if (string.IsNullOrWhiteSpace(r.Folder) || string.IsNullOrWhiteSpace(r.BaseName))
        {
            Dialogs.ShowInfo(Loc.T("rsi.export_again_no_folder"), Loc.T("rsi_dlg.animation_export"));
            return;
        }
        try
        {
            ExportFiles(doc.Flatten(), r, src.Fw, src.Fh, src.Frames, src.Dirs, src.Delays, src.Name, src.Rows);
            _s.Status(Loc.T("rsi.exported_again_to", r.BaseName, r.Folder));
        }
        catch (Exception ex) { Dialogs.ShowError(ex); }
    }

    /// <summary>Writes the sheet and the descriptions. Extracted separately for tests.</summary>
    public void ExportFiles(PixelImage sheet, AnimationExportResult r, int fw, int fh, int frames, int dirs, double[][] delays, string state, IReadOnlyList<string>? rows = null)
    {
        Directory.CreateDirectory(r.Folder);
        var sheetFile = r.BaseName + ".png";
        _s.Images.SavePng(sheet, Path.Combine(r.Folder, sheetFile));
        // a sheet of more than 8 rows without names would run out of direction names: use row1, row2...
        if (rows == null && dirs > 8) rows = Enumerable.Range(1, dirs).Select(i => $"row{i}").ToList();
        var clips = AnimationExport.Clips(state, dirs, frames, delays, rows, r.NoLoopRows);
        if (r.Aseprite)
            File.WriteAllText(Path.Combine(r.Folder, r.BaseName + ".aseprite.json"), AnimationExport.AsepriteJson(sheetFile, sheet.Width, sheet.Height, fw, fh, frames, clips));
        if (r.Godot)
            File.WriteAllText(Path.Combine(r.Folder, r.BaseName + ".tres"), AnimationExport.GodotSpriteFrames(r.GodotTexturePath, fw, fh, frames, clips));
        if (r.Simple || r.Unity)   // the Unity script reads the plain JSON
            File.WriteAllText(Path.Combine(r.Folder, r.BaseName + ".json"), AnimationExport.SimpleJson(sheetFile, fw, fh, frames, dirs, clips));
        if (r.Unity)
            File.WriteAllText(Path.Combine(r.Folder, AnimationExport.UnityScriptFileName), AnimationExport.UnityImporterScript);
        if (r.Gif && frames > 1)
            foreach (var clip in clips)
            {
                var cells = Sheet.Slice(sheet, fw, fh).Where(c => c.Row == clip.Row).OrderBy(c => c.Col).Select(c => c.Image).ToList();
                int delayMs = Math.Max(10, (int)Math.Round(clip.Delays.Average() * 1000));
                File.WriteAllBytes(Path.Combine(r.Folder, $"{clip.Name}.gif".Replace(' ', '_')), GifWriter.Encode(cells, delayMs));
            }
    }
}
