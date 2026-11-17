// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Collections.ObjectModel;
using System.Text.Json;
using PixelEditor.Core;
using PixelEditor.Core.Ops;

namespace PixelEditor.ViewModels;

/// <summary>Operations and templates (pipelines): running on a layer or on all layers, the template list, batch processing, the reference.</summary>
public sealed class AutomationViewModel : ObservableObject
{
    private sealed record LayerRun(Layer Layer, PipelineResult Result);

    private readonly EditorSession _s;
    private readonly CanvasViewModel? _canvas;
    private readonly ColorsViewModel? _colors;
    private bool _allLayers;
    private bool _selectionOnly;
    private string? _lastTuned;

    /// <summary>In an operation's JSON this text is replaced by the current primary color (black when it is empty or transparent).</summary>
    public const string PrimaryColorToken = "$primary";

    public AutomationViewModel(EditorSession session, CanvasViewModel? canvas = null, ColorsViewModel? colors = null)
    {
        _s = session;
        _canvas = canvas;
        _colors = colors;
        ApplyOperationCommand = new RelayCommand(p => ApplyOperation((string)p!));
        SnapToPaletteCommand = new RelayCommand(SnapToPalette);
        TuneOperationCommand = new RelayCommand(p => TuneOperation(p as string));
        RunTemplateCommand = new RelayCommand(p => { if (p is TemplateItem t) RunTemplate(t); else RunPipelineFile((string)p!); });
        CleanSpriteCommand = new RelayCommand(() => { if (CleanSpriteTemplate() is { } t) RunTemplate(t); });
        RunFileCommand = new RelayCommand(RunFile);
        ChatPipelineCommand = new RelayCommand(OpenChatPipeline);
        BatchCommand = new RelayCommand(() => _s.Dialogs.ShowDialog(new BatchViewModel(_s.Services, Library)));
        ReferenceCommand = new RelayCommand(() => _s.Dialogs.ShowDialog(new ReferenceViewModel(_s.Services.Shell, Library.UserDirectory)));
        TemplateLibraryCommand = new RelayCommand(() => _s.Dialogs.ShowDialog(new TemplateLibraryViewModel(_s, this)));
        NewTemplateCommand = new RelayCommand(() => { if (_s.Dialogs.ShowDialog(CreateEditor(null, false)) == true) RefreshTemplates(); });
        RefreshTemplates();
    }

    public ObservableCollection<TemplateItem> Templates { get; } = new();

    /// <summary>The same templates as menu items with a ready run command.</summary>
    public ObservableCollection<MenuEntry> TemplateMenu { get; } = new();

    /// <summary>Apply a TEMPLATE to all unlocked layers as one undo action. Single operations and the tuner always change the current layer only.</summary>
    public bool AllLayers
    {
        get => _allLayers;
        set => SetProperty(ref _allLayers, value);
    }

    /// <summary>Operations and templates change only the selected area (the rest of the layer is restored); without a selection they do nothing.</summary>
    public bool SelectionOnly
    {
        get => _selectionOnly;
        set => SetProperty(ref _selectionOnly, value);
    }

    public RelayCommand ApplyOperationCommand { get; }
    public RelayCommand TuneOperationCommand { get; }

    /// <summary>Operations → "Snap to project palette": every color goes to the nearest color of the current palette.</summary>
    public RelayCommand SnapToPaletteCommand { get; }

    private void SnapToPalette()
    {
        var palette = _colors?.Palette;
        if (palette == null || palette.Count == 0)
        {
            _s.Status(Loc.T("auto.palette_empty"));
            return;
        }
        var json = new System.Text.Json.Nodes.JsonObject { ["palette"] = new System.Text.Json.Nodes.JsonArray(palette.Select(c => (System.Text.Json.Nodes.JsonNode?)Pixel.ToHex(c)).ToArray()) };
        ApplyOperation("applyPalette", json.ToJsonString());
    }

    /// <summary>In an operation's JSON this text is replaced by the secondary color (black when it is empty or transparent).</summary>
    public const string SecondaryColorToken = "$secondary";

    /// <summary>In an operation's JSON this text is replaced by the color selected in the used-colors list, or by the secondary color when nothing is selected.</summary>
    public const string SelectedUsedColorToken = "$selectedUsed";

    private static string Hex(uint c) => Pixel.IsEmpty(c) || Pixel.A(c) == 0 ? "#000000" : $"#{c & 0xFFFFFFu:X6}";

    /// <summary>Replaces the color tokens (<see cref="PrimaryColorToken"/>, <see cref="SecondaryColorToken"/>, <see cref="SelectedUsedColorToken"/>) by hex colors.</summary>
    private string ResolveArgs(string argsJson)
    {
        if (!argsJson.Contains('$')) return argsJson;
        uint primary = _colors?.Primary ?? 0;
        uint secondary = _colors?.Secondary ?? 0;
        uint selected = _colors?.SelectedStat?.Color ?? secondary;
        return argsJson
            .Replace(PrimaryColorToken, Hex(primary), StringComparison.Ordinal)
            .Replace(SecondaryColorToken, Hex(secondary), StringComparison.Ordinal)
            .Replace(SelectedUsedColorToken, Hex(selected), StringComparison.Ordinal);
    }

    /// <summary>Id of the built-in "Clean up sprite" template (looked up by id, never by the display name).</summary>
    public const string CleanSpriteTemplateId = "system:clean-sprite";

    private TemplateItem? CleanSpriteTemplate() => Templates.FirstOrDefault(t => t.IsSystem && string.Equals(t.Id, CleanSpriteTemplateId, StringComparison.OrdinalIgnoreCase));

    /// <summary>False when the built-in template is gone: the menu item is hidden.</summary>
    public bool HasCleanSpriteTemplate => CleanSpriteTemplate() != null;

    /// <summary>Operations → "Clean up sprite": runs the built-in template through the normal template preview.</summary>
    public RelayCommand CleanSpriteCommand { get; }

    /// <summary>The inverted selection mask (non-empty = keep the original) when "only in selection" is on; null if it is off. cancelled: it is on but nothing is selected.</summary>
    private PixelImage? ProtectMask(out bool cancelled)
    {
        cancelled = false;
        if (!_selectionOnly) return null;
        var mask = _canvas?.SelectionMaskImage(inverted: true);
        if (mask == null)
        {
            _s.Status(Loc.T("canvas.no_selection"));
            cancelled = true;
        }
        return mask;
    }

    /// <summary>Puts the original pixels back wherever the protect mask is set (the same mechanism a template run uses).</summary>
    private static PixelImage RestoreProtected(PixelImage result, PixelImage original, PixelImage protect)
    {
        var o = result.Clone();
        for (int i = 0; i < o.Pixels.Length; i++)
            if (!Pixel.IsEmpty(protect.Pixels[i])) o.Pixels[i] = original.Pixels[i];
        return o;
    }

    /// <summary>The "Tune an operation" window: an operation with fields and a live Before/After. spec: "name" or "name|{json of starting parameters}".</summary>
    public void TuneOperation(string? spec)
    {
        if (!_s.CanEditActiveLayer()) return;
        string? op = null, args = null;
        if (!string.IsNullOrWhiteSpace(spec))
        {
            int sep = spec.IndexOf('|');
            op = sep < 0 ? spec : spec[..sep];
            args = sep < 0 ? null : spec[(sep + 1)..];
        }
        _s.FlushPending();
        var protect = ProtectMask(out var cancelled);
        if (cancelled) return;
        // no operation given (the generic menu item): start with the one tuned last, otherwise the first of the list
        var vm = new OperationTunerViewModel(() => _s.ActiveLayer.ToImage(), op ?? _lastTuned, args == null ? null : ResolveArgs(args), _s.Services.Timers);
        bool accepted = _s.Dialogs.ShowDialog(vm) == true;
        _lastTuned = vm.OperationName;                  // remembered even when the dialog is cancelled: it is the last operation the user looked at
        if (!accepted || vm.Result == null) return;
        try
        {
            var result = vm.Result;
            if (protect != null)
            {
                var source = _s.ActiveLayer.ToImage();
                if (result.Width != source.Width || result.Height != source.Height) { _s.Dialogs.ShowWarning(Loc.T("auto.selection_needs_same_size"), Loc.T("auto.nothing_changed")); return; }
                result = RestoreProtected(result, source, protect);
            }
            _s.ApplyResult(result, vm.OperationName);
        }
        catch (Exception ex) { _s.Dialogs.ShowError(ex); }
    }
    public RelayCommand RunTemplateCommand { get; }
    public RelayCommand RunFileCommand { get; }
    public RelayCommand ChatPipelineCommand { get; }
    public RelayCommand BatchCommand { get; }
    public RelayCommand ReferenceCommand { get; }
    public RelayCommand TemplateLibraryCommand { get; }
    public RelayCommand NewTemplateCommand { get; }

    /// <summary>Template library: system and your own.</summary>
    public PipelineLibrary Library => _s.Settings.Templates;

    /// <summary>The "Standard" and "My templates" menus.</summary>
    public ObservableCollection<MenuEntry> SystemTemplateMenu { get; } = new();
    public ObservableCollection<MenuEntry> UserTemplateMenu { get; } = new();
    public bool HasUserTemplates => Templates.Any(t => !t.IsSystem);

    public void RefreshTemplates()
    {
        Templates.Clear();
        TemplateMenu.Clear();
        SystemTemplateMenu.Clear();
        UserTemplateMenu.Clear();
        foreach (var info in Library.All())
        {
            var t = TemplateItem.From(info);
            var entry = new MenuEntry(t.Name, new RelayCommand(() => RunTemplate(t)));
            Templates.Add(t);
            TemplateMenu.Add(entry);
            (t.IsSystem ? SystemTemplateMenu : UserTemplateMenu).Add(entry);
        }
        // an empty submenu does not open in WPF and looks broken: offer to create the first template
        if (UserTemplateMenu.Count == 0) UserTemplateMenu.Add(new MenuEntry(Loc.T("menu.create_template"), NewTemplateCommand));
        OnPropertyChanged(nameof(HasUserTemplates));
        OnPropertyChanged(nameof(HasCleanSpriteTemplate));
    }

    /// <summary>The builder window: a new template, a copy of a system one (asCopy) or editing your own. The trial run goes on the current layer.</summary>
    public PipelineEditorViewModel CreateEditor(TemplateInfo? source, bool asCopy) =>
        new(Library, _s.Dialogs, () => _s.ActiveLayer.ToImage(), source, asCopy);

    public void RunTemplate(TemplateItem template) => RunPipelineFile(template.Source, preview: true);

    /// <summary>A prompt for an ordinary AI chat (with the size of the current picture). It is copied to the clipboard.</summary>
    private string BuildChatPrompt(ChatKind kind, SheetLayoutSpec? sheetLayout)
    {
        var img = _s.ActiveLayer.ToImage();
        return ChatPrompt.Build(kind, new ChatPromptImage(img.Width, img.Height, img.DistinctColors(), img.Pixels.Any(Pixel.IsEmpty)), sheetLayout: sheetLayout);
    }

    /// <summary>The sheet the document is (an animation sheet or an RSI state whose canvas still matches its binding), or null.</summary>
    private RsiBinding? CurrentSheetBinding() =>
        _s.Doc.Rsi is { } b && _s.Doc.Width == b.SheetWidth && _s.Doc.Height == b.SheetHeight ? b : null;

    private ChatSheetSetup SheetSetup() => new(_s.Settings.SheetTemplates, CurrentSheetBinding() is { } b ? SheetLayoutSpec.FromBinding(b) : null,
        _s.Settings.LastSheetTemplateId, _s.Settings.LastSheetFrameWidth, _s.Settings.LastSheetFrameHeight, _s.Settings.LastSheetDelay,
        (id, w, h, delay) =>
        {
            if (id != null) _s.Settings.LastSheetTemplateId = id;
            _s.Settings.LastSheetFrameWidth = w; _s.Settings.LastSheetFrameHeight = h; _s.Settings.LastSheetDelay = delay;
            _s.Settings.Save();
        });

    /// <summary>The picture of an answer is drawn into the current sheet (one undo step); its binding (rows, frame names, delays, RSI folder) is left alone.</summary>
    private void FillCurrentSheet(SheetResult sheet)
    {
        if (CurrentSheetBinding() == null || sheet.Image.Width != _s.Doc.Width || sheet.Image.Height != _s.Doc.Height)
        {
            _s.Dialogs.ShowError(Loc.T("chat.layout_bad"));
            return;
        }
        _s.ApplyResult(sheet.Image, Loc.T("chat.fill_current_sheet"));
        _s.Status(Loc.T("chat.sheet_filled", sheet.Rows, sheet.Columns));
    }

    /// <summary>The single "pipeline from a chat" dialog: the prompt, pasting the answer, editing the JSON, preview and comparing variants, applying.</summary>
    private void OpenChatPipeline()
    {
        _s.FlushPending();
        var vm = new ChatPipelineViewModel(_s.Clipboard, BuildChatPrompt, () => _s.ActiveLayer.ToImage(), SaveChatTemplate, _s.Services.Timers, SheetSetup());
        if (_s.Dialogs.ShowDialog(vm) != true || vm.ResultJson == null) return;
        try
        {
            switch (vm.ResultKind)
            {
                case ChatKind.Sprite when vm.ResultImage != null:
                    if (vm.Choice == ChatApply.Secondary) OpenImageAsDocument(vm.ResultImage, vm.ResultName);
                    else AddSpriteLayer(vm.ResultImage, vm.ResultName);
                    break;
                case ChatKind.Sheet when vm.ResultSheet != null:
                    if (vm.ResultFillsCurrent) FillCurrentSheet(vm.ResultSheet);
                    else OpenSheetDocument(vm.ResultSheet, vm.ResultName);
                    break;
                default:
                    RunPipelineFile(vm.ResultJson, alreadyPreviewed: true, resizeCanvas: vm.ResizeCanvas);
                    break;
            }
        }
        catch (Exception ex) { _s.Dialogs.ShowError(ex); }
    }

    /// <summary>A sprite drawn from primitives becomes a new layer at the top left corner; a canvas smaller than the sprite grows first (one undo step each).</summary>
    private void AddSpriteLayer(PixelImage sprite, string name)
    {
        var doc = _s.Doc;
        if (sprite.Width > doc.Width || sprite.Height > doc.Height)
            doc.ResizeCanvas(Math.Max(doc.Width, sprite.Width), Math.Max(doc.Height, sprite.Height));
        doc = _s.Doc;
        var pixels = new uint[doc.Width * doc.Height];
        for (int y = 0; y < sprite.Height; y++)
            for (int x = 0; x < sprite.Width; x++)
                pixels[y * doc.Width + x] = sprite.Get(x, y);
        var layer = doc.AddLayer(string.IsNullOrWhiteSpace(name) ? Loc.T("chat.sprite_layer") : name, null, pixels);
        _s.SetActiveLayer(layer);
        _s.RequestRender();
        _s.Status(Loc.T("chat.sprite_added", sprite.Width, sprite.Height));
    }

    /// <summary>The picture becomes a document of its own size (asks before dropping unsaved changes).</summary>
    private void OpenImageAsDocument(PixelImage image, string name)
    {
        if (!_s.ConfirmDiscard()) return;
        _s.SetDocument(Document.FromImage(image, string.IsNullOrWhiteSpace(name) ? null : name), null);
    }

    /// <summary>A sheet drawn from primitives opens as an animation sheet: grid cells, row names, frame names and delays are set, so it exports at once.</summary>
    private void OpenSheetDocument(SheetResult sheet, string name)
    {
        if (!_s.ConfirmDiscard()) return;
        var doc = Document.FromImage(sheet.Image);
        doc.SheetCellWidth = sheet.FrameWidth;
        doc.SheetCellHeight = sheet.FrameHeight;
        var state = string.IsNullOrWhiteSpace(name) ? "sheet" : name;
        doc.Rsi = new RsiBinding("", state, sheet.Rows, sheet.Columns, sheet.FrameWidth, sheet.FrameHeight, sheet.Delays,
            sheet.RowNames, sheet.FrameNames.Select(n => n ?? "").ToArray());
        _s.SetDocument(doc, null);
        _s.RaiseOverlaysChanged();
        _s.Status(Loc.T("chat.sheet_opened", sheet.Rows, sheet.Columns, sheet.FrameWidth, sheet.FrameHeight));
    }

    private bool SaveChatTemplate(string json)
    {
        var pipeline = Pipeline.Parse(json);
        var dlg = new NameDescriptionViewModel(Loc.T("auto.save_template"), pipeline.Name == "pipeline" ? "" : pipeline.Name, pipeline.Description ?? "",
            n => Library.Find(n) != null ? Loc.T("auto.name_taken") : null);
        if (_s.Dialogs.ShowDialog(dlg) != true) return false;
        try { Library.Save(dlg.Name, dlg.Description, json); RefreshTemplates(); return true; }
        catch (Exception ex) { _s.Dialogs.ShowError(ex); return false; }
    }

    private void RunFile()
    {
        var path = _s.Dialogs.PickOpenFile(Loc.T("auto.pipeline_json_json"));
        if (path != null) RunPipelineFile(path, preview: true);
    }

    /// <summary>A parameter of the form "name|{arguments json}".</summary>
    public void ApplyOperation(string spec)
    {
        int sep = spec.IndexOf('|');
        ApplyOperation(spec[..sep], spec[(sep + 1)..]);
    }

    public void ApplyOperation(string name, string argsJson)
    {
        if (!_s.CanEditActiveLayer()) return;
        var protect = ProtectMask(out var cancelled);
        if (cancelled) return;
        try
        {
            using var doc = JsonDocument.Parse(ResolveArgs(argsJson));
            var source = _s.ActiveLayer.ToImage();
            var result = Operations.Get(name).Run(source, new OpArgs(doc.RootElement, null));
            if (protect != null)
            {
                if (result.Width != source.Width || result.Height != source.Height) { _s.Dialogs.ShowWarning(Loc.T("auto.selection_needs_same_size"), Loc.T("auto.nothing_changed")); return; }
                result = RestoreProtected(result, source, protect);
            }
            bool changed = _s.ApplyResult(result, name);
            if (!changed && result.Width == _s.ActiveLayer.Width && result.Height == _s.ActiveLayer.Height)
                _s.Dialogs.ShowInfo(Loc.T("auto.the_operation_changed_nothing_there", OperationTitle(name)), Loc.T("auto.nothing_changed"));
        }
        catch (Exception ex) { _s.Dialogs.ShowError(ex); }
    }

    /// <summary>preview: first show the result (before/after, what changes, checks) and apply only on the button; there is no preview for several layers.</summary>
    /// <param name="alreadyPreviewed">the user already saw the result in their own dialog: no second preview, the size change is confirmed.</param>
    public void RunPipelineFile(string path, bool preview = false, bool alreadyPreviewed = false, bool resizeCanvas = false)
    {
        if (_allLayers) _s.FlushPending();
        else if (!_s.CanEditActiveLayer()) return;
        try
        {
            var pipeline = TemplateSource.Load(path);
            var targets = _allLayers ? _s.Doc.Layers.Where(l => !l.Locked).ToList() : [_s.ActiveLayer];
            if (targets.Count == 0)
            {
                _s.Status(Loc.T("auto.no_unlocked_layers"));
                return;
            }
            PixelImage? protectMask = null;
            if (_selectionOnly)
            {
                protectMask = _canvas?.SelectionMaskImage(inverted: true);
                if (protectMask == null)
                {
                    _s.Status(Loc.T("canvas.no_selection"));
                    return;
                }
            }
            var runs = targets.Select(l => new LayerRun(l, pipeline.Run(l.ToImage(), protectMask))).ToList();

            // apply nothing until all layers have run without errors
            var bad = runs.FirstOrDefault(r => !r.Result.Ok);
            if (bad != null)
            {
                var step = bad.Result.Log.First(l => l.Error != null);
                _s.Dialogs.ShowWarning(
                    (runs.Count > 1 ? Loc.T("auto.layer_prefix", bad.Layer.Name) : "") + Loc.T("auto.step_error", step.Index, step.Op, step.Error!),
                    Loc.T("auto.pipeline_stopped"));
                return;
            }

            if (_allLayers && runs.Any(r => r.Result.Image.Width != _s.Doc.Width || r.Result.Image.Height != _s.Doc.Height))
            {
                _s.Dialogs.ShowWarning(Loc.T("auto.the_template_changes_the_picture"), Loc.T("auto.pipeline"));
                return;
            }

            if (runs.Count == 1 && (preview || alreadyPreviewed))
            {
                var run = runs[0];
                if (!alreadyPreviewed)
                {
                    var dlg = new ResultPreviewViewModel(pipeline.Name, run.Layer.ToImage(), run.Result);
                    if (_s.Dialogs.ShowDialog(dlg) != true) return;
                }
                if (_allLayers) ApplyToAllLayers(pipeline, runs);
                else if (resizeCanvas) _s.ApplyResultResizingCanvas(run.Result.Image, pipeline.Name);
                else _s.ApplyResult(run.Result.Image, pipeline.Name, sizeChangeConfirmed: true);
                _s.Status(Loc.T("auto.template_applied", pipeline.Name));
                return;
            }
            if (!_allLayers)
                _s.ApplyResult(runs[0].Result.Image, pipeline.Name);
            else
                ApplyToAllLayers(pipeline, runs);

            ReportChecks(pipeline, runs);
        }
        catch (Exception ex) { _s.Dialogs.ShowError(ex); }
    }

    private static string OperationTitle(string name) => Operations.All.FirstOrDefault(o => o.Name == name)?.Description.Split('.')[0] ?? name;

    private void ApplyToAllLayers(Pipeline pipeline, List<LayerRun> runs)
    {
        var doc = _s.Doc;
        var actions = new List<IUndoAction>();
        int changed = 0;
        foreach (var r in runs)
        {
            var b = new PixelBatch(r.Layer);
            EditorSession.Stamp(b, r.Result.Image);
            changed += b.Count;
            var a = b.ToAction();
            if (a != null) actions.Add(a);
        }
        if (actions.Count > 0) doc.History.Push(new CompositeAction(actions)); else _s.RequestRender();
        _s.Status(Loc.T("auto.template_applied_to_layers_pixels", pipeline.Name, runs.Count, changed));
    }

    private void ReportChecks(Pipeline pipeline, List<LayerRun> runs)
    {
        if (!runs.Any(r => r.Result.Checks.Count > 0)) return;

        var failed = runs.SelectMany(r => r.Result.Checks.Where(c => !c.Passed)
            .Select(c => $"{(runs.Count > 1 ? r.Layer.Name + ": " : "")}{(c.Required ? Loc.T("auto.check_failed") : Loc.T("auto.check_warning"))}: {c.Check}: {c.Message}")).ToList();
        if (failed.Count == 0)
        {
            _s.Status(Loc.T("auto.applied_checks_passed", pipeline.Name));
            return;
        }

        var text = string.Join(Environment.NewLine, failed) + Environment.NewLine + Environment.NewLine +
                   Loc.T("auto.the_result_is_applied_and");
        var title = Loc.T("auto.checks_of_template", pipeline.Name);
        if (runs.All(r => r.Result.ChecksPassed)) _s.Dialogs.ShowInfo(text, title);
        else _s.Dialogs.ShowWarning(text, title);
    }
}
