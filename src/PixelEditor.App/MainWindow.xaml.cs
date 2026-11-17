// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PixelEditor.Core;
using PixelEditor.ViewModels;

namespace PixelEditor.App;

/// <summary>
/// Thin view: bindings live in XAML, view models make the decisions, timers live in the models too.
/// Only what belongs to WPF itself is here: the canvas raster, overlays (grid, selection), mouse, scrolling, letter hotkeys.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private WriteableBitmap _bmp = null!;
    private uint[] _comp = [];

    private bool _panning, _spaceHeld;
    private MouseButton _panButton = MouseButton.Middle;
    private Point _panStart;
    private double _panH, _panV;

    private EditorSession S => _vm.Session;
    private CanvasViewModel C => _vm.Canvas;

    public MainWindow()
    {
        PixelEditor.Core.Plugins.PluginLoader.LoadDefaults();
        InitializeComponent();

        var services = new AppServices(new DialogService(this), new ClipboardService(), new ImageStore(), new ThemeService(),
            new UiTimerFactory(), new BatchRunner(), new ShellService());
        _vm = new MainViewModel(services, App.Settings);
        DataContext = _vm;

        S.DocumentChanged += RebuildAll;
        S.CanvasResized += RebuildAll;
        S.FrameInvalidated += Render;
        S.OverlaysChanged += UpdateOverlays;
        C.PropertyChanged += OnCanvasPropertyChanged;
        C.FitRequested += FitZoom;
        C.PasteOrigin = () =>
        {
            var p = Scroller.TranslatePoint(new Point(0, 0), CanvasHost);
            return ((int)Math.Ceiling(p.X), (int)Math.Ceiling(p.Y));
        };
        _vm.CloseRequested += Close;

        RebuildAll();
        ApplyZoom();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        FitZoom();
        _vm.Startup(App.StartupFile);
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_vm.TryClose()) e.Cancel = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files) _vm.HandleDroppedFile(files[0]);
    }

    private void Recent_Opened(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, RecentMenu)) _vm.RefreshRecent();
    }

    private void Pipelines_Opened(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, PipelineMenu)) _vm.Automation.RefreshTemplates();
    }

    // ------------------------------------------------------------------ raster and overlays

    private void RebuildAll()
    {
        var doc = S.Doc;
        _bmp = new WriteableBitmap(doc.Width, doc.Height, 96, 96, PixelFormats.Bgra32, null);
        _comp = new uint[doc.Width * doc.Height];
        CanvasImage.Source = _bmp;
        CanvasHost.Width = doc.Width;
        CanvasHost.Height = doc.Height;
        UpdateOverlays();
        UpdateSelectionVisual();
        Render();
    }

    // ------------------------------------------------------------------ canvas resize grips (as in Paint)

    private string? _gripDrag;
    private Point _gripStart;
    private int _gripW0, _gripH0, _gripW, _gripH;

    /// <summary>Squares just outside the right edge, the bottom edge and the bottom right corner of the canvas.</summary>
    private void PlaceCanvasGrips()
    {
        double z = C.Zoom, size = 9 / z, gap = 4 / z, w = S.Doc.Width, h = S.Doc.Height;
        void Put(System.Windows.Shapes.Rectangle g, double cx, double cy)
        {
            g.Width = g.Height = size;
            g.StrokeThickness = 1 / z;
            Canvas.SetLeft(g, cx - size / 2);
            Canvas.SetTop(g, cy - size / 2);
        }
        Put(GripR, w + gap + size / 2, h / 2);
        Put(GripB, w / 2, h + gap + size / 2);
        Put(GripBR, w + gap + size / 2, h + gap + size / 2);
    }

    private void Grip_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement g || g.Tag is not string kind) return;
        _gripDrag = kind;
        _gripStart = e.GetPosition(CanvasHost);
        _gripW0 = _gripW = S.Doc.Width;
        _gripH0 = _gripH = S.Doc.Height;
        CanvasHost.CaptureMouse();
        e.Handled = true;
    }

    private void DragGrip(Point p)
    {
        _gripW = _gripDrag!.Contains('R') ? Math.Clamp(_gripW0 + (int)Math.Round(p.X - _gripStart.X), 1, 8192) : _gripW0;
        _gripH = _gripDrag.Contains('B') ? Math.Clamp(_gripH0 + (int)Math.Round(p.Y - _gripStart.Y), 1, 8192) : _gripH0;
        CanvasResizeFrame.Width = _gripW;
        CanvasResizeFrame.Height = _gripH;
        CanvasResizeFrame.StrokeThickness = 1.5 / C.Zoom;
        CanvasResizeFrame.StrokeDashArray = new DoubleCollection { 4, 3 };
        CanvasResizeFrame.Visibility = Visibility.Visible;
        S.Status(Loc.T("canvas.canvas", _gripW, _gripH));
    }

    private void EndGrip(bool apply)
    {
        if (_gripDrag == null) return;
        _gripDrag = null;
        CanvasResizeFrame.Visibility = Visibility.Collapsed;
        if (apply) C.ResizeCanvasTo(_gripW, _gripH);
    }

    private void Render()
    {
        var doc = S.Doc;
        if (_comp.Length != doc.Width * doc.Height) return;
        S.ComposeFrame(_comp);
        _bmp.WritePixels(new Int32Rect(0, 0, doc.Width, doc.Height), _comp, doc.Width * 4, 0);

    }

    private void OnCanvasPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(CanvasViewModel.Zoom): ApplyZoom(); break;
            case nameof(CanvasViewModel.ShowPixelGrid): UpdateOverlays(); break;
            case nameof(CanvasViewModel.ShowSelectionGrid): case nameof(CanvasViewModel.SelectionGridLocal): UpdateSelectionVisual(); break;
            case nameof(CanvasViewModel.SelectionBounds): UpdateSelectionVisual(); break;
            case nameof(CanvasViewModel.LassoPath): UpdateLassoLine(); break;
            case nameof(CanvasViewModel.TextFrame): case nameof(CanvasViewModel.TextCaret): UpdateTextOverlay(); break;
            case nameof(CanvasViewModel.Tool): case nameof(CanvasViewModel.HandlesActive): PlaceHandles(); UpdateBrushCursor(); break;
            case nameof(CanvasViewModel.PositionText): case nameof(CanvasViewModel.BrushSize):
            case nameof(CanvasViewModel.ShapeThickness): case nameof(CanvasViewModel.MirrorX): case nameof(CanvasViewModel.MirrorY):
                UpdateBrushCursor();
                break;
        }
    }

    private void FitZoom()
    {
        double vw = Scroller.ActualWidth > 100 ? Scroller.ActualWidth - 90 : 800;
        double vh = Scroller.ActualHeight > 100 ? Scroller.ActualHeight - 90 : 560;
        C.FitZoom(vw, vh);
        ApplyZoom();
    }

    private void ApplyZoom()
    {
        double z = C.Zoom;
        CanvasHost.LayoutTransform = new ScaleTransform(z, z);
        UpdateOverlays();
        UpdateSelectionVisual();
    }

    private void UpdateOverlays()
    {
        PlaceCanvasGrips();
        double z = C.Zoom;

        // checkerboard: cells of 8 screen pixels regardless of zoom (colors from the layout)
        double sq = 8.0 / z;
        var dark = new GeometryGroup();
        dark.Children.Add(new RectangleGeometry(new Rect(0, 0, 1, 1)));
        dark.Children.Add(new RectangleGeometry(new Rect(1, 1, 1, 1)));
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0xD4, 0xD4, 0xD4)), null, new RectangleGeometry(new Rect(0, 0, 2, 2))));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0xBD, 0xBD, 0xBD)), null, dark));
        CheckerRect.Fill = new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewbox = new Rect(0, 0, 2, 2),
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, 2 * sq, 2 * sq),
            ViewportUnits = BrushMappingMode.Absolute,
        };

        var pen = new Pen(new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)), 1.0 / z);
        var cell = new DrawingGroup();
        cell.Children.Add(new GeometryDrawing(null, pen, new RectangleGeometry(new Rect(0, 0, 1, 1))));
        GridRect.Fill = new DrawingBrush(cell)
        {
            TileMode = TileMode.Tile,
            Viewbox = new Rect(0, 0, 1, 1),
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, 1, 1),
            ViewportUnits = BrushMappingMode.Absolute,
        };
        GridRect.Visibility = C.ShowPixelGrid && z >= 6 ? Visibility.Visible : Visibility.Collapsed;

        // sheet grid: #258AD4 lines with 0.85 opacity, 2 screen pixels thick
        var doc = S.Doc;
        if (doc.HasSheetGrid)
        {
            int cw = doc.SheetCellWidth, ch = doc.SheetCellHeight;
            var sheetPen = new Pen(new SolidColorBrush(Color.FromArgb(0xD9, 0x25, 0x8A, 0xD4)), 2.0 / z);
            var sheetCell = new DrawingGroup();
            sheetCell.Children.Add(new GeometryDrawing(null, sheetPen, new RectangleGeometry(new Rect(0, 0, cw, ch))));
            SheetRect.Fill = new DrawingBrush(sheetCell)
            {
                TileMode = TileMode.Tile,
                Viewbox = new Rect(0, 0, cw, ch),
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(0, 0, cw, ch),
                ViewportUnits = BrushMappingMode.Absolute,
            };
            SheetRect.Visibility = Visibility.Visible;
        }
        else SheetRect.Visibility = Visibility.Collapsed;
        DrawRsiZones();
    }

    private static readonly Color[] ZoneColors =
    [
        Color.FromRgb(0x3B, 0x82, 0xF6), Color.FromRgb(0x22, 0xC5, 0x5E), Color.FromRgb(0xF5, 0x9E, 0x0B), Color.FromRgb(0xA8, 0x55, 0xF7),
        Color.FromRgb(0x06, 0xB6, 0xD4), Color.FromRgb(0xEC, 0x48, 0x99), Color.FromRgb(0xEF, 0x44, 0x44), Color.FromRgb(0x84, 0xCC, 0x16),
    ];

    /// <summary>
    /// RSI state sheet zones: each direction has its own color (semi-transparent backing), with the direction label and frame number in the zone corner.
    /// Shows where to draw south, north, east and west and which frame it is.
    /// </summary>
    private void DrawRsiZones()
    {
        RsiZones.Children.Clear();
        if (S.Doc.Rsi is not { } b) { RsiZones.Visibility = Visibility.Collapsed; return; }
        double z = C.Zoom;
        RsiZones.Width = S.Doc.Width;
        RsiZones.Height = S.Doc.Height;
        RsiZones.Visibility = Visibility.Visible;
        foreach (var zone in RsiLayout.Zones(b))
        {
            var color = ZoneColors[zone.Direction % ZoneColors.Length];
            var rect = new System.Windows.Shapes.Rectangle
            {
                Width = zone.Rect.Width,
                Height = zone.Rect.Height,
                Fill = new SolidColorBrush(Color.FromArgb(zone.Frame % 2 == 0 ? (byte)0x26 : (byte)0x14, color.R, color.G, color.B)),
                Stroke = new SolidColorBrush(Color.FromArgb(0xB0, color.R, color.G, color.B)),
                StrokeThickness = 1.5 / z,
            };
            Canvas.SetLeft(rect, zone.Rect.X);
            Canvas.SetTop(rect, zone.Rect.Y);
            RsiZones.Children.Add(rect);

            var label = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xE6, color.R, color.G, color.B)),
                CornerRadius = new CornerRadius(2.0 / z),
                Child = new TextBlock { Text = zone.Label, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, FontSize = 10 / z, Padding = new Thickness(3.0 / z, 0.5 / z, 3.0 / z, 0.5 / z) },
            };
            Canvas.SetLeft(label, zone.Rect.X + 0.2);
            Canvas.SetTop(label, zone.Rect.Y + 0.2);
            RsiZones.Children.Add(label);
        }
    }

    /// <summary>Selection frame as in the layout: a light dashed line with a blue outline and four white handles at the corners.</summary>
    private void UpdateSelectionVisual()
    {
        var shape = C.SelectionShape;
        if (shape == null) HideMask();
        PlaceHandles();
        if (C.SelectionBounds is not PixelRect rect || rect.IsEmpty)
        {
            SelectionRect.Visibility = SelectionOuter.Visibility = Visibility.Collapsed;
            SelGrid.Visibility = Visibility.Collapsed;
            SelGrid.Children.Clear();
            return;
        }

        if (shape != null)
        {
            SelectionRect.Visibility = SelectionOuter.Visibility = Visibility.Collapsed;
            DrawMask(shape);
            DrawSelectionGrid(shape.Bounds);
            return;
        }

        double z = C.Zoom;
        double line = 1.5 / z;
        foreach (var r in new[] { SelectionRect, SelectionOuter })
        {
            r.Margin = new Thickness(rect.X, rect.Y, 0, 0);
            r.Width = rect.Width;
            r.Height = rect.Height;
            r.StrokeThickness = line;
            r.Visibility = Visibility.Visible;
        }
        SelectionRect.StrokeDashArray = new DoubleCollection { 3, 3 };
        SelectionOuter.StrokeThickness = line * 2;
        DrawSelectionGrid(rect);
    }

    // ---- rotation handle (as in Paint): a circle above the selection, dragged around the center ----

    private bool _rotating;
    private (double X, double Y) _rotCenter;

    private System.Windows.Shapes.Rectangle[] Handles => [Handle1, Handle2, Handle3, Handle4, Handle5, Handle6, Handle7, Handle8];

    /// <summary>Eight handles on the frame: corners rotate, side midpoints stretch; visible with the Move tool.</summary>
    private void PlaceHandles()
    {
        var hs = Handles;
        if (C.IsRotating) return;   // while rotating, the rotated frame is drawn (ShowRotationFrame)
        if (!C.HandlesActive || C.SelectionBounds is not PixelRect r || r.IsEmpty)
        {
            foreach (var h in hs) h.Visibility = Visibility.Collapsed;
            RotHandle.Visibility = RotStem.Visibility = Visibility.Collapsed;
            return;
        }
        double z = C.Zoom, size = 7 / z;
        for (int i = 0; i < 8; i++)
        {
            var (hx, hy) = CanvasViewModel.HandlePoint(r, i);
            hs[i].Width = hs[i].Height = size;
            hs[i].StrokeThickness = 1 / z;
            hs[i].Margin = new Thickness(hx - size / 2, hy - size / 2, 0, 0);
            hs[i].Visibility = Visibility.Visible;
        }
        RotHandle.Visibility = RotStem.Visibility = Visibility.Collapsed;
    }

    private bool _scaling;

    /// <summary>Index of the stretch handle under a document point, or -1.</summary>
    private int HitScaleHandle(Point p)
    {
        if (!C.HandlesActive || C.SelectionBounds is not PixelRect r || r.IsEmpty) return -1;
        double rad = 7.0 / C.Zoom;   // C.Zoom is a double: with an int the division would be integer and the radius would be 0
        int best = -1; double bestD = double.MaxValue;
        for (int i = 0; i < 8; i++)
        {
            var (hx, hy) = CanvasViewModel.HandlePoint(r, i);
            double d = (p.X - hx) * (p.X - hx) + (p.Y - hy) * (p.Y - hy);
            if (d <= rad * rad && d < bestD) { best = i; bestD = d; }
        }
        return best;
    }

    private bool TryBeginScale(MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return false;
        int h = HitScaleHandle(e.GetPosition(CanvasHost));
        if (h < 0 || !C.BeginScaleHandle(h)) return false;
        _scaling = true;
        CanvasHost.CaptureMouse();
        e.Handled = true;
        return true;
    }

    private static Cursor CursorFor(int h) => h switch
    {
        0 or 4 => Cursors.SizeNWSE, 2 or 6 => Cursors.SizeNESW, 1 or 5 => Cursors.SizeNS, _ => Cursors.SizeWE,
    };

    private static readonly Cursor RotateCursor = RotateCursorFactory.Create();

    /// <summary>Rotation zone as in Paint: next to a frame corner, outside it and beyond the little square.</summary>
    private bool InRotateZone(Point p)
    {
        if (!C.HandlesActive || C.SelectionBounds is not PixelRect r || r.IsEmpty) return false;
        double z = C.Zoom, inner = 7 / z, outer = 26 / z;
        bool inside = p.X >= r.X && p.X <= r.X + r.Width && p.Y >= r.Y && p.Y <= r.Y + r.Height;
        if (inside) return false;
        for (int i = 0; i < 8; i += 2)
        {
            var (hx, hy) = CanvasViewModel.HandlePoint(r, i);
            double d = Math.Sqrt((p.X - hx) * (p.X - hx) + (p.Y - hy) * (p.Y - hy));
            if (d > inner && d <= outer) return true;
        }
        return false;
    }

    private double _rotStartAngle;

    private static double AngleOf(Point p, (double X, double Y) c) => Math.Atan2(p.X - c.X, -(p.Y - c.Y)) * 180 / Math.PI;

    /// <summary>Rotating by a corner handle: the angle is measured from where the handle was grabbed, so there is no jump.</summary>
    private bool TryBeginRotate(MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return false;
        var p = e.GetPosition(CanvasHost);
        if (!InRotateZone(p)) return false;
        if (C.SelectionCenter is not { } c || !C.BeginRotateHandle()) return false;
        _rotCenter = c;
        _rotStartAngle = AngleOf(p, c);
        _rotating = true;
        CanvasHost.CaptureMouse();
        e.Handled = true;
        return true;
    }

    private void RotateTo(Point p)
    {
        // 0° is up, clockwise; Shift snaps the angle to 15° steps
        double angle = AngleOf(p, _rotCenter) - _rotStartAngle;
        angle = ((angle + 180) % 360 + 360) % 360 - 180;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) angle = Math.Round(angle / 15) * 15;
        C.RotateHandleTo(angle);
        ShowRotationFrame();
    }

    /// <summary>While rotating: a rotated frame around the center (as in Paint) without the squares that would run after the growing frame.</summary>
    private void ShowRotationFrame()
    {
        if (C.RotateBaseRect is not PixelRect b) { RotFrame.Visibility = Visibility.Collapsed; return; }
        double a = C.RotateAngle * Math.PI / 180, cos = Math.Cos(a), sin = Math.Sin(a);
        double cx = b.X + b.Width / 2.0, cy = b.Y + b.Height / 2.0, hw = b.Width / 2.0, hh = b.Height / 2.0;
        Point P(double dx, double dy) => new(cx + dx * cos - dy * sin, cy + dx * sin + dy * cos);
        RotFrame.Points = new PointCollection { P(-hw, -hh), P(hw, -hh), P(hw, hh), P(-hw, hh) };
        RotFrame.StrokeThickness = 1.5 / C.Zoom;
        RotFrame.StrokeDashArray = new DoubleCollection { 3, 3 };
        RotFrame.Visibility = Visibility.Visible;
        foreach (var h in Handles) h.Visibility = Visibility.Collapsed;
    }

    /// <summary>Light lasso outline: just a polyline through the points, the shape is computed on release.</summary>
    private void UpdateLassoLine()
    {
        var pts = C.LassoPath;
        if (pts == null) { LassoLine.Visibility = Visibility.Collapsed; return; }
        LassoLine.StrokeThickness = 1.5 / C.Zoom;
        LassoLine.Points = new PointCollection(pts.Select(p => new Point(p.X + 0.5, p.Y + 0.5)));
        LassoLine.Visibility = Visibility.Visible;
    }

    /// <summary>Highlight of the pixels the brush, eraser or shape outline under the cursor will touch (symmetry included).</summary>
    private void UpdateBrushCursor()
    {
        var rects = C.BrushPreview;
        if (rects.Count == 0) { BrushCursor.Visibility = BrushCursorDark.Visibility = Visibility.Collapsed; return; }
        var group = new GeometryGroup { FillRule = FillRule.Nonzero };
        foreach (var r in rects) group.Children.Add(new RectangleGeometry(new Rect(r.X, r.Y, r.Width, r.Height)));
        group.Freeze();
        bool eraser = C.Tool == Tool.Eraser;
        BrushCursor.Stroke = new SolidColorBrush(eraser ? Color.FromRgb(0xFF, 0x7A, 0x7A) : Colors.White);
        BrushCursor.Fill = new SolidColorBrush(eraser ? Color.FromArgb(0x38, 0xFF, 0x4C, 0x4C) : Color.FromArgb(0x2A, 0xFF, 0xFF, 0xFF));
        double z = C.Zoom;
        BrushCursor.StrokeThickness = 1.2 / z;
        BrushCursorDark.StrokeThickness = 3.0 / z;
        BrushCursor.Data = BrushCursorDark.Data = group;
        BrushCursor.Visibility = BrushCursorDark.Visibility = Visibility.Visible;
    }

    private void HideMask()
    {
        SelMaskImage.Visibility = SelMaskOuter.Visibility = SelMaskInner.Visibility = Visibility.Collapsed;
    }

    /// <summary>Free-form selection: highlighted pixels and an outline along the shape edge (along pixel borders).</summary>
    private void DrawMask(SelectionMask shape)
    {
        var b = shape.Bounds;
        var bmp = new WriteableBitmap(b.Width, b.Height, 96, 96, PixelFormats.Bgra32, null);
        var px = new uint[b.Width * b.Height];
        for (int i = 0; i < px.Length; i++) if (shape.Bits[i]) px[i] = 0x404CC2FF;
        bmp.WritePixels(new Int32Rect(0, 0, b.Width, b.Height), px, b.Width * 4, 0);
        SelMaskImage.Source = bmp;
        SelMaskImage.Margin = new Thickness(b.X, b.Y, 0, 0);
        SelMaskImage.Width = b.Width;
        SelMaskImage.Height = b.Height;
        SelMaskImage.Visibility = Visibility.Visible;

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            bool On(int x, int y) => x >= 0 && y >= 0 && x < b.Width && y < b.Height && shape.Bits[y * b.Width + x];
            for (int y = 0; y < b.Height; y++)
                for (int x = 0; x < b.Width; x++)
                {
                    if (!On(x, y)) continue;
                    double gx = b.X + x, gy = b.Y + y;
                    if (!On(x, y - 1)) { ctx.BeginFigure(new Point(gx, gy), false, false); ctx.LineTo(new Point(gx + 1, gy), true, false); }
                    if (!On(x, y + 1)) { ctx.BeginFigure(new Point(gx, gy + 1), false, false); ctx.LineTo(new Point(gx + 1, gy + 1), true, false); }
                    if (!On(x - 1, y)) { ctx.BeginFigure(new Point(gx, gy), false, false); ctx.LineTo(new Point(gx, gy + 1), true, false); }
                    if (!On(x + 1, y)) { ctx.BeginFigure(new Point(gx + 1, gy), false, false); ctx.LineTo(new Point(gx + 1, gy + 1), true, false); }
                }
        }
        geo.Freeze();
        double z = C.Zoom;
        SelMaskOuter.Data = SelMaskInner.Data = geo;
        SelMaskOuter.StrokeThickness = 3.0 / z;
        SelMaskInner.StrokeThickness = 1.2 / z;
        SelMaskInner.StrokeDashArray = new DoubleCollection { 3, 3 };
        SelMaskOuter.Visibility = SelMaskInner.Visibility = Visibility.Visible;
    }

    /// <summary>A click on the gray area around the canvas (not on the scroll bars) clears the selection.</summary>
    private void Workspace_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // a click on the canvas hands the keyboard back to the hotkeys (a slider or combo box would otherwise keep it)
        if (Keyboard.FocusedElement is TextBox or ComboBox or System.Windows.Controls.Slider || LayersList.IsKeyboardFocusWithin) Keyboard.ClearFocus();
        if (TryBeginScale(e) || TryBeginRotate(e)) return;
        if (e.OriginalSource is not DependencyObject src) return;
        for (DependencyObject? d = src; d != null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (ReferenceEquals(d, CanvasHost) || d is System.Windows.Controls.Primitives.ScrollBar) return;
        if (C.HasSelection) C.DeselectCommand.Execute(null);
    }

    /// <summary>
    /// Selection coordinate grid: lines between pixels inside the frame and column and row numbers (canvas coordinates) outside, at the top and left.
    /// Labels use a step that depends on the zoom so they do not run together.
    /// </summary>
    private void DrawSelectionGrid(PixelRect rect)
    {
        SelGrid.Children.Clear();
        if (!C.ShowSelectionGrid)
        {
            SelGrid.Visibility = Visibility.Collapsed;
            return;
        }
        double z = C.Zoom;
        SelGrid.Width = S.Doc.Width;
        SelGrid.Height = S.Doc.Height;
        SelGrid.Visibility = Visibility.Visible;

        var lineBrush = new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF));
        var darkBrush = new SolidColorBrush(Color.FromArgb(0x50, 0, 0, 0));
        double t = 1.0 / z;
        void Line(double x1, double y1, double x2, double y2)
        {
            foreach (var (brush, off) in new[] { (darkBrush, t), (lineBrush, 0.0) })
                SelGrid.Children.Add(new System.Windows.Shapes.Line { X1 = x1 + off, Y1 = y1 + off, X2 = x2 + off, Y2 = y2 + off, Stroke = brush, StrokeThickness = t });
        }
        for (int i = 1; i < rect.Width; i++) Line(rect.X + i, rect.Y, rect.X + i, rect.Y + rect.Height);
        for (int j = 1; j < rect.Height; j++) Line(rect.X, rect.Y + j, rect.X + rect.Width, rect.Y + j);

        int step = z >= 32 ? 1 : z >= 16 ? 2 : z >= 8 ? 4 : z >= 4 ? 8 : 16;
        double fs = 11 / z;
        void Label(string text, double x, double y)
        {
            var tb = new TextBlock { Text = text, FontSize = fs, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, FontFamily = new FontFamily("Consolas"), Padding = new Thickness(2.0 / z, 0.5 / z, 2.0 / z, 0.5 / z) };
            // dense backing with a thin light frame: the digits are readable on both light and dark pictures
            var b = new Border { Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14)), BorderBrush = new SolidColorBrush(Color.FromRgb(0x8A, 0xD7, 0xFF)), BorderThickness = new Thickness(0.8 / z), Child = tb, CornerRadius = new CornerRadius(2.0 / z) };
            Canvas.SetLeft(b, x);
            Canvas.SetTop(b, y);
            SelGrid.Children.Add(b);
        }
        double lh = fs * 1.5;
        for (int i = 0; i < rect.Width; i += step) Label(((C.SelectionGridLocal ? 0 : rect.X) + i).ToString(), rect.X + i + 0.05, rect.Y - lh - 3 / z);
        for (int j = 0; j < rect.Height; j += step)
        {
            var num = ((C.SelectionGridLocal ? 0 : rect.Y) + j).ToString();
            Label(num, rect.X - fs * 0.62 * num.Length - 7 / z, rect.Y + j + 0.05);
        }
        Label($"{rect.Width}×{rect.Height}", rect.X, rect.Y + rect.Height + 2 / z);
    }

    // ------------------------------------------------------------------ mouse

    private (int X, int Y) Pos(MouseEventArgs e)
    {
        var p = e.GetPosition(CanvasHost);
        return ((int)Math.Floor(p.X), (int)Math.Floor(p.Y));
    }

    private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // pan: middle button, or left button while Space is held
        if (e.ChangedButton == MouseButton.Middle || (e.ChangedButton == MouseButton.Left && _spaceHeld))
        {
            _panButton = e.ChangedButton;
            _panning = true;
            _panStart = e.GetPosition(Scroller);
            _panH = Scroller.HorizontalOffset;
            _panV = Scroller.VerticalOffset;
            CanvasHost.CaptureMouse();
            return;
        }
        if (e.ChangedButton is not (MouseButton.Left or MouseButton.Right)) return;
        if (TryBeginScale(e) || TryBeginRotate(e)) return;   // fallback route if the event reached the canvas itself

        var (x, y) = Pos(e);
        bool alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        if (C.PointerDown(x, y, e.ChangedButton == MouseButton.Right, alt)) CanvasHost.CaptureMouse();
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_gripDrag != null) { DragGrip(e.GetPosition(CanvasHost)); return; }
        if (_rotating) { CanvasHost.Cursor = RotateCursor; RotateTo(e.GetPosition(CanvasHost)); return; }
        if (_scaling)
        {
            var q = e.GetPosition(CanvasHost);
            C.ScaleHandleTo(q.X, q.Y, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            return;
        }
        if (!_spaceHeld && !_panning)
        {
            var hp = e.GetPosition(CanvasHost);
            int hh = HitScaleHandle(hp);
            CanvasHost.Cursor = hh >= 0 ? CursorFor(hh) : InRotateZone(hp) ? RotateCursor : (C.HandlesActive && C.IsOverSelection((int)Math.Floor(hp.X), (int)Math.Floor(hp.Y))) ? Cursors.SizeAll : Cursors.Cross;
        }
        if (_panning)
        {
            var p = e.GetPosition(Scroller);
            Scroller.ScrollToHorizontalOffset(_panH - (p.X - _panStart.X));
            Scroller.ScrollToVerticalOffset(_panV - (p.Y - _panStart.Y));
            return;
        }
        var (x, y) = Pos(e);
        C.UpdatePosition(x, y);
        C.PointerMove(x, y);
    }

    private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_gripDrag != null)
        {
            EndGrip(apply: true);
            CanvasHost.ReleaseMouseCapture();
            return;
        }
        if (_rotating)
        {
            _rotating = false;
            RotFrame.Visibility = Visibility.Collapsed;
            C.EndRotateHandle();
            PlaceHandles();
            CanvasHost.ReleaseMouseCapture();
            return;
        }
        if (_scaling)
        {
            _scaling = false;
            C.EndScaleHandle();
            CanvasHost.ReleaseMouseCapture();
            return;
        }
        if (_panning && e.ChangedButton == _panButton)
        {
            _panning = false;
            CanvasHost.ReleaseMouseCapture();
            return;
        }
        C.PointerUp();
        if (CanvasHost.IsMouseCaptured) CanvasHost.ReleaseMouseCapture();
    }

    private void Canvas_LostCapture(object sender, MouseEventArgs e)
    {
        if (_gripDrag != null) { EndGrip(apply: false); return; }
        if (_rotating) { _rotating = false; RotFrame.Visibility = Visibility.Collapsed; C.EndRotateHandle(); PlaceHandles(); return; }
        if (_scaling) { _scaling = false; C.EndScaleHandle(); return; }
        _panning = false;
        C.PointerUp();
    }

    private void Canvas_MouseLeave(object sender, MouseEventArgs e) => C.ClearPosition();

    private int _wheelRemainder;

    /// <summary>
    /// The wheel over the canvas always zooms around the point under the cursor (it used to need Ctrl, otherwise the canvas just scrolled).
    /// Shift+wheel: horizontal scroll, Ctrl+wheel: ordinary vertical scroll.
    /// </summary>
    private void Scroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        if (mods.HasFlag(ModifierKeys.Shift))
        {
            Scroller.ScrollToHorizontalOffset(Scroller.HorizontalOffset - e.Delta);
            e.Handled = true;
            return;
        }
        if (mods.HasFlag(ModifierKeys.Control)) return;

        // touchpads and precision wheels send deltas below 120: accumulate to a whole step
        _wheelRemainder += e.Delta;
        int steps = _wheelRemainder / 120;
        _wheelRemainder -= steps * 120;
        if (steps != 0) ZoomAt(e.GetPosition(Scroller), steps);
        e.Handled = true;
    }

    /// <summary>
    /// Changes the zoom by steps (positive zooms in) so that the canvas point under viewportPoint (viewport coordinates)
    /// stays in place. If the whole canvas fits in the window it simply stays centered.
    /// </summary>
    public void ZoomAt(Point viewportPoint, int steps)
    {
        var docPoint = Scroller.TranslatePoint(viewportPoint, CanvasHost); // in document pixels
        double before = C.Zoom;
        for (int i = 0; i < Math.Abs(steps); i++)
            (steps > 0 ? C.ZoomInCommand : C.ZoomOutCommand).Execute(null);
        if (C.Zoom == before) return;

        Scroller.UpdateLayout();
        var now = CanvasHost.TranslatePoint(docPoint, Scroller);
        Scroller.ScrollToHorizontalOffset(Scroller.HorizontalOffset + now.X - viewportPoint.X);
        Scroller.ScrollToVerticalOffset(Scroller.VerticalOffset + now.Y - viewportPoint.Y);
        Scroller.UpdateLayout();
    }

    // ------------------------------------------------------------------ keys and small handlers

    private static void Run(ICommand command)
    {
        if (command.CanExecute(null)) command.Execute(null);
    }

    private void Window_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space) EndSpacePan();
    }

    private void Window_Deactivated(object? sender, EventArgs e) => EndSpacePan();

    private void EndSpacePan()
    {
        _spaceHeld = false;
        CanvasHost.ClearValue(FrameworkElement.CursorProperty);
    }

    /// <summary>Dashed frame and caret of the text being typed (the preview pixels themselves are composed by the session).</summary>
    private void UpdateTextOverlay()
    {
        if (C.TextFrame is not PixelRect f || C.TextCaret is not { } caret)
        {
            TextFrameRect.Visibility = TextCaretRect.Visibility = Visibility.Collapsed;
            return;
        }
        double z = C.Zoom;
        TextFrameRect.Margin = new Thickness(f.X, f.Y, 0, 0);
        TextFrameRect.Width = f.Width;
        TextFrameRect.Height = f.Height;
        TextFrameRect.StrokeThickness = 1.5 / z;
        TextFrameRect.Visibility = Visibility.Visible;
        double cw = Math.Max(1.0 / z * 1.5, 0.3);
        TextCaretRect.Margin = new Thickness(caret.X - cw / 2, caret.Y, 0, 0);
        TextCaretRect.Width = cw;
        TextCaretRect.Height = caret.Height;
        TextCaretRect.StrokeThickness = 0.5 / z;
        TextCaretRect.Visibility = Visibility.Visible;
    }

    /// <summary>After a font or scale change the keyboard goes back to the text.</summary>
    private void TextOption_Done(object? sender, EventArgs e)
    {
        if (C.IsTextEditing) Dispatcher.BeginInvoke(new Action(Keyboard.ClearFocus), System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>Characters (any layout, Cyrillic included) go to the text while it is being typed.</summary>
    private void Window_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (!C.IsTextEditing || Keyboard.FocusedElement is TextBox or ComboBox) return;
        C.TextInsert(e.Text);
        e.Handled = true;
    }

    /// <summary>While text is typed every key belongs to it: no tool letters, no Space pan, no Delete of the selection.</summary>
    private bool HandleTextKey(KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control), shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift), alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (ctrl)
        {
            switch (key)
            {
                case Key.Enter: C.TextCommit(); break;
                case Key.Z: _vm.UndoCommand.Execute(null); break;
                case Key.Y: _vm.RedoCommand.Execute(null); break;
                case Key.V: C.TextPaste(); break;
                case Key.A: C.TextSelectAll(); break;
                case Key.C: C.TextCopy(false); break;
                case Key.X: C.TextCopy(true); break;
                default: return false;   // other Ctrl shortcuts (save, open...) stay available; they commit the text first
            }
            return true;
        }
        switch (key)
        {
            case Key.Escape: C.TextCancel(); break;
            case Key.Enter: C.TextInsert("\n"); break;
            case Key.Back: C.TextBackspace(); break;
            case Key.Delete: C.TextDelete(); break;
            case Key.Left when alt: C.TextNudge(-1, 0); break;
            case Key.Right when alt: C.TextNudge(1, 0); break;
            case Key.Up when alt: C.TextNudge(0, -1); break;
            case Key.Down when alt: C.TextNudge(0, 1); break;
            case Key.Left: C.TextMoveCaret(TextMove.Left, shift); break;
            case Key.Right: C.TextMoveCaret(TextMove.Right, shift); break;
            case Key.Up: C.TextMoveCaret(TextMove.Up, shift); break;
            case Key.Down: C.TextMoveCaret(TextMove.Down, shift); break;
            case Key.Home: C.TextMoveCaret(TextMove.Home, shift); break;
            case Key.End: C.TextMoveCaret(TextMove.End, shift); break;
            case Key.Space: break;   // the character itself arrives as text input
            default: return !alt;   // letters, digits, F keys: swallowed here, the character arrives as text input
        }
        return true;
    }

    /// <summary>Ctrl shortcuts are declared in XAML (InputBindings). Here: tool letters, arrows and service keys without modifiers.</summary>
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (C.IsTextEditing && Keyboard.FocusedElement is not (TextBox or ComboBox) && HandleTextKey(e)) { e.Handled = true; return; }
        // text boxes, combo boxes and sliders own the keyboard (arrows, letters, space): no hotkeys there
        if (Keyboard.FocusedElement is TextBox or ComboBox or System.Windows.Controls.Slider) return;
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        // the layers list uses the arrows to move between layers and letters to jump to a name: no layer shift or tool letters there
        bool inLayers = LayersList.IsKeyboardFocusWithin;

        // space: the canvas moves with the left button (hand); do not interfere with buttons and lists that need the space key
        if (e.Key == Key.Space && !ctrl && Keyboard.FocusedElement is not (System.Windows.Controls.Primitives.ButtonBase or ListBoxItem or MenuItem or ComboBox or System.Windows.Controls.Slider))
        {
            _spaceHeld = true;
            CanvasHost.Cursor = Cursors.Hand;
            e.Handled = true;
            return;
        }
        if (inLayers && e.Key is >= Key.A and <= Key.Z) return;
        if (e.Key == Key.F && !ctrl && !shift) { FitZoom(); e.Handled = true; return; }

        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            if (inLayers) return;
            int dx = e.Key == Key.Left ? -1 : e.Key == Key.Right ? 1 : 0;
            int dy = e.Key == Key.Up ? -1 : e.Key == Key.Down ? 1 : 0;
            if (ctrl) { C.ShiftLayer(dx, dy); e.Handled = true; }
            else if (C.Nudge(dx, dy, shift)) e.Handled = true;
            return;
        }
        if (ctrl) return;

        Tool? tool = e.Key switch
        {
            Key.B => Tool.Pencil,
            Key.E => Tool.Eraser,
            Key.G => Tool.Fill,
            Key.I => Tool.Picker,
            Key.L => Tool.Line,
            Key.R => Tool.Rect,
            Key.O => Tool.Ellipse,
            Key.M => Tool.Select,
            Key.V => Tool.Move,
            Key.K => Tool.BgEraser,
            Key.W => Tool.Wand,
            Key.S => Tool.Lasso,
            Key.D => Tool.Gradient,
            Key.U => Tool.Shade,
            Key.T => Tool.Text,
            Key.P => Tool.Polygon,
            _ => null,
        };
        if (tool != null)
        {
            C.Tool = tool.Value;
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Delete: Run(C.DeleteCommand); break;
            case Key.Escape: Run(C.DeselectCommand); break;
            case Key.Enter when C.IsBuildingPolygon: C.FinishPolygon(); break;
            case Key.Back when C.IsBuildingPolygon: C.UndoPolygonNode(); break;
            case Key.X: Run(_vm.Colors.SwapColorsCommand); break;
            case Key.OemPlus or Key.Add: Run(C.ZoomInCommand); break;
            case Key.OemMinus or Key.Subtract: Run(C.ZoomOutCommand); break;
            default: return;
        }
        e.Handled = true;
    }

    private void HexBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        BindingOperations.GetBindingExpression((TextBox)sender, TextBox.TextProperty)?.UpdateSource();
    }

    /// <summary>The "⋯" button in the palette header opens its context menu below itself (a left click does not open a ContextMenu by default).</summary>
    private void PaletteMore_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button) return;
        menu.PlacementTarget = button;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    /// <summary>The layer name editor appears: focus it and select the text (the model decided to start editing).</summary>
    private void LayerNameBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not TextBox box || e.NewValue is not true) return;
        box.Dispatcher.BeginInvoke(() => { box.Focus(); Keyboard.Focus(box); box.SelectAll(); }, System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>Focus loss commits the rename (Enter and Esc are key bindings on the box itself).</summary>
    private void LayerNameBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox { DataContext: LayerItemViewModel item }) item.CommitEdit();
    }

    /// <summary>Left click on a palette color sets the primary color, right click the secondary one.</summary>
    private void PaletteList_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject src) return;
        if (sender is not ListBox list) return;
        if (ItemsControl.ContainerFromElement(list, src) is not ListBoxItem { DataContext: uint color }) return;
        bool right = e.ChangedButton == MouseButton.Right;
        if (!right && e.ChangedButton != MouseButton.Left) return;
        _vm.Colors.PaletteClick(color, right);
        // the palette keeps its selection on left click; the recent row has no selection at all
        if (right || ReferenceEquals(list, RecentList)) e.Handled = true;
    }
}
