// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using PixelEditor.Core;
using PixelEditor.Core.Ops;

namespace PixelEditor.ViewModels;

public enum Tool { Pencil, Eraser, Fill, Picker, Line, Rect, Ellipse, Select, Move, Wand, Lasso, Gradient, Shade, Text, Polygon, BgEraser }

/// <summary>
/// Everything related to drawing on the canvas: tools, strokes, selection with moving, clipboard, layer shift, zoom.
/// The UI turns mouse events into PointerDown/Move/Up calls and decides nothing else.
/// </summary>
public sealed partial class CanvasViewModel : ObservableObject
{
    private enum DragMode { None, Marquee, MoveFloat, Lasso, MoveText }

    // below 100% for big pictures (down to 6.25%), then whole-number steps for pixel work
    private static readonly double[] Zooms = [0.0625, 0.125, 0.25, 0.5, 1, 2, 3, 4, 6, 8, 12, 16, 24, 32, 48, 64];

    private readonly EditorSession _s;
    private readonly ColorsViewModel _colors;
    private Tool _tool = Tool.Pencil;
    private double _brushSize = 1, _shapeThickness = 1;
    private bool _showSelGrid, _selGridLocal;
    private bool _mirrorX, _mirrorY, _filled, _pickLayerOnly, _wrapShift = true, _showGrid = true;
    private int _zoomIdx = 9;   // 800%
    private string _positionText = "";
    private PixelRect? _selectionBounds;

    // stroke
    private PixelBatch? _batch;
    private PaintContext? _ctx;
    private int _sx, _sy, _lx, _ly;

    // fill
    private double _fillTolerance;
    private bool _fillWholeLayer;

    // selection
    private PixelRect? _selRect;
    private FloatingSelection? _float;
    private PixelRect? _floatSource;
    private SelectionMask? _selMask;        // selection shape (wand, lasso); null if the selection is rectangular
    private SelectionMask? _floatSourceMask;
    private DragMode _drag = DragMode.None;
    private int _dragX, _dragY, _floatOrigX, _floatOrigY;

    // clipboard
    private PixelImage? _clip;
    private uint _clipSequence;

    public CanvasViewModel(EditorSession session, ColorsViewModel colors)
    {
        _s = session;
        _colors = colors;
        _s.FlushRequested += CommitFloat;
        _s.DocumentChanged += ResetState;
        _s.CanvasResized += ResetState;
        _s.OverlaysChanged += () => OnPropertyChanged(nameof(WorkspaceLabel));

        SelectToolCommand = new RelayCommand(p => Tool = p is Tool t ? t : Enum.Parse<Tool>((string)p!));
        ZoomInCommand = new RelayCommand(() => SetZoom(_zoomIdx + 1));
        ZoomOutCommand = new RelayCommand(() => SetZoom(_zoomIdx - 1));
        ZoomActualCommand = new RelayCommand(() => SetZoom(Array.IndexOf(Zooms, 1.0)));
        FitCommand = new RelayCommand(() => FitRequested?.Invoke());
        CopyCommand = new RelayCommand(Copy, () => HasSelection);
        ExportSelectionMaskCommand = new RelayCommand(ExportSelectionMask, () => HasSelection);
        CutCommand = new RelayCommand(Cut, () => HasSelection);
        PasteCommand = new RelayCommand(Paste);
        DeleteCommand = new RelayCommand(DeleteSelection, () => HasSelection);
        SelectAllCommand = new RelayCommand(SelectAll);
        DeselectCommand = new RelayCommand(Deselect);
        CanvasSizeCommand = new RelayCommand(ResizeCanvas);
        ImageSizeCommand = new RelayCommand(ResizeImage);
        HookText();
        RestorePrefs();
    }

    // ---- tool settings ----

    public Tool Tool
    {
        get => _tool;
        set
        {
            var old = _tool;
            if (!SetProperty(ref _tool, value)) return;
            OnPropertyChanged(nameof(IsShapeTool));
            OnPropertyChanged(nameof(IsBrushTool));
            OnPropertyChanged(nameof(IsPickerTool));
            OnPropertyChanged(nameof(IsFillTool));
            OnPropertyChanged(nameof(IsMoveTool));
            OnPropertyChanged(nameof(IsSelectTool));
            OnPropertyChanged(nameof(IsWandTool));
            OnPropertyChanged(nameof(IsLassoTool));
            OnPropertyChanged(nameof(IsPolygonTool));
            OnPropertyChanged(nameof(IsGradientTool));
            OnPropertyChanged(nameof(IsShadeTool));
            OnPropertyChanged(nameof(IsTextTool));
            OnPropertyChanged(nameof(IsBgEraserTool));
            OnPropertyChanged(nameof(HandlesActive));
            if (value is Tool.Line or Tool.Rect or Tool.Ellipse) _lastShape = value;
            if (value is Tool.Select or Tool.Wand or Tool.Lasso or Tool.Polygon) _lastSelection = value;
            OnPropertyChanged(nameof(IsShapeGroup));
            OnPropertyChanged(nameof(IsSelectionGroup));
            OnPropertyChanged(nameof(LastShapeTool));
            OnPropertyChanged(nameof(LastSelectionTool));
            OnPropertyChanged(nameof(IsGradientSteps));
            // leaving "Selection", commit the floating paste and drop the frame: otherwise it stays hanging over another tool
            OnToolLeft(old, value);
        }
    }

    /// <summary>Line, rectangle, ellipse: outline thickness and fill are available for them.</summary>
    public bool IsShapeTool => _tool is Tool.Line or Tool.Rect or Tool.Ellipse;

    /// <summary>Pencil and eraser: brush size and symmetry.</summary>
    public bool IsBrushTool => _tool is Tool.Pencil or Tool.Eraser or Tool.Shade;

    public bool IsPickerTool => _tool == Tool.Picker;
    public bool IsMoveTool => _tool == Tool.Move;
    public bool IsSelectTool => _tool == Tool.Select;

    /// <summary>Shape outline thickness (px), set with a right click on the tool.</summary>
    public double ShapeThickness
    {
        get => _shapeThickness;
        set => SetProperty(ref _shapeThickness, Math.Clamp(Math.Round(value), 1, 16));
    }

    /// <summary>Selection coordinate grid: pixel numbers along the frame edges and lines between pixels inside.</summary>
    public bool ShowSelectionGrid { get => _showSelGrid; set => SetProperty(ref _showSelGrid, value); }

    /// <summary>Grid labels: false: canvas coordinates (global), true: from the top left corner of the selection (0, 1, 2…).</summary>
    public bool SelectionGridLocal { get => _selGridLocal; set => SetProperty(ref _selGridLocal, value); }

    public double BrushSize
    {
        get => _brushSize;
        set => SetProperty(ref _brushSize, Math.Clamp(Math.Round(value), 1, 16));
    }

    public bool IsFillTool => _tool == Tool.Fill;

    // button groups on the left: one button per family, the variant is chosen with chips in the options
    private Tool _lastShape = Tool.Line, _lastSelection = Tool.Select;
    public bool IsShapeGroup => _tool is Tool.Line or Tool.Rect or Tool.Ellipse;
    public bool IsSelectionGroup => _tool is Tool.Select or Tool.Wand or Tool.Lasso or Tool.Polygon;
    /// <summary>Which shape variant the group button turns on (the last chosen one).</summary>
    public Tool LastShapeTool => _lastShape;
    public Tool LastSelectionTool => _lastSelection;

    /// <summary>Fill tolerance 0..255: how much a color may differ from the chosen pixel.</summary>
    public double FillTolerance
    {
        get => _fillTolerance;
        set => SetProperty(ref _fillTolerance, Math.Clamp(Math.Round(value), 0, 255));
    }

    /// <summary>The fill paints all similar pixels of the layer, not only the connected area.</summary>
    public bool FillWholeLayer { get => _fillWholeLayer; set => SetProperty(ref _fillWholeLayer, value); }

    public bool MirrorX { get => _mirrorX; set => SetProperty(ref _mirrorX, value); }
    public bool MirrorY { get => _mirrorY; set => SetProperty(ref _mirrorY, value); }
    public bool FilledShapes { get => _filled; set => SetProperty(ref _filled, value); }
    public bool PickFromLayerOnly { get => _pickLayerOnly; set => SetProperty(ref _pickLayerOnly, value); }

    /// <summary>Layer shift by keys wraps around (for tiles).</summary>
    public bool WrapShift { get => _wrapShift; set => SetProperty(ref _wrapShift, value); }

    public bool ShowPixelGrid { get => _showGrid; set => SetProperty(ref _showGrid, value); }

    public string PositionText
    {
        get => _positionText;
        private set => SetProperty(ref _positionText, value);
    }

    /// <summary>The frame of the selection or floating area (null if there is no selection).</summary>
    public PixelRect? SelectionBounds
    {
        get => _selectionBounds;
        private set => SetProperty(ref _selectionBounds, value);
    }

    /// <summary>The selection shape (wand, lasso, a moved area with a shape); null if the selection is rectangular or absent.</summary>
    public SelectionMask? SelectionShape => CurrentMask();

    private SelectionMask? CurrentMask() =>
        _float != null ? (_float.Mask != null ? new SelectionMask(_float.Bounds, _float.Mask) : null) : _selMask;

    public bool HasSelection => _float != null || (_selRect is { IsEmpty: false });
    public bool HasFloat => _float != null;

    public RelayCommand SelectToolCommand { get; }
    public RelayCommand ZoomInCommand { get; }
    public RelayCommand ZoomOutCommand { get; }

    /// <summary>Zoom 100% (one canvas pixel is one screen pixel).</summary>
    public RelayCommand ZoomActualCommand { get; }

    /// <summary>Fit the whole canvas into the window (the view picks the zoom through <see cref="FitRequested"/>).</summary>
    public RelayCommand FitCommand { get; }
    public RelayCommand CopyCommand { get; }
    public RelayCommand ExportSelectionMaskCommand { get; }
    public RelayCommand CutCommand { get; }
    public RelayCommand PasteCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand SelectAllCommand { get; }
    public RelayCommand DeselectCommand { get; }
    public RelayCommand CanvasSizeCommand { get; }
    public RelayCommand ImageSizeCommand { get; }

    /// <summary>Image size: scaling all layers with the chosen method (unlike the canvas size).</summary>
    private void ResizeImage()
    {
        CommitFloat();
        var r = _s.Dialogs.AskImageSize(_s.Doc.Width, _s.Doc.Height);
        if (r == null) return;
        try
        {
            var doc = _s.Doc;
            doc.ResizeImage(r.Width, r.Height, r.Method);
            if (doc.HasSheetGrid && (doc.SheetCellWidth > doc.Width || doc.SheetCellHeight > doc.Height))
            {
                doc.SheetCellWidth = doc.SheetCellHeight = 0;
                _s.RaiseOverlaysChanged();
            }
            _s.Status(Loc.T("canvas.image", doc.Width, doc.Height, r.Method));
        }
        catch (Exception ex) { _s.Dialogs.ShowError(ex); }
    }

    /// <summary>The UI reports the top left visible corner of the canvas, where to paste a picture.</summary>
    public Func<(int X, int Y)>? PasteOrigin { get; set; }

    /// <summary>The canvas changed size: the UI must pick the zoom again.</summary>
    public event Action? FitRequested;

    // ---- zoom ----

    public double Zoom => Zooms[_zoomIdx];
    public string ZoomText => $"{Math.Round(Zoom * 100, 2)}%";

    public void SetZoom(int index)
    {
        var clamped = Math.Clamp(index, 0, Zooms.Length - 1);
        if (clamped == _zoomIdx) return;
        _zoomIdx = clamped;
        OnPropertyChanged(nameof(Zoom));
        OnPropertyChanged(nameof(ZoomText));
    }

    /// <summary>The largest zoom step at which the whole canvas fits the viewport (can be below 100% for big pictures).</summary>
    public void FitZoom(double viewWidth, double viewHeight)
    {
        int best = 0;
        for (int i = 0; i < Zooms.Length; i++)
            if (_s.Doc.Width * Zooms[i] <= viewWidth && _s.Doc.Height * Zooms[i] <= viewHeight) best = i;
        SetZoom(best);
    }

    public void UpdatePosition(int x, int y)
    {
        bool inside = _s.ActiveLayer.InBounds(x, y);
        _hoverInside = inside;
        if (inside) { _hoverX = x; _hoverY = y; }
        PositionText = inside ? $"{x}, {y}" : "";
        if (inside)
        {
            OnPropertyChanged(nameof(WorkspaceLabel));
        }
    }

    public void ClearPosition()
    {
        _hoverInside = false;
        PositionText = "";
    }

    private int _hoverX, _hoverY;
    private bool _moveObjectDrag;

    /// <summary>Document size for the status bar: "32 × 32".</summary>
    public string DocumentSizeText => $"{_s.Doc.Width} × {_s.Doc.Height}";

    /// <summary>The caption above the work area: the size and, if a sheet grid is set, the cell under the cursor ("32 × 32 px · Cell 1, 1").</summary>
    public string WorkspaceLabel
    {
        get
        {
            var doc = _s.Doc;
            var text = $"{doc.Width} × {doc.Height} px";
            if (!doc.HasSheetGrid) return text;
            int col = Math.Min(_hoverX, doc.Width - 1) / doc.SheetCellWidth;
            int row = Math.Min(_hoverY, doc.Height - 1) / doc.SheetCellHeight;
            return $"{text} · Cell {col + 1}, {row + 1}";
        }
    }

    // ---- pointer ----

    /// <summary>Returns true if the UI must capture the mouse until PointerUp.</summary>
    public bool PointerDown(int x, int y, bool right, bool alt)
    {
        if (_batch != null || _drag != DragMode.None) return false;

        if (alt || _tool == Tool.Picker)
        {
            Pick(x, y, right);
            return false;
        }
        // Any other tool action drops the used-colors highlight (it dims the whole canvas and must not linger)
        if (_s.Highlight != null) _s.Highlight = null;
        // A floating paste under a drawing tool stays draggable until the first click outside commits it
        if (!right && _float != null && _tool is not (Tool.Select or Tool.Move or Tool.Wand or Tool.Lasso or Tool.Polygon or Tool.Text or Tool.Gradient)
            && _polyNodes == null && InsideSelection(x, y))
            return BeginSelectDrag(x, y);
        if (_tool == Tool.BgEraser) { if (!right) BgEraserClick(x, y); return false; }
        if (_tool == Tool.Select)
            return !right && BeginSelectDrag(x, y);
        if (_tool == Tool.Move)
            return !right && BeginMoveObject(x, y);
        // Magic wand, lasso and point selection: a click inside a finished selection drags it, without changing the tool
        if (_tool is Tool.Wand or Tool.Lasso or Tool.Polygon && !right && _polyNodes == null && InsideSelection(x, y))
            return BeginSelectDrag(x, y);
        if (_tool == Tool.Wand) { WandClick(x, y, right); return false; }
        if (_tool == Tool.Lasso) return !right && BeginLasso(x, y);
        if (_tool == Tool.Polygon) { PolygonClick(x, y, right); return false; }
        if (_tool == Tool.Text) return !right && BeginTextOrMove(x, y);

        CommitFloat();
        var layer = _s.ActiveLayer;
        if (layer.Locked) { _s.Status(Loc.T("canvas.the_layer_is_locked")); return false; }
        if (!layer.Visible) { _s.Status(Loc.T("canvas.the_layer_is_hidden_turn")); return false; }

        uint color = _tool == Tool.Eraser ? Pixel.Empty : (right ? _colors.Secondary : _colors.Primary);
        if (IsShapeTool) layer = BeginObjectLayer(Loc.T("canvas.shape"));
        _batch = new PixelBatch(layer);
        _ctx = new PaintContext(_batch)
        {
            Color = color,
            BrushSize = (int)(IsShapeTool ? _shapeThickness : _brushSize),
            // symmetry is set in the pencil and eraser options and only for them (shapes have no such setting, the highlight also shows a single shape)
            MirrorX = _tool is Tool.Pencil or Tool.Eraser && _mirrorX,
            MirrorY = _tool is Tool.Pencil or Tool.Eraser && _mirrorY,
        };
        _sx = _lx = x;
        _sy = _ly = y;

        switch (_tool)
        {
            case Tool.Pencil:
            case Tool.Eraser:
                _ctx.Plot(x, y);
                break;
            case Tool.Fill:
                Drawing.FloodFill(_batch, x, y, color, (int)_fillTolerance, _fillWholeLayer);
                EndStroke();
                return false;
            case Tool.Gradient:
                if (!BeginGradient(x, y)) { _batch = null; _ctx = null; return false; }
                GradientPreview(x, y);
                break;
            case Tool.Shade:
                BeginShade(right);
                ShadePlot(x, y);
                break;
            default:
                ShapePreview(x, y);
                break;
        }
        _s.RequestRender();
        return true;
    }

    public void PointerMove(int x, int y)
    {
        if (_tool == Tool.Polygon) PolygonHover(x, y);
        if (_drag == DragMode.Lasso) { LassoMove(x, y); return; }
        if (_drag == DragMode.MoveText) { TextDragTo(x, y); return; }
        if (_drag == DragMode.Marquee)
        {
            int cx = Math.Clamp(x, 0, _s.Doc.Width - 1), cy = Math.Clamp(y, 0, _s.Doc.Height - 1);
            _selRect = PixelRect.FromCorners(_dragX, _dragY, cx, cy).ClampTo(_s.Doc.Width, _s.Doc.Height);
            UpdateSelection();
            return;
        }
        if (_drag == DragMode.MoveFloat && _float != null)
        {
            _float.MoveTo(_floatOrigX + (x - _dragX), _floatOrigY + (y - _dragY));
            UpdateSelection();
            _s.RequestRender();
            return;
        }

        if (_batch == null || _ctx == null) return;
        bool freehand = _tool is Tool.Pencil or Tool.Eraser or Tool.Shade;
        if (freehand && x == _lx && y == _ly) return;

        if (freehand)
            Drawing.Line(_lx, _ly, x, y, _tool == Tool.Shade ? ShadePlot : _ctx.Plot);
        else
        {
            _batch.Revert();
            ShapePreview(x, y);
        }
        _lx = x;
        _ly = y;
        _s.RequestRender();
    }

    /// <summary>Safe to call an extra time: does nothing without an active stroke.</summary>
    public void PointerUp()
    {
        if (_drag != DragMode.None)
        {
            if (_drag == DragMode.Lasso) { _drag = DragMode.None; FinishLasso(); return; }
            if (_drag == DragMode.MoveText) { _drag = DragMode.None; return; }
            if (_drag == DragMode.Marquee && _selRect is { Width: 1, Height: 1 }) _selRect = null; // a simple click clears the selection
            _drag = DragMode.None;
            if (_tool == Tool.Move && _moveObjectDrag && _float != null)
            {
                var moved = _float.Bounds;
                CommitFloat();   // the object stays selected: you can keep dragging, stretch and rotate; a click on an empty spot clears it
                UpdateSelection();
                _s.Status(Loc.T("canvas.object_moved", moved.X, moved.Y, moved.Width, moved.Height));
                return;
            }
            UpdateSelection();
            if (_selRect is PixelRect r && !r.IsEmpty) _s.Status(Loc.T("canvas.selected", r.Width, r.Height, r.X, r.Y));
            // the selection tool stays active: a press inside the selection drags it, outside starts a new one
            return;
        }
        if (_batch != null) EndStroke();
    }

    private void ShapePreview(int x, int y)
    {
        switch (_tool)
        {
            case Tool.Line: Drawing.Line(_sx, _sy, x, y, _ctx!.Plot); break;
            case Tool.Rect: Drawing.Rectangle(_sx, _sy, x, y, _filled, _ctx!.Plot); break;
            case Tool.Ellipse: Drawing.Ellipse(_sx, _sy, x, y, _filled, _ctx!.Plot); break;
            case Tool.Gradient: GradientPreview(x, y); break;
        }
    }

    private void EndStroke()
    {
        var batch = _batch;
        var ctx = _ctx;
        _batch = null;
        _ctx = null;
        var action = batch?.ToAction();
        if (action != null && ctx != null) NoteUsedColors(ctx.Color);
        if (_pendingLayerAction != null) CompletePendingLayer(action);
        else if (action != null) _s.Doc.History.Push(action);
        _s.RequestRender();
    }

    /// <summary>A stroke that changed pixels is committed: its colors go to the "recent colors" row (eraser and lighten/darken use none).</summary>
    private void NoteUsedColors(uint strokeColor)
    {
        switch (_tool)
        {
            case Tool.Pencil: case Tool.Fill: case Tool.Line: case Tool.Rect: case Tool.Ellipse:
                _colors.NoteUsed(strokeColor);
                break;
            case Tool.Gradient:
                _colors.NoteUsed(_colors.Secondary);
                _colors.NoteUsed(_colors.Primary);
                break;
        }
    }

    /// <summary>Stretch handles and the rotate zone work under the selection tools and Move, and for a floating paste under a drawing tool.</summary>
    public bool HandlesActive => (IsSelectionGroup || _tool == Tool.Move || (_float != null && _tool is not (Tool.Text or Tool.Gradient))) && HasSelection;

    /// <summary>The point is inside the current selection (shape included): the view shows a move cursor.</summary>
    public bool IsOverSelection(int x, int y) => InsideSelection(x, y);

    private void Pick(int x, int y, bool secondary)
    {
        // picking a color must not leave the used-colors highlight (dimmed canvas) behind
        if (_s.Highlight != null) _s.Highlight = null;
        var layer = _s.ActiveLayer;
        if (!layer.InBounds(x, y)) return;
        uint c = _pickLayerOnly ? layer.Get(x, y) : _s.Doc.Flatten().Get(x, y);
        if (secondary) _colors.SetSecondary(c); else _colors.SetPrimary(c);
        _s.Status(Loc.T("canvas.eyedropper", Pixel.ToHex(c)));
    }

    // ---- selection and the floating area ----

    /// <summary>A point inside a finished selection (the shape is taken into account).</summary>
    private bool InsideSelection(int x, int y)
    {
        PixelRect? current = _float != null ? _float.Bounds : _selRect;
        if (current is not PixelRect b || b.IsEmpty || !b.Contains(x, y)) return false;
        return CurrentMask() is not { } shape || shape.Contains(x, y);
    }

    private bool BeginSelectDrag(int x, int y)
    {
        PixelRect? current = _float != null ? _float.Bounds : _selRect;
        if (current is PixelRect b && !b.IsEmpty && b.Contains(x, y) && (CurrentMask() is not { } shape || shape.Contains(x, y)))
        {
            // dragging the selected area: the area is lifted above the layer
            if (_float == null && !TryLift(b)) return false;
            _drag = DragMode.MoveFloat;
            _dragX = x;
            _dragY = y;
            _floatOrigX = _float!.X;
            _floatOrigY = _float.Y;
        }
        else
        {
            CommitFloat();
            _selMask = null;
            _drag = DragMode.Marquee;
            _dragX = x;
            _dragY = y;
            _selRect = null;   // the old selection disappears at once on press; the new frame appears when the mouse moves
        }
        UpdateSelection();
        _s.RequestRender();
        return true;
    }

    /// <summary>The "Move" tool: takes the object under the cursor (connected pixels) and drags it; releasing the mouse commits the position with one undo.</summary>
    private bool BeginMoveObject(int x, int y)
    {
        // with a finished selection a click inside it drags the selection itself; outside it (or without a selection) the object under the cursor is taken
        if (InsideSelection(x, y)) { _moveObjectDrag = false; return BeginSelectDrag(x, y); }
        _moveObjectDrag = true;
        CommitFloat();
        _selRect = null;
        _selMask = null;
        UpdateSelection();   // otherwise the frame and SelectionBounds stay "hanging" if the spot under the cursor is empty
        var layer = _s.ActiveLayer;
        if (layer.Locked) { _s.Status(Loc.T("canvas.the_layer_is_locked")); return false; }
        if (!layer.Visible) { _s.Status(Loc.T("canvas.the_layer_is_hidden_turn")); return false; }
        var f = FloatingSelection.LiftObject(layer, x, y);
        if (f == null) { _s.Status(Loc.T("canvas.nothing_here_click_a_pixel")); return false; }
        _float = f;
        _floatSource = f.Bounds;
        _drag = DragMode.MoveFloat;
        _dragX = x;
        _dragY = y;
        _floatOrigX = f.X;
        _floatOrigY = f.Y;
        UpdateSelection();
        _s.RequestRender();
        return true;
    }

    private bool TryLift(PixelRect rect)
    {
        var layer = _s.ActiveLayer;
        if (layer.Locked) { _s.Status(Loc.T("canvas.the_layer_is_locked")); return false; }
        if (!layer.Visible) { _s.Status(Loc.T("canvas.the_layer_is_hidden_turn")); return false; }
        _floatSourceMask = _selMask != null && _selMask.Bounds == rect ? _selMask : null;
        _float = _floatSourceMask != null ? FloatingSelection.LiftMask(layer, _floatSourceMask) : FloatingSelection.Lift(layer, rect);
        _floatSource = rect;
        return true;
    }

    /// <summary>Commits the dragged area with one undo action.</summary>
    public void CommitFloat()
    {
        if (_float == null) return;
        var f = _float;
        _float = null;
        _floatSource = null;
        _floatSourceMask = null;
        _selRect = f.Bounds.ClampTo(_s.Doc.Width, _s.Doc.Height);
        // the shape is kept while the area is entirely on the canvas (otherwise the frame is clipped and the mask would not match it)
        _selMask = f.Mask != null && _selRect == f.Bounds ? new SelectionMask(f.Bounds, f.Mask) : null;
        if (_selRect is { IsEmpty: true }) { _selRect = null; _selMask = null; }
        var action = f.Commit();
        if (_floatOwnsPendingLayer) CompletePendingLayer(action);
        else if (action != null) _s.Doc.History.Push(action);
        _s.RequestRender();
        UpdateSelection();
    }

    /// <summary>If the area is floating, cancels its move (that is the "cancel" at this moment). Returns true if there was something to cancel.</summary>
    public bool TryCancelFloat()
    {
        if (_float == null) return false;
        _float.Cancel();
        if (_floatOwnsPendingLayer) DiscardPendingLayer();
        _selRect = _floatSource;
        _selMask = _floatSourceMask;
        _floatSourceMask = null;
        _float = null;
        _floatSource = null;
        _s.RequestRender();
        UpdateSelection();
        return true;
    }

    /// <summary>Leaving the selection tools for a drawing tool clears the selection (otherwise the frame hangs over another tool); the gradient works inside the selection and keeps it.</summary>
    private void OnToolLeft(Tool old, Tool now)
    {
        if (old == Tool.Text) CommitText();   // the typed text is placed when the tool is left
        static bool Family(Tool t) => t is Tool.Select or Tool.Wand or Tool.Lasso or Tool.Polygon or Tool.Move;
        if (old == Tool.Polygon) CancelPolygon();
        if (Family(old) && !Family(now) && now != Tool.Gradient) Deselect();
        else if (old == Tool.Text && now != Tool.Text) Deselect();
    }

    private void UpdateSelection()
    {
        SelectionBounds = _float != null ? _float.Bounds : _selRect;
        OnPropertyChanged(nameof(SelectionShape));
        CopyCommand.RaiseCanExecuteChanged();
        ExportSelectionMaskCommand.RaiseCanExecuteChanged();
        CutCommand.RaiseCanExecuteChanged();
        DeleteCommand.RaiseCanExecuteChanged();
        RaiseSelectionCommands();
        _s.NotifyStateChanged();
    }

    private void ResetState()
    {
        // the raster or the document is different: the floating area, frame and stroke lose their meaning
        ForgetPendingLayer();
        ResetText();
        _float = null;
        _floatSource = null;
        _floatSourceMask = null;
        _selMask = null;
        _selRect = null;
        _batch = null;
        _ctx = null;
        _drag = DragMode.None;
        _hoverX = _hoverY = 0;
        OnPropertyChanged(nameof(DocumentSizeText));
        OnPropertyChanged(nameof(WorkspaceLabel));
        UpdateSelection();
        FitRequested?.Invoke();
    }

    private void SelectAll()
    {
        if (_text != null) { _text.SelectAll(); return; }   // while typing, "select all" selects the text
        CommitFloat();
        // from a drawing tool go to the last selection tool (so the frame, handles and dragging work); selection tools, Move and Gradient stay
        if (_tool is not (Tool.Select or Tool.Wand or Tool.Lasso or Tool.Polygon or Tool.Move or Tool.Gradient)) Tool = _lastSelection;
        _selMask = null;
        _selRect = new PixelRect(0, 0, _s.Doc.Width, _s.Doc.Height);
        UpdateSelection();
        _s.Status(Loc.T("canvas.selected_all", _s.Doc.Width, _s.Doc.Height));
    }

    private void Deselect()
    {
        CancelPolygon();
        CommitFloat();
        _selRect = null;
        _selMask = null;
        UpdateSelection();
    }

    /// <summary>Moves the selection (lifting it above the layer) by dx, dy. Returns false if there is no selection.</summary>
    public bool Nudge(int dx, int dy, bool big)
    {
        if (!HasSelection) return false;
        int step = big ? 8 : 1;
        MoveSelection(dx * step, dy * step);
        return true;
    }

    private void MoveSelection(int dx, int dy)
    {
        if (_float == null)
        {
            if (_selRect is not PixelRect r || r.IsEmpty || !TryLift(r)) return;
        }
        _float!.MoveBy(dx, dy);
        UpdateSelection();
        _s.RequestRender();
    }

    // ---- clipboard ----

    private PixelImage? SelectionImage()
    {
        if (_float != null) return _float.Pixels;
        if (_selMask != null) return _selMask.Copy(_s.ActiveLayer);
        if (_selRect is PixelRect r && !r.IsEmpty) return Selection.Copy(_s.ActiveLayer, r);
        return null;
    }

    private void Copy()
    {
        var img = SelectionImage();
        if (img == null) { _s.Status(Loc.T("canvas.no_selection")); return; }
        _clip = img.Clone();
        _clipSequence = _s.Clipboard.TrySetImage(img) ? _s.Clipboard.SequenceNumber : 0;
        _s.Status(Loc.T("canvas.copied", img.Width, img.Height));
    }

    private void Cut()
    {
        if (SelectionImage() == null) { _s.Status(Loc.T("canvas.no_selection")); return; }
        Copy();
        DeleteSelection();
    }

    private void DeleteSelection()
    {
        var layer = _s.ActiveLayer;
        if (layer.Locked) { _s.Status(Loc.T("canvas.the_layer_is_locked")); return; }
        if (_float != null)
        {
            var src = _floatSource;
            var srcMask = _floatSourceMask;
            TryCancelFloat();
            if (srcMask != null) ClearMasked(srcMask);
            else if (src is PixelRect s) ClearRegion(s);
            _selRect = src;
            _selMask = srcMask;
        }
        else if (_selMask != null)
            ClearMasked(_selMask);
        else if (_selRect is PixelRect r && !r.IsEmpty)
            ClearRegion(r);
        else
            _s.Status(Loc.T("canvas.no_selection"));
        UpdateSelection();
    }

    private void ClearMasked(SelectionMask mask)
    {
        var batch = new PixelBatch(_s.ActiveLayer);
        mask.Clear(batch);
        _s.Doc.History.Commit(batch);
    }

    private void ClearRegion(PixelRect r)
    {
        var batch = new PixelBatch(_s.ActiveLayer);
        Selection.Clear(batch, r);
        _s.Doc.History.Commit(batch);
    }

    private void Paste()
    {
        if (_text != null) { TextPaste(); return; }   // while typing, paste inserts the clipboard text
        var layer = _s.ActiveLayer;
        if (layer.Locked) { _s.Status(Loc.T("canvas.the_layer_is_locked")); return; }

        // take our own copy from memory: the system clipboard may lose transparency
        bool ours = _clip != null && _clipSequence != 0 && _clipSequence == _s.Clipboard.SequenceNumber;
        var img = ours ? _clip : (_s.Clipboard.GetImage() ?? _clip);
        if (img == null) { _s.Status(Loc.T("canvas.there_is_no_image_on")); return; }

        CommitFloat();
        var (ox, oy) = PasteOrigin?.Invoke() ?? (0, 0);

        // the picture is larger than the canvas (a screenshot, a photo): otherwise only a part of it would be pasted, so offer to enlarge the canvas
        var doc = _s.Doc;
        if (img.Width > doc.Width || img.Height > doc.Height)
        {
            var ask = Loc.T("canvas.the_picture_is_larger_than", img.Width, img.Height, doc.Width, doc.Height);
            if (_s.Dialogs.Confirm(ask, Loc.T("canvas.paste")))
            {
                try
                {
                    doc.ResizeCanvas(Math.Max(doc.Width, img.Width), Math.Max(doc.Height, img.Height), 0, 0);
                    layer = _s.ActiveLayer; // layers are replaced by new objects after the canvas changes
                    (ox, oy) = (0, 0);
                }
                catch (Exception ex)
                {
                    _s.Dialogs.ShowError(ex);
                    return;
                }
            }
        }

        int px = Math.Clamp(ox, 0, Math.Max(0, _s.Doc.Width - 1));
        int py = Math.Clamp(oy, 0, Math.Max(0, _s.Doc.Height - 1));

        // the active tool stays; the floating paste is draggable at once (selection tools, Move, and drawing tools until the first click outside)
        layer = BeginObjectLayer(Loc.T("canvas.paste"));
        _float = FloatingSelection.FromImage(layer, img, px, py);
        _floatOwnsPendingLayer = _pendingLayer != null;
        _floatSource = null;
        _floatSourceMask = null;
        _selMask = null;
        _selRect = null;
        UpdateSelection();
        _s.RequestRender();
        _s.Status(Loc.T("canvas.pasted_drag_it_and_click", img.Width, img.Height));
    }

    // ---- layer shift and canvas size ----

    public void ShiftLayer(int dx, int dy)
    {
        if (!_s.CanEditActiveLayer()) return;
        _s.ApplyResult(Transform.Shift(_s.ActiveLayer.ToImage(), dx, dy, _wrapShift), Loc.T("canvas.layer_shift"));
    }

    private void ResizeCanvas()
    {
        CommitFloat();
        var r = _s.Dialogs.AskCanvasSize(_s.Doc.Width, _s.Doc.Height);
        if (r == null) return;
        ApplyCanvasSize(r.Width, r.Height, r.AnchorX, r.AnchorY);
    }

    /// <summary>Drag handles on the canvas edges (as in Paint): the new size keeps the content anchored to the top left corner. One undo action.</summary>
    public void ResizeCanvasTo(int width, int height)
    {
        width = Math.Clamp(width, 1, 8192);
        height = Math.Clamp(height, 1, 8192);
        if (width == _s.Doc.Width && height == _s.Doc.Height) return;
        CommitFloat();
        ApplyCanvasSize(width, height, 0, 0);
    }

    private void ApplyCanvasSize(int width, int height, double anchorX, double anchorY)
    {
        try
        {
            var doc = _s.Doc;
            doc.ResizeCanvas(width, height, anchorX, anchorY);
            if (doc.HasSheetGrid && (doc.SheetCellWidth > doc.Width || doc.SheetCellHeight > doc.Height))
            {
                doc.SheetCellWidth = doc.SheetCellHeight = 0;
                _s.RaiseOverlaysChanged();
            }
            _s.Status(Loc.T("canvas.canvas", doc.Width, doc.Height));
        }
        catch (Exception ex) { _s.Dialogs.ShowError(ex); }
    }
}
