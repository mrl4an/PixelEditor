// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using PixelEditor.Core;
using PixelEditor.Core.Ops;

namespace PixelEditor.ViewModels;

public sealed record Choice(string Key, string Title);

/// <summary>
/// Import of an external picture: the downscale, color and cleanup settings are assembled into a JSON pipeline that runs
/// with the same operations as templates. The pipeline is visible to the user and is saved as a template.
/// </summary>
public sealed class ImportViewModel : DialogViewModel
{
    private readonly IDialogService _dialogs;
    private readonly PixelImage _source;
    private readonly PixelImage _trimmed;
    private readonly IReadOnlyList<uint> _palette;
    private readonly Debouncer _recompute;
    private bool _loading = true;
    private bool _syncingSize;

    private string _width = "", _height = "";
    private bool _keepAspect = true, _trim;
    private Choice _method;
    private string _alpha = "128", _maxColors = "0";
    private bool _usePalette, _dither, _mergeRare, _removeIslands, _fillHoles, _outline;
    private Choice _target;
    private PixelImage? _preview;
    private string _resultInfo = "", _pipelineJson = "";
    private bool _canImport;

    public ImportViewModel(IDialogService dialogs, IUiTimerFactory? timers, PixelImage source, string name,
        IReadOnlyList<uint> palette, bool canAddLayer)
    {
        _dialogs = dialogs;
        _source = source;
        _trimmed = Transform.Trim(source);
        _palette = palette;
        SourceInfo = $"{name}: {source.Width}×{source.Height} px";

        Methods =
        [
            new Choice("none", Loc.T("import.no_resizing_original")),
            new Choice("box", Loc.T("import.averaging_box_photos_gradients")),
            new Choice("mode", Loc.T("import.most_frequent_color_mode_pixel")),
            new Choice("smart", Loc.T("import.edge_preserving_smart_crisp_outlines")),
            new Choice("nearest", Loc.T("import.nearest_nearest_fast")),
        ];
        _method = Methods[0];

        var targets = new List<Choice> { new(nameof(ImportTarget.NewDocument), Loc.T("import.to_a_new_document")) };
        if (canAddLayer) targets.Add(new Choice(nameof(ImportTarget.NewLayer), Loc.T("import.as_a_new_layer_in")));
        Targets = targets;
        _target = targets[0];

        _recompute = new Debouncer(timers, TimeSpan.FromMilliseconds(180), Recompute);
        ImportCommand = new RelayCommand(Import, () => _canImport);
        SaveTemplateCommand = new RelayCommand(SaveTemplate);

        OriginalSizeCommand = new RelayCommand(ResetToOriginalSize);
        SetLongestSideCommand = new RelayCommand(p => SetLongestSide(Convert.ToInt32(p)));

        // by default offer the picture as is: the original size, no cropping or processing
        var (bw, bh) = BaseSize();
        _syncingSize = true;
        _width = bw.ToString();
        _height = bh.ToString();
        _syncingSize = false;

        _loading = false;
        Recompute();
    }

    /// <summary>Return the original size (taking the trimming of empty edges into account if it is on).</summary>
    public RelayCommand OriginalSizeCommand { get; }

    /// <summary>Shrink so the long side equals the parameter (for example 32 or 64), keeping proportions.</summary>
    public RelayCommand SetLongestSideCommand { get; }

    private void ResetToOriginalSize()
    {
        _method = Methods[0];
        OnPropertyChanged(nameof(SelectedMethod));
        var (bw, bh) = BaseSize();
        SetSize(bw, bh);
    }

    private void SetLongestSide(int side)
    {
        if (side < 1) return;
        LeaveNone();
        var (bw, bh) = BaseSize();
        double k = (double)side / Math.Max(bw, bh);
        SetSize(Math.Max(1, (int)Math.Round(bw * k)), Math.Max(1, (int)Math.Round(bh * k)));
    }

    /// <summary>As soon as the size is changed by hand or with a preset, the "no resize" method is replaced with averaging.</summary>
    private void LeaveNone()
    {
        if (_method.Key != "none") return;
        _method = Methods[1];
        OnPropertyChanged(nameof(SelectedMethod));
    }

    private void SetSize(int w, int h)
    {
        _syncingSize = true;
        WidthText = w.ToString();
        HeightText = h.ToString();
        _syncingSize = false;
        Schedule();
    }

    public override string Title => Loc.T("import.import_image");
    public string SourceInfo { get; }
    public IReadOnlyList<Choice> Methods { get; }
    public IReadOnlyList<Choice> Targets { get; }
    public bool UsePaletteEnabled => _palette.Count > 0;

    // ---- size ----

    public string WidthText
    {
        get => _width;
        set
        {
            if (!SetProperty(ref _width, value) || _loading || _syncingSize) return;
            LeaveNone();
            if (_keepAspect && int.TryParse(value, out var w) && w > 0)
            {
                var (bw, bh) = BaseSize();
                SyncHeight(Math.Max(1, (int)Math.Round((double)bh * w / bw)));
            }
            Schedule();
        }
    }

    public string HeightText
    {
        get => _height;
        set
        {
            if (!SetProperty(ref _height, value) || _loading || _syncingSize) return;
            LeaveNone();
            if (_keepAspect && int.TryParse(value, out var h) && h > 0)
            {
                var (bw, bh) = BaseSize();
                SyncWidth(Math.Max(1, (int)Math.Round((double)bw * h / bh)));
            }
            Schedule();
        }
    }

    public bool KeepAspect { get => _keepAspect; set => SetProperty(ref _keepAspect, value); }

    /// <summary>Trimming empty edges changes the original proportions, so with the link on it recomputes the height.</summary>
    public bool TrimEmpty
    {
        get => _trim;
        set
        {
            if (!SetProperty(ref _trim, value) || _loading) return;
            if (_keepAspect && int.TryParse(_width, out var w) && w > 0)
            {
                var (bw, bh) = BaseSize();
                SyncHeight(Math.Max(1, (int)Math.Round((double)bh * w / bw)));
            }
            Schedule();
        }
    }

    public Choice SelectedMethod
    {
        get => _method;
        set
        {
            if (value == null || !SetProperty(ref _method, value)) return;
            if (value.Key == "none") { var (bw, bh) = BaseSize(); SetSize(bw, bh); return; }
            Schedule();
        }
    }

    // ---- colors and transparency ----

    public string AlphaText { get => _alpha; set { if (SetProperty(ref _alpha, value)) Schedule(); } }
    public string MaxColorsText { get => _maxColors; set { if (SetProperty(ref _maxColors, value)) Schedule(); } }
    public bool UsePalette { get => _usePalette; set { if (SetProperty(ref _usePalette, value)) Schedule(); } }
    public bool Dither { get => _dither; set { if (SetProperty(ref _dither, value)) Schedule(); } }

    // ---- cleanup and outline ----

    public bool MergeRareColors { get => _mergeRare; set { if (SetProperty(ref _mergeRare, value)) Schedule(); } }
    public bool RemoveIslands { get => _removeIslands; set { if (SetProperty(ref _removeIslands, value)) Schedule(); } }
    public bool FillHoles { get => _fillHoles; set { if (SetProperty(ref _fillHoles, value)) Schedule(); } }
    public bool Outline { get => _outline; set { if (SetProperty(ref _outline, value)) Schedule(); } }

    public Choice SelectedTarget { get => _target; set { if (value != null) SetProperty(ref _target, value); } }

    // ---- result ----

    public PixelImage? Preview { get => _preview; private set => SetProperty(ref _preview, value); }
    public string ResultInfo { get => _resultInfo; private set => SetProperty(ref _resultInfo, value); }
    public string PipelineJson { get => _pipelineJson; private set => SetProperty(ref _pipelineJson, value); }
    public ImportResult? Result { get; private set; }

    public RelayCommand ImportCommand { get; }
    public RelayCommand SaveTemplateCommand { get; }

    // ---- internals ----

    private (int W, int H) BaseSize() => _trim ? (_trimmed.Width, _trimmed.Height) : (_source.Width, _source.Height);

    private void SyncHeight(int value)
    {
        _syncingSize = true;
        HeightText = value.ToString();
        _syncingSize = false;
    }

    private void SyncWidth(int value)
    {
        _syncingSize = true;
        WidthText = value.ToString();
        _syncingSize = false;
    }

    private void Schedule()
    {
        if (!_loading) _recompute.Trigger();
    }

    /// <summary>Recomputes the result at once, without waiting for the timer.</summary>
    public void Recompute()
    {
        try
        {
            PipelineJson = BuildPipelineJson();
            var res = Pipeline.Parse(PipelineJson).Run(_source);
            if (!res.Ok)
            {
                Preview = null;
                _canImport = false;
                ResultInfo = Loc.T("batch.error_2") + res.Log.First(l => l.Error != null).Error;
            }
            else
            {
                Preview = res.Image;
                _canImport = true;
                ResultInfo = Loc.T("import.result_colors", res.Image.Width, res.Image.Height, res.Image.DistinctColors());
            }
        }
        catch (Exception ex)
        {
            Preview = null;
            _canImport = false;
            ResultInfo = ex.Message;
        }
        ImportCommand.RaiseCanExecuteChanged();
    }

    private string BuildPipelineJson()
    {
        var steps = new JsonArray();
        if (_trim) steps.Add(new JsonObject { ["op"] = "trim" });

        if (!int.TryParse(_width, out var w) || !int.TryParse(_height, out var h) || w < 1 || h < 1)
            throw new FormatException(Loc.T("import.enter_the_size_as_numbers"));
        var (bw, bh) = BaseSize();
        if ((w != bw || h != bh) && _method.Key != "none")
            steps.Add(new JsonObject
            {
                ["op"] = "resize", ["width"] = w, ["height"] = h, ["keepAspect"] = false, ["method"] = _method.Key,
            });

        int alpha = int.TryParse(_alpha, out var a) ? Math.Clamp(a, 0, 255) : 128;
        steps.Add(new JsonObject { ["op"] = "alphaThreshold", ["value"] = alpha });

        if (_usePalette && _palette.Count > 0)
            steps.Add(new JsonObject
            {
                ["op"] = "applyPalette", ["dither"] = _dither,
                ["palette"] = new JsonArray(_palette.Select(c => (JsonNode?)JsonValue.Create(Pixel.ToHex(c))).ToArray()),
            });
        else if (int.TryParse(_maxColors, out var colors) && colors > 0)
            steps.Add(new JsonObject { ["op"] = "quantize", ["colors"] = colors, ["dither"] = _dither });

        if (_mergeRare) steps.Add(new JsonObject { ["op"] = "mergeRareColors", ["minCount"] = 3 });
        if (_removeIslands) steps.Add(new JsonObject { ["op"] = "removeIslands", ["maxSize"] = 1 });
        if (_fillHoles) steps.Add(new JsonObject { ["op"] = "fillHoles", ["maxSize"] = 1 });
        if (_outline)
            steps.Add(new JsonObject { ["op"] = "outline", ["color"] = "#000000", ["mode"] = "outside", ["diagonal"] = false });

        var root = new JsonObject { ["name"] = "import", ["description"] = Loc.T("import.template_from_the_import_dialog"), ["steps"] = steps };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    private void Import()
    {
        _recompute.Flush();
        if (_preview == null) return;
        var target = _target.Key == nameof(ImportTarget.NewLayer) ? ImportTarget.NewLayer : ImportTarget.NewDocument;
        Result = new ImportResult(_preview, target);
        Close(true);
    }

    private void SaveTemplate()
    {
        var path = _dialogs.PickSaveFile(Loc.T("auto.pipeline_json_json"), "my-pipeline.json");
        if (path == null) return;
        try { File.WriteAllText(path, _pipelineJson); }
        catch (Exception ex) { _dialogs.ShowError(ex); }
    }
}
