// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using PixelEditor.Core;

namespace PixelEditor.ViewModels;

public enum ShadeMode { Brightness, Hue }

/// <summary>Tools with their own logic: magic wand, lasso, gradient, color shift, text, selection transforms.</summary>
public sealed partial class CanvasViewModel
{
    private double _wandTolerance = AppSettings.DefaultWandTolerance;
    private bool _wandContiguous = true;
    private bool _wandFollow;
    private double _eraserTolerance = AppSettings.DefaultWandTolerance;
    private bool _eraserContiguous = true;
    private bool _eraserFollow;
    private GradientMode _gradientMode = GradientMode.Dither;
    private double _gradientSteps = 4;
    private ShadeMode _shadeMode = ShadeMode.Brightness;
    private double _shadeAmount = 12;
    private List<(int X, int Y)>? _lassoPoints;
    private HashSet<int>? _shadeTouched;
    private bool _shadeRight;

    // ---- settings ----

    /// <summary>Wand tolerance: how much a color may differ (0 is an exact match only).</summary>
    public double WandTolerance { get => _wandTolerance; set => SetProperty(ref _wandTolerance, Math.Clamp(Math.Round(value), 0, 128)); }

    /// <summary>true: only the connected area, false: all matching pixels of the layer.</summary>
    public bool WandContiguous { get => _wandContiguous; set => SetProperty(ref _wandContiguous, value); }

    /// <summary>true: the fill also follows a smooth color change (a step from the neighbor it came from), so a gradient or noisy background is selected up to a sharp edge.</summary>
    public bool WandFollow { get => _wandFollow; set => SetProperty(ref _wandFollow, value); }

    // The background eraser has its own remembered copies of the wand options and uses the same matching (SelectionMask.Wand).
    public double EraserTolerance { get => _eraserTolerance; set => SetProperty(ref _eraserTolerance, Math.Clamp(Math.Round(value), 0, 128)); }
    public bool EraserContiguous { get => _eraserContiguous; set => SetProperty(ref _eraserContiguous, value); }
    public bool EraserFollow { get => _eraserFollow; set => SetProperty(ref _eraserFollow, value); }
    public bool IsBgEraserTool => _tool == Tool.BgEraser;

    /// <summary>One click: the region matching the clicked pixel (same rules as the wand) becomes transparent on the active layer, one undo step.</summary>
    private void BgEraserClick(int x, int y)
    {
        CommitFloat();
        var layer = _s.ActiveLayer;
        if (layer.Locked) { _s.Status(Loc.T("canvas.the_layer_is_locked")); return; }
        if (!layer.Visible) { _s.Status(Loc.T("canvas.the_layer_is_hidden_turn")); return; }
        var mask = SelectionMask.Wand(layer, x, y, (int)_eraserTolerance, _eraserContiguous, _eraserFollow);
        if (mask == null) return;
        var batch = new PixelBatch(layer);
        mask.Clear(batch);
        int n = batch.Count;
        _s.Doc.History.Commit(batch);
        _s.RequestRender();
        _s.Status(n == 0 ? Loc.T("canvas.bg_eraser_nothing") : Loc.T("canvas.bg_eraser_erased", n, (int)_eraserTolerance));
    }

    public GradientMode GradientKind { get => _gradientMode; set { if (SetProperty(ref _gradientMode, value)) OnPropertyChanged(nameof(IsGradientSteps)); } }
    public double GradientSteps { get => _gradientSteps; set => SetProperty(ref _gradientSteps, Math.Clamp(Math.Round(value), 2, 16)); }
    public ShadeMode ShadeKind { get => _shadeMode; set => SetProperty(ref _shadeMode, value); }
    public double ShadeAmount { get => _shadeAmount; set => SetProperty(ref _shadeAmount, Math.Clamp(Math.Round(value), 1, 40)); }

    public bool IsWandTool => _tool == Tool.Wand;
    public bool IsLassoTool => _tool == Tool.Lasso;
    public bool IsGradientTool => _tool == Tool.Gradient;
    public bool IsShadeTool => _tool == Tool.Shade;
    public bool IsTextTool => _tool == Tool.Text;
    public bool IsGradientSteps => _tool == Tool.Gradient && _gradientMode == GradientMode.Steps;

    // ---- magic wand ----

    /// <summary>Left button: select the area under the cursor. Right: add the area to the current selection.</summary>
    private void WandClick(int x, int y, bool add)
    {
        CommitFloat();
        var layer = _s.ActiveLayer;
        var mask = SelectionMask.Wand(layer, x, y, (int)_wandTolerance, _wandContiguous, _wandFollow);
        if (mask == null) return;
        if (add)
        {
            var current = _selMask ?? (_selRect is { IsEmpty: false } r ? SelectionMask.Rectangle(r) : null);
            if (current != null) mask = current.Union(mask, _s.Doc.Width, _s.Doc.Height);
        }
        _selMask = mask;
        _selRect = mask.Bounds;
        UpdateSelection();
        _s.RequestRender();
        _s.Status(Loc.T("canvas.wand_selected_tol", mask.Count, (int)_wandTolerance, mask.Bounds.Width, mask.Bounds.Height));
    }

    // ---- lasso ----

    private bool BeginLasso(int x, int y)
    {
        CommitFloat();
        _selMask = null;
        _selRect = null;
        _lassoPoints = [(x, y)];
        OnPropertyChanged(nameof(LassoPath));
        _drag = DragMode.Lasso;
        UpdateSelection();
        return true;
    }

    /// <summary>The lasso outline while tracing (for a light line highlight); null outside tracing.</summary>
    public IReadOnlyList<(int X, int Y)>? LassoPath => _lassoPoints ?? PolygonPath();

    /// <summary>While moving we only remember the point: the shape is computed once when the button is released, otherwise tracing lags.</summary>
    private void LassoMove(int x, int y)
    {
        if (_lassoPoints == null) return;
        x = Math.Clamp(x, 0, _s.Doc.Width - 1);
        y = Math.Clamp(y, 0, _s.Doc.Height - 1);
        if (_lassoPoints[^1] == (x, y)) return;
        _lassoPoints.Add((x, y));
        OnPropertyChanged(nameof(LassoPath));
    }

    private void FinishLasso()
    {
        var points = _lassoPoints;
        _lassoPoints = null;
        OnPropertyChanged(nameof(LassoPath));
        var mask = points == null ? null : SelectionMask.Polygon(points, _s.Doc.Width, _s.Doc.Height);
        _selMask = mask;
        _selRect = mask?.Bounds;
        if (mask == null || mask.Count < 2)
        {
            _selMask = null;
            _selRect = null;
            UpdateSelection();
            return;
        }
        UpdateSelection();
        _s.Status(Loc.T("canvas.lasso_selected_px_drag_the", mask.Count));
    }

    // ---- gradient ----

    private SelectionMask? _gradMask;
    private PixelRect? _gradRect;

    /// <summary>
    /// The gradient area: the selection (shape or frame), and without a selection the object under the start point (connected non-empty pixels).
    /// The gradient never fills the whole field: without a selection and without an object it does nothing.
    /// </summary>
    private bool BeginGradient(int x, int y)
    {
        _gradMask = _selMask;
        _gradRect = _gradMask == null && _selRect is { IsEmpty: false } r ? r : null;
        if (_gradMask != null || _gradRect != null) return true;
        _gradMask = SelectionMask.Object(_s.ActiveLayer, x, y);
        if (_gradMask != null) return true;
        _s.Status(Loc.T("canvas.the_gradient_applies_to_an"));
        return false;
    }

    private void GradientPreview(int x, int y) =>
        Gradient.Fill(_batch!, _gradRect, _gradMask, _sx, _sy, x, y, _colors.Primary, _colors.Secondary, _gradientMode, (int)_gradientSteps);

    // ---- color shift ----

    private void BeginShade(bool right)
    {
        _shadeRight = right;
        _shadeTouched = [];
    }

    /// <summary>The adjuster brush: the left button lightens (or rotates the hue forward), the right one darkens (backward). Each pixel changes once per stroke.</summary>
    private void ShadePlot(int x, int y)
    {
        var layer = _s.ActiveLayer;
        int size = Math.Max(1, (int)_brushSize);
        int ox = x - (size - 1) / 2, oy = y - (size - 1) / 2;
        double sign = _shadeRight ? -1 : 1;
        for (int dy = 0; dy < size; dy++)
            for (int dx = 0; dx < size; dx++)
            {
                int px = ox + dx, py = oy + dy;
                if (!layer.InBounds(px, py) || !_shadeTouched!.Add(py * layer.Width + px)) continue;
                uint c = layer.Get(px, py);
                if (Pixel.IsEmpty(c)) continue;
                uint n = _shadeMode == ShadeMode.Brightness
                    ? Gradient.Brightness(c, sign * _shadeAmount / 100.0)
                    : Gradient.Hue(c, sign * _shadeAmount * 2);
                _batch!.Set(px, py, n);
            }
    }

    // ---- selection transforms ----

    // Rotate 90/180 and the flips act on the selection, or on the whole active layer when nothing is selected (one undo step).
    // On a non-square layer a whole-layer 90 degree turn is the same as "Rotate by angle": around the center, the excess is cropped.
    public RelayCommand RotateRightCommand => _rotateRight ??= new RelayCommand(() => RotateQuarter(90, RotateCw, Loc.T("canvas.rotated_clockwise")));
    public RelayCommand RotateLeftCommand => _rotateLeft ??= new RelayCommand(() => RotateQuarter(-90, RotateCcw, Loc.T("canvas.rotated_counterclockwise")));
    public RelayCommand Rotate180Command => _rotate180 ??= new RelayCommand(() => TransformSelectionOrLayer(img => FlipH(FlipV(img)), Loc.T("canvas.rotated_180")));
    public RelayCommand FlipHorizontalCommand => _flipH ??= new RelayCommand(() => TransformSelectionOrLayer(FlipH, Loc.T("canvas.flipped_left_to_right")));
    public RelayCommand FlipVerticalCommand => _flipV ??= new RelayCommand(() => TransformSelectionOrLayer(FlipV, Loc.T("canvas.flipped_top_to_bottom")));
    public RelayCommand ScaleUpCommand => _scaleUp ??= new RelayCommand(() => TransformSelection(i => Scale(i, 2), Loc.T("canvas.doubled_in_size")), () => HasSelection);
    public RelayCommand ScaleDownCommand => _scaleDown ??= new RelayCommand(() => TransformSelection(HalfSize, Loc.T("canvas.halved_in_size")), () => HasSelection);

    private RelayCommand? _rotateRight, _rotateLeft, _rotate180, _flipH, _flipV, _scaleUp, _scaleDown;

    private void RaiseSelectionCommands()
    {
        foreach (var c in new[] { _scaleUp, _scaleDown, _cropSel }) c?.RaiseCanExecuteChanged();
    }

    /// <summary>With a selection the selection is transformed (it stays floating); without one the whole active layer is, as one undo step.</summary>
    private void TransformSelectionOrLayer(Func<PixelImage, PixelImage> apply, string status)
    {
        if (HasSelection) { TransformSelection(apply, status); return; }
        if (!_s.CanEditActiveLayer()) return;
        _s.ApplyResult(apply(_s.ActiveLayer.ToImage()), status);
    }

    private void RotateQuarter(double degrees, Func<PixelImage, PixelImage> selectionTurn, string status)
    {
        if (HasSelection) TransformSelection(selectionTurn, status);
        else ApplyRotation(degrees);
    }

    private void TransformSelection(Func<PixelImage, PixelImage> apply, string status)
    {
        if (!HasSelection) { _s.Status(Loc.T("canvas.no_selection")); return; }
        if (_float == null)
        {
            if (_selRect is not PixelRect r || r.IsEmpty || !TryLift(r)) return;
        }
        _float!.Transform(apply);
        UpdateSelection();
        _s.RequestRender();
        _s.Status(Loc.T("canvas.a_click_outside_the_area", status, _float.Bounds.Width, _float.Bounds.Height));
    }

    public static PixelImage FlipH(PixelImage src)
    {
        var d = new PixelImage(src.Width, src.Height);
        for (int y = 0; y < src.Height; y++)
            for (int x = 0; x < src.Width; x++)
                d.Pixels[y * src.Width + (src.Width - 1 - x)] = src.Pixels[y * src.Width + x];
        return d;
    }

    public static PixelImage FlipV(PixelImage src)
    {
        var d = new PixelImage(src.Width, src.Height);
        for (int y = 0; y < src.Height; y++)
            for (int x = 0; x < src.Width; x++)
                d.Pixels[(src.Height - 1 - y) * src.Width + x] = src.Pixels[y * src.Width + x];
        return d;
    }

    public static PixelImage RotateCw(PixelImage src)
    {
        var d = new PixelImage(src.Height, src.Width);
        for (int y = 0; y < src.Height; y++)
            for (int x = 0; x < src.Width; x++)
                d.Pixels[x * d.Width + (src.Height - 1 - y)] = src.Pixels[y * src.Width + x];
        return d;
    }

    public static PixelImage RotateCcw(PixelImage src)
    {
        var d = new PixelImage(src.Height, src.Width);
        for (int y = 0; y < src.Height; y++)
            for (int x = 0; x < src.Width; x++)
                d.Pixels[(src.Width - 1 - x) * d.Width + y] = src.Pixels[y * src.Width + x];
        return d;
    }

    public static PixelImage Scale(PixelImage src, int k)
    {
        var d = new PixelImage(src.Width * k, src.Height * k);
        for (int y = 0; y < d.Height; y++)
            for (int x = 0; x < d.Width; x++)
                d.Pixels[y * d.Width + x] = src.Pixels[(y / k) * src.Width + x / k];
        return d;
    }

    /// <summary>Halving: from each 2×2 block the most frequent color is taken (no new colors appear).</summary>
    public static PixelImage HalfSize(PixelImage src)
    {
        int w = Math.Max(1, src.Width / 2), h = Math.Max(1, src.Height / 2);
        var d = new PixelImage(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var block = new List<uint>(4);
                for (int j = 0; j < 2; j++)
                    for (int i = 0; i < 2; i++)
                    {
                        int sx = Math.Min(src.Width - 1, x * 2 + i), sy = Math.Min(src.Height - 1, y * 2 + j);
                        block.Add(src.Pixels[sy * src.Width + sx]);
                    }
                // the most frequent color; on a tie the non-empty and earlier one wins
                d.Pixels[y * w + x] = block.GroupBy(c => c).OrderByDescending(g => g.Count()).ThenBy(g => Pixel.IsEmpty(g.Key) ? 1 : 0).First().Key;
            }
        return d;
    }
}
