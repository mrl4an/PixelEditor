// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Collections.ObjectModel;
using System.Text.Json;
using PixelEditor.Core;
using PixelEditor.Core.Ops;

namespace PixelEditor.ViewModels;

/// <summary>A variant to compare: one chat answer (a pipeline, a sprite or a sheet) and its result.</summary>
public sealed class ChatVariant
{
    public ChatVariant(string name, string json, PixelImage after, string info, bool checksOk)
    {
        Name = name; Json = json; Key = PipelineText.Pretty(json); After = after; Info = info; ChecksOk = checksOk;
    }

    public string Name { get; }
    public string Json { get; }
    public string Key { get; }
    public PixelImage After { get; }
    public string Info { get; }
    public bool ChecksOk { get; }
    public string Mark => ChecksOk ? "✓" : "!";
}

/// <summary>Which of the two apply buttons was pressed.</summary>
public enum ChatApply { Primary, Secondary }

/// <summary>One entry of the sheet layout selector in the chat dialog: a template, or null for "any layout" (the AI decides).</summary>
public sealed record ChatLayoutChoice(string Id, string Name, SheetTemplate? Template);

/// <summary>
/// What the chat dialog needs to ask for a sheet of a given layout: the template library for the selector, the layout of the current animation sheet
/// or RSI state (when there is one, it is REQUIRED and the selector is hidden), the last choice, and a callback to remember a choice.
/// </summary>
/// <param name="DocumentLayout">The layout of the current sheet document (null: the document is not a sheet or its canvas no longer matches).</param>
/// <param name="Remember">Called with (template id, frame width, frame height, delay override) when the prompt is copied or the answer applied.</param>
public sealed record ChatSheetSetup(SheetTemplateLibrary? Templates, SheetLayoutSpec? DocumentLayout = null, string? LastTemplateId = null,
    int FrameWidth = 32, int FrameHeight = 32, double? Delay = null, Action<string?, int, int, double?>? Remember = null);

/// <summary>
/// The single "from an AI chat" window: copying a prompt (process the picture / draw a sprite / draw a sprite sheet), pasting the answer, EDITING the JSON by hand,
/// live validation, a live preview and several variants side by side (paste different chat answers or edits and compare).
/// The kind of the answer is detected from the JSON: a pipeline (runs on the current layer), a sprite scene or a sheet of frames (drawn from primitives, no reference).
/// Applying closes the window; the editor then runs the pipeline, adds the sprite as a layer or opens the sheet as an animation sheet.
/// </summary>
public sealed class ChatPipelineViewModel : DialogViewModel
{
    private readonly IClipboardService _clipboard;
    private readonly Func<ChatKind, SheetLayoutSpec?, string> _promptFactory;
    private readonly ChatSheetSetup? _setup;
    private ChatLayoutChoice? _layoutChoice;
    private string _layoutW = "32", _layoutH = "32", _layoutDelay = "";
    private readonly Func<PixelImage> _source;
    private readonly Func<string, bool>? _saveTemplate;
    private readonly IUiTimer? _debounce;
    private string _text = "";
    private string _status = "";
    private string _notice = "";
    private string? _json;
    private PipelineProblems? _problems;
    private PixelImage? _before, _after;
    private SheetResult? _sheet;
    private ChatKind _kind = ChatKind.Pipeline;
    private ChatKind _promptKind = ChatKind.Pipeline;
    private string _previewSummary = "", _previewDetails = "";
    private bool _previewOk, _checksOk;
    private ChatVariant? _selected;
    private bool _loadingVariant;
    private bool _previewStale;           // the text changed and the delayed recompute has not fired yet

    /// <param name="promptFactory">Builds the prompt for the chat of the chosen kind (with data about the current picture for a pipeline).</param>
    /// <param name="source">The picture a pipeline preview is computed on (the current layer).</param>
    /// <param name="saveTemplate">Saves the pipeline to the library (shows its own name dialog); null: no button.</param>
    /// <param name="timers">For the delayed preview recompute while typing (null: recompute at once).</param>
    /// <param name="sheet">For "Draw a sheet of frames": the layout selector and the required layout (null: the classic free prompt).</param>
    public ChatPipelineViewModel(IClipboardService clipboard, Func<ChatKind, SheetLayoutSpec?, string> promptFactory, Func<PixelImage> source,
        Func<string, bool>? saveTemplate = null, IUiTimerFactory? timers = null, ChatSheetSetup? sheet = null)
    {
        _clipboard = clipboard;
        _promptFactory = promptFactory;
        _source = source;
        _saveTemplate = saveTemplate;
        _setup = sheet;
        if (sheet != null)
        {
            _layoutW = sheet.FrameWidth.ToString(); _layoutH = sheet.FrameHeight.ToString();
            _layoutDelay = sheet.Delay is { } sd ? sd.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) : "";
            if (sheet.Templates != null)
            {
                LayoutChoices.Add(new ChatLayoutChoice("", Loc.T("chat.layout_free"), null));
                foreach (var t in sheet.Templates.All()) LayoutChoices.Add(new ChatLayoutChoice(t.Id, t.Name, t));
                _layoutChoice = LayoutChoices.FirstOrDefault(c => c.Id == sheet.LastTemplateId) ?? LayoutChoices.FirstOrDefault(c => c.Id == "system:walk-4") ?? LayoutChoices[0];
            }
        }
        if (timers != null)
        {
            _debounce = timers.Create(TimeSpan.FromMilliseconds(600));
            _debounce.Tick += () => { _debounce.Stop(); RunPreview(); };
        }
        PasteCommand = new RelayCommand(Paste);
        CopyPromptCommand = new RelayCommand(CopyPrompt);
        AddVariantCommand = new RelayCommand(AddVariant, () => CanAddVariant);
        RemoveVariantCommand = new RelayCommand(RemoveVariant, () => _selected != null);
        ApplyCommand = new RelayCommand(() => Apply(ChatApply.Primary), () => CanApply);
        ApplySecondaryCommand = new RelayCommand(() => Apply(ChatApply.Secondary), () => CanApply && HasSecondary);
        SaveTemplateCommand = new RelayCommand(() => { if (_json != null && _saveTemplate!(_json)) Notice = Loc.T("chat.saved_to_library"); },
            () => CanApply && _saveTemplate != null && _kind == ChatKind.Pipeline);
        Revalidate(runPreviewNow: true);
    }

    /// <summary>For the common case: one prompt for the picture (the kind is ignored).</summary>
    public ChatPipelineViewModel(IClipboardService clipboard, Func<string> promptFactory, Func<PixelImage> source,
        Func<string, bool>? saveTemplate = null, IUiTimerFactory? timers = null)
        : this(clipboard, (_, _) => promptFactory(), source, saveTemplate, timers) { }

    /// <summary>The prompt factory only needs the kind (no sheet layout).</summary>
    public ChatPipelineViewModel(IClipboardService clipboard, Func<ChatKind, string> promptFactory, Func<PixelImage> source,
        Func<string, bool>? saveTemplate = null, IUiTimerFactory? timers = null)
        : this(clipboard, (k, _) => promptFactory(k), source, saveTemplate, timers) { }

    public override string Title => Loc.T("chat.title");

    /// <summary>The editable text: the whole chat answer or clean JSON (it can be edited by hand).</summary>
    public string Text
    {
        get => _text;
        set { if (SetProperty(ref _text, value ?? "")) { if (!_loadingVariant) Select(null); Revalidate(runPreviewNow: false); } }
    }

    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string Notice { get => _notice; private set => SetProperty(ref _notice, value); }
    public ObservableCollection<string> Messages { get; } = new();
    public ObservableCollection<ChatVariant> Variants { get; } = new();

    /// <summary>What the user asks the chat for: the prompt that "Copy the prompt" puts on the clipboard.</summary>
    public ChatKind PromptKind
    {
        get => _promptKind;
        set { if (SetProperty(ref _promptKind, value)) LayoutChanged(); }
    }

    // ---- the layout of a requested sheet ----

    /// <summary>The templates of the selector ("any layout" first).</summary>
    public ObservableCollection<ChatLayoutChoice> LayoutChoices { get; } = new();

    public ChatLayoutChoice? SelectedLayout
    {
        get => _layoutChoice;
        set { if (value != null && SetProperty(ref _layoutChoice, value)) LayoutChanged(); }
    }

    public string LayoutWidthText { get => _layoutW; set { if (SetProperty(ref _layoutW, value ?? "")) LayoutChanged(); } }
    public string LayoutHeightText { get => _layoutH; set { if (SetProperty(ref _layoutH, value ?? "")) LayoutChanged(); } }

    /// <summary>An optional delay (seconds, comma or dot) that replaces every frame delay of the chosen template.</summary>
    public string LayoutDelayText { get => _layoutDelay; set { if (SetProperty(ref _layoutDelay, value ?? "")) LayoutChanged(); } }

    /// <summary>The current document is an animation sheet or RSI state: its layout is required and fixed.</summary>
    public bool HasDocumentLayout => _setup?.DocumentLayout != null;

    /// <summary>Show the layout selector: sheet kind, and no fixed layout from the document.</summary>
    public bool ShowLayoutSelector => _promptKind == ChatKind.Sheet && !HasDocumentLayout && _setup?.Templates != null;

    public bool ShowDocumentLayout => _promptKind == ChatKind.Sheet && HasDocumentLayout;
    public string DocumentLayoutText => _setup?.DocumentLayout is { } l ? Loc.T("chat.layout_from_sheet", l.Describe()) : "";
    public string LayoutRuleText => Loc.T("chat.layout_rule");

    /// <summary>A required layout exists (so the rule text applies): from the document or from the selector.</summary>
    public bool HasTargetLayout => ShowDocumentLayout || (ShowLayoutSelector && SelectedLayout?.Template != null);

    /// <summary>The layout the prompt asks for and the answer is checked against; null: free (any layout) or an invalid selector value.</summary>
    public SheetLayoutSpec? TargetLayout => ComputeTarget(out _);

    private SheetLayoutSpec? ComputeTarget(out bool invalid)
    {
        invalid = false;
        if (_promptKind != ChatKind.Sheet) return null;
        if (_setup?.DocumentLayout is { } doc) return doc;
        if (_setup?.Templates == null || _layoutChoice?.Template is not { } t) return null;
        if (!int.TryParse(_layoutW.Trim(), out var w) || !int.TryParse(_layoutH.Trim(), out var h) || w < 1 || h < 1 || w > 256 || h > 256) { invalid = true; return null; }
        double? delay = null;
        if (_layoutDelay.Trim().Length > 0)
        {
            if (!double.TryParse(_layoutDelay.Trim().Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) || !(d > 0 && d <= 60)) { invalid = true; return null; }
            delay = d;
        }
        return SheetLayoutSpec.FromTemplate(t, w, h, delay);
    }

    private void LayoutChanged()
    {
        OnPropertyChanged(nameof(ShowLayoutSelector));
        OnPropertyChanged(nameof(ShowDocumentLayout));
        OnPropertyChanged(nameof(HasTargetLayout));
        OnPropertyChanged(nameof(TargetLayout));
        OnPropertyChanged(nameof(LayoutError));
        if (_kind == ChatKind.Sheet && IsValid) Revalidate(runPreviewNow: true);   // the answer is checked against the new layout
    }

    /// <summary>Shown under the selector when the size or the delay is not a number in range.</summary>
    public string LayoutError { get { ComputeTarget(out var bad); return bad ? Loc.T("chat.layout_bad") : ""; } }

    private void RememberLayout()
    {
        if (_setup?.Remember == null || _setup.DocumentLayout != null || _layoutChoice == null) return;
        ComputeTarget(out var bad);
        if (bad) return;
        int.TryParse(_layoutW.Trim(), out var w); int.TryParse(_layoutH.Trim(), out var h);
        double? delay = double.TryParse(_layoutDelay.Trim().Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) && d > 0 ? d : null;
        _setup.Remember(_layoutChoice.Template?.Id, w, h, delay);
    }

    /// <summary>What the current JSON is (detected): a pipeline, a sprite or a sheet.</summary>
    public ChatKind Kind => _kind;
    public bool IsPipeline => _kind == ChatKind.Pipeline;

    /// <summary>The selected variant: its JSON is loaded into the editor.</summary>
    public ChatVariant? SelectedVariant
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value)) return;
            RemoveVariantCommand.RaiseCanExecuteChanged();
            if (value == null) return;
            _loadingVariant = true;
            try { Text = value.Json; }
            finally { _loadingVariant = false; }
            Revalidate(runPreviewNow: true);
        }
    }

    public bool IsValid => _problems is { IsValid: true } && _json != null;
    public bool HasMessages => Messages.Count > 0;
    public bool CanSave => _saveTemplate != null;
    public bool HasVariants => Variants.Count > 0;

    /// <summary>The answer passed validation, the preview is built and no step failed.</summary>
    public bool CanApply => IsValid && (_previewOk || _previewStale);

    /// <summary>The "before" picture exists only for a pipeline: sprites and sheets are drawn from nothing.</summary>
    public PixelImage? Before => _before;
    public PixelImage? After => _after;
    public bool HasPreview => _after != null;
    public bool ShowBefore => _kind == ChatKind.Pipeline;
    public string PreviewSummary { get => _previewSummary; private set => SetProperty(ref _previewSummary, value); }
    public string PreviewDetails { get => _previewDetails; private set => SetProperty(ref _previewDetails, value); }
    public bool ChecksOk => _checksOk;

    /// <summary>The caption of the main button.</summary>
    public string ApplyText => _kind switch
    {
        ChatKind.Sprite => Loc.T("chat.add_as_layer"),
        ChatKind.Sheet => CanFillCurrent ? Loc.T("chat.fill_current_sheet") : Loc.T("chat.open_as_sheet"),
        _ => ChangesSize ? Loc.T("result_preview.open_in_a_new_document") : Loc.T("template_lib.apply_to_layer"),
    };

    /// <summary>The second button: a pipeline whose result has another size can resize the canvas instead; a sprite can open as a new document.</summary>
    public bool HasSecondary => _kind switch
    {
        ChatKind.Pipeline => ChangesSize,
        ChatKind.Sprite => _after != null,
        ChatKind.Sheet => CanFillCurrent,
        _ => false,
    };

    public string SecondaryText => _kind switch
    {
        ChatKind.Sprite => Loc.T("chat.open_sprite_new_document"),
        ChatKind.Sheet => Loc.T("chat.open_new_sheet"),
        _ => Loc.T("chat.resize_canvas_and_apply"),
    };

    public string SecondaryTip => _kind switch
    {
        ChatKind.Sprite => Loc.T("chat.open_sprite_new_document.tip"),
        ChatKind.Sheet => Loc.T("chat.open_new_sheet.tip"),
        _ => Loc.T("chat.resize_canvas_and_apply.tip"),
    };

    /// <summary>The answer (clean JSON) to apply after confirmation.</summary>
    public string? ResultJson { get; private set; }

    /// <summary>What the confirmed answer is.</summary>
    public ChatKind ResultKind { get; private set; }

    /// <summary>The button that closed the dialog.</summary>
    public ChatApply Choice { get; private set; }

    /// <summary>The drawn picture of a sprite or of a whole sheet (null for a pipeline).</summary>
    public PixelImage? ResultImage { get; private set; }

    /// <summary>The sheet with its row names, frame names and delays (null unless the answer is a sheet).</summary>
    public SheetResult? ResultSheet { get; private set; }

    /// <summary>The name given in the answer (or a default one).</summary>
    public string ResultName { get; private set; } = "";

    public RelayCommand PasteCommand { get; }
    public RelayCommand CopyPromptCommand { get; }
    public RelayCommand AddVariantCommand { get; }
    public RelayCommand RemoveVariantCommand { get; }
    public RelayCommand ApplyCommand { get; }
    public RelayCommand ApplySecondaryCommand { get; }
    public RelayCommand SaveTemplateCommand { get; }

    /// <summary>The result of a pipeline has a different size than the layer: the layer cannot fit it without changing the canvas.</summary>
    public bool ChangesSize => _kind == ChatKind.Pipeline && _before != null && _after != null && (_before.Width != _after.Width || _before.Height != _after.Height);

    /// <summary>"Resize canvas and apply" was chosen for a pipeline (otherwise a result of another size opens as a new document).</summary>
    public bool ResizeCanvas => ResultKind == ChatKind.Pipeline && Choice == ChatApply.Secondary;

    private void Paste()
    {
        var text = _clipboard.GetText();
        if (string.IsNullOrWhiteSpace(text)) { Notice = Loc.T("chat.clipboard_has_no_text"); return; }
        PasteText(text);
    }

    /// <summary>A pasted answer: clean indented JSON remains and the ready result is remembered as a variant at once (identical ones are not repeated).</summary>
    public void PasteText(string text)
    {
        Notice = "";
        Text = text;
        Normalize();
        if (!CanApply) return;
        RunPreviewIfStale();
        var same = Variants.FirstOrDefault(v => v.Key == Key(_json!));
        if (same != null) { SelectVariantQuietly(same); return; }
        AddVariant();
    }

    private void RunPreviewIfStale() { if (_previewStale) { _debounce?.Stop(); RunPreview(); } }

    private void SelectVariantQuietly(ChatVariant v)
    {
        _loadingVariant = true;
        try { SelectedVariant = v; }
        finally { _loadingVariant = false; }
        AddVariantCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanAddVariant));
    }

    /// <summary>Independent of whitespace: the same answer in a different layout counts as one variant.</summary>
    private static string Key(string json) => PipelineText.Pretty(json);

    /// <summary>The "keep" button is only needed for a new result: on an already saved variant it would breed copies.</summary>
    public bool CanAddVariant => CanApply && _json != null && !Variants.Any(v => v.Key == Key(_json));

    /// <summary>Brings the text to clean indented JSON (called on paste and when the box loses focus).</summary>
    public void Normalize()
    {
        if (_json == null) return;
        var pretty = PipelineText.Pretty(_json);
        if (pretty != _text) { _loadingVariant = true; try { Text = pretty; } finally { _loadingVariant = false; } }
        Revalidate(runPreviewNow: true);
        var match = Variants.FirstOrDefault(v => v.Key == Key(_json!));
        if (match != null) SelectVariantQuietly(match);
    }

    private void CopyPrompt()
    {
        var layout = ComputeTarget(out var invalid);
        if (invalid) { Notice = Loc.T("chat.layout_bad"); return; }
        if (_promptKind == ChatKind.Sheet) RememberLayout();
        Notice = _clipboard.TrySetText(_promptFactory(_promptKind, layout)) ? Loc.T("chat.prompt_copied") : Loc.T("chat.could_not_copy");
    }

    /// <summary>The answer is a sheet that has the grid of the current sheet document: it can be drawn into that document, keeping its rows, frame names and delays.</summary>
    public bool CanFillCurrent => _kind == ChatKind.Sheet && _sheet != null && _promptKind == ChatKind.Sheet && _setup?.DocumentLayout is { } l && l.FitsGrid(_sheet);

    /// <summary>The main button was "draw into this sheet" (otherwise the sheet opens as a new document).</summary>
    public bool ResultFillsCurrent { get; private set; }

    private static string NameOf(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
                return n.GetString() ?? "";
        }
        catch (JsonException) { }
        return "";
    }

    private void AddVariant()
    {
        if (_previewStale) { _debounce?.Stop(); RunPreview(); }
        if (!CanAddVariant || _after == null) return;
        var name = NameOf(_json!);
        if (name.Length == 0 || name == "pipeline") name = Loc.T("chat.variant");
        string unique = name;
        for (int n = 2; Variants.Any(v => v.Name == unique); n++) unique = $"{name} {n}";
        var variant = new ChatVariant(unique, _json!, _after, _previewSummary, _checksOk);
        Variants.Add(variant);
        OnPropertyChanged(nameof(HasVariants));
        _loadingVariant = true;
        try { SelectedVariant = variant; }
        finally { _loadingVariant = false; }
        AddVariantCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanAddVariant));
        Notice = Loc.T("chat.variant_added", unique);
    }

    private void RemoveVariant()
    {
        if (_selected == null) return;
        var gone = _selected;
        _selected = null;
        Variants.Remove(gone);
        OnPropertyChanged(nameof(SelectedVariant));
        OnPropertyChanged(nameof(HasVariants));
        RemoveVariantCommand.RaiseCanExecuteChanged();
        AddVariantCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanAddVariant));
    }

    private void Select(ChatVariant? v)
    {
        if (_selected == null && v == null) return;
        _selected = v;
        OnPropertyChanged(nameof(SelectedVariant));
        RemoveVariantCommand.RaiseCanExecuteChanged();
    }

    private void Apply(ChatApply choice)
    {
        if (_previewStale) { _debounce?.Stop(); RunPreview(); }       // pressed right after an edit: finish the computation so that exactly the visible text is applied
        if (!CanApply) return;
        if (choice == ChatApply.Secondary && !HasSecondary) return;
        ResultJson = _json;
        ResultKind = _kind;
        Choice = choice;
        ResultImage = _kind == ChatKind.Pipeline ? null : _after;
        ResultSheet = _kind == ChatKind.Sheet ? _sheet : null;
        ResultFillsCurrent = _kind == ChatKind.Sheet && choice == ChatApply.Primary && CanFillCurrent;
        if (_kind == ChatKind.Sheet) RememberLayout();
        ResultName = NameOf(_json!);
        Close(true);
    }

    private void Revalidate(bool runPreviewNow)
    {
        Messages.Clear();
        _json = null;
        _problems = null;
        if (string.IsNullOrWhiteSpace(_text))
        {
            Status = Loc.T("chat.waiting_for_the_answer");
            SetKind(ChatKind.Pipeline);
        }
        else
        {
            try
            {
                _json = PipelineText.Extract(_text);
                SetKind(PipelineText.Detect(_json));
                _problems = PipelineText.Check(_json);
                foreach (var e in _problems.Errors) Messages.Add("✖ " + e);
                foreach (var w in _problems.Warnings) Messages.Add("⚠ " + w);
                if (!_problems.IsValid) Status = Loc.T("chat.problems_found", _problems.Errors.Count);
                else if (_kind == ChatKind.Pipeline)
                {
                    var pipeline = Pipeline.Parse(_json);
                    Status = Loc.T("chat.ready", pipeline.Steps.Count, pipeline.CheckSteps.Count);
                }
                else Status = _kind == ChatKind.Sprite ? Loc.T("chat.ready_sprite") : Loc.T("chat.ready_sheet");
            }
            catch (Exception ex) when (ex is FormatException or JsonException)
            {
                _problems = new PipelineProblems([ex.Message], []);
                Messages.Add("✖ " + ex.Message);
                Status = Loc.T("chat.problems_found", 1);
            }
        }
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(HasMessages));

        if (!IsValid) { ClearPreview(); return; }
        if (runPreviewNow || _debounce == null) { _debounce?.Stop(); RunPreview(); }
        else { _previewStale = true; _debounce.Stop(); _debounce.Start(); RaisePreview(); }          // typing is in progress: recompute when the user stops
    }

    private void SetKind(ChatKind kind)
    {
        if (_kind == kind) return;
        _kind = kind;
        OnPropertyChanged(nameof(Kind));
        OnPropertyChanged(nameof(IsPipeline));
        OnPropertyChanged(nameof(ShowBefore));
    }

    private void ClearPreview()
    {
        _previewStale = false;
        _after = null; _before = null; _sheet = null; _previewOk = false; _checksOk = false;
        PreviewSummary = ""; PreviewDetails = "";
        RaisePreview();
    }

    /// <summary>Runs the current answer: a pipeline on a copy of the current picture (the layer does not change), a sprite or a sheet is drawn from nothing.</summary>
    public void RunPreview()
    {
        _previewStale = false;
        if (!IsValid || _json == null) { ClearPreview(); return; }
        try
        {
            switch (_kind)
            {
                case ChatKind.Sprite: PreviewSprite(); break;
                case ChatKind.Sheet: PreviewSheet(); break;
                default: PreviewPipeline(); break;
            }
        }
        catch (Exception ex)
        {
            _after = null; _previewOk = false;
            Messages.Add("✖ " + ex.Message);
            OnPropertyChanged(nameof(HasMessages));
        }
        RaisePreview();
    }

    private void PreviewPipeline()
    {
        _sheet = null;
        var before = _source();
        var result = Pipeline.Parse(_json!).Run(before);
        _before = before;
        _after = result.Image;
        _previewOk = result.Ok;
        _checksOk = result.ChecksPassed;
        int cb = ColorStats.Count(before).Count, ca = ColorStats.Count(_after).Count;
        PreviewSummary = Loc.T("chat.preview_summary", before.Width, before.Height, _after.Width, _after.Height, cb, ca);
        var lines = result.Log.Select(l => l.Error != null ? $"✗ {l.Index}. {l.Op}: {l.Error}" : $"✓ {l.Index}. {l.Op}: {l.Width}×{l.Height}, {l.Colors}")
            .Concat(result.Checks.Select(c => $"{(c.Passed ? "✓" : c.Required ? "✗" : "!")} {c.Check}: {c.Message}"));
        PreviewDetails = string.Join(Environment.NewLine, lines);
        if (!result.Ok)
        {
            var bad = result.Log.First(l => l.Error != null);
            Messages.Add("✖ " + Loc.T("chat.step_failed", bad.Index, bad.Op, bad.Error ?? ""));
            OnPropertyChanged(nameof(HasMessages));
        }
    }

    private void PreviewSprite()
    {
        _before = null; _sheet = null;
        _after = Scene.Render(_json!);
        _previewOk = true; _checksOk = true;
        PreviewSummary = Loc.T("chat.sprite_summary", _after.Width, _after.Height, ColorStats.Count(_after).Count);
        PreviewDetails = "";
    }

    private void PreviewSheet()
    {
        _before = null;
        var raw = SheetScene.Render(_json!);
        // the rule: what the answer gives wins; frame names and delays it omits come from the requested layout (the user's choice)
        var target = TargetLayout;
        _sheet = target != null ? target.Complete(raw) : raw;
        if (target != null && target.Differences(raw) is { Count: > 0 } diff)
        {
            Messages.Add(Loc.T("chat.layout_differs", string.Join("; ", diff)));
            OnPropertyChanged(nameof(HasMessages));
        }
        _after = _sheet.Image;
        _previewOk = true; _checksOk = true;
        PreviewSummary = Loc.T("chat.sheet_summary", _after.Width, _after.Height, _sheet.FrameWidth, _sheet.FrameHeight, _sheet.Rows, _sheet.Columns);
        var lines = new List<string>();
        for (int r = 0; r < _sheet.Rows; r++)
            lines.Add($"{_sheet.RowNames[r]}: " + string.Join(", ", Enumerable.Range(0, _sheet.Columns)
                .Select(c => $"{(string.IsNullOrEmpty(_sheet.FrameNames[c]) ? (c + 1).ToString() : _sheet.FrameNames[c])} {_sheet.Delays[r][c]:0.###}s")));
        PreviewDetails = string.Join(Environment.NewLine, lines);
    }

    private void RaisePreview()
    {
        OnPropertyChanged(nameof(Before));
        OnPropertyChanged(nameof(After));
        OnPropertyChanged(nameof(HasPreview));
        OnPropertyChanged(nameof(ChecksOk));
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(CanAddVariant));
        OnPropertyChanged(nameof(ApplyText));
        OnPropertyChanged(nameof(CanFillCurrent));
        OnPropertyChanged(nameof(ChangesSize));
        OnPropertyChanged(nameof(HasSecondary));
        OnPropertyChanged(nameof(SecondaryText));
        OnPropertyChanged(nameof(SecondaryTip));
        ApplyCommand.RaiseCanExecuteChanged();
        ApplySecondaryCommand.RaiseCanExecuteChanged();
        AddVariantCommand.RaiseCanExecuteChanged();
        SaveTemplateCommand.RaiseCanExecuteChanged();
    }

    protected override void OnClosed() => _debounce?.Stop();
}
