// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PixelEditor.App;
using PixelEditor.App.Themes;
using PixelEditor.Core;
using PixelEditor.Core.Ops;
using PixelEditor.ViewModels;

// A view smoke test: brings up a real window and checks that it correctly reflects the view models,
// that styles apply by themselves and that every dialog opens on its own model without binding errors.
// The model logic is checked separately (tests/PixelEditor.ViewModels.Tests).
// Run: dotnet run --project tests/PixelEditor.GuiSmoke   (needs an interactive Windows desktop)
// PIXED_SMOKE_SHOTS=<folder>: also save the window look to PNG (a WPF render, independent of what is on the screen).

return Smoke.Run();

/// <summary>Collects WPF binding error messages: in XAML they otherwise stay silent.</summary>
sealed class BindingErrorListener : TraceListener
{
    public List<string> Errors { get; } = new();
    public override void Write(string? message) { }
    public override void WriteLine(string? message)
    {
        if (!string.IsNullOrWhiteSpace(message)) Errors.Add(message);
    }
}

static class Smoke
{
    private static int _fail;
    private static readonly BindingErrorListener Binding = new();
    private static string? _shotsDir;

    private static void Pump() =>
        Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));

    private static void Check(string name, bool ok, string? detail = null)
    {
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}{(ok || detail == null ? "" : "  -> " + detail)}");
        if (!ok) _fail++;
    }

    private static void Guard(string name, Action body)
    {
        Console.WriteLine($"...   {name}");
        try { body(); }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL  {name}  -> исключение: {ex.GetType().Name}: {ex.Message}");
            _fail++;
        }
    }

    private static uint PixelAt(BitmapSource bmp, int x, int y)
    {
        var px = new uint[1];
        bmp.CopyPixels(new Int32Rect(x, y, 1, 1), px, 4, 0);
        return px[0];
    }

    /// <summary>Whether the visual tree has a TextBlock with this text (meaning the binding really reached the screen).</summary>
    private static bool HasText(DependencyObject root, string text)
    {
        if (root is TextBlock tb && tb.Text == text) return true;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (HasText(VisualTreeHelper.GetChild(root, i), text)) return true;
        return false;
    }

    private static T? FindVisual<T>(DependencyObject root, Func<T, bool> match) where T : DependencyObject
    {
        if (root is T t && match(t)) return t;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var found = FindVisual(VisualTreeHelper.GetChild(root, i), match);
            if (found != null) return found;
        }
        return null;
    }

    private static Button? CaptionButton(Window w, string action) =>
        FindVisual<Button>(w, b => Ui.GetWindowAction(b) == action);

    private static void PressKey(Window win, Key key)
    {
        var source = PresentationSource.FromVisual(win)!;
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        win.RaiseEvent(args);
    }

    private static readonly System.Text.RegularExpressions.Regex CyrillicRx = new("[А-Яа-яЁё]");

    /// <summary>Russian captions, tooltips and menu item headers in the window tree (to check the English mode).</summary>
    private static List<string> FindCyrillic(DependencyObject root)
    {
        var found = new List<string>();
        void Scan(DependencyObject d)
        {
            object?[] texts = d switch
            {
                Window w => [w.Title, w.ToolTip],
                TextBlock t => [t.Text, t.ToolTip],
                MenuItem m => [m.Header, m.ToolTip],
                ContentControl c => [c.Content, c.ToolTip],
                _ => [(d as FrameworkElement)?.ToolTip],
            };
            foreach (var o in texts)
                if (o is string s && CyrillicRx.IsMatch(s)) found.Add(s.Length > 50 ? s[..50] : s);
            foreach (var child in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>()) Scan(child);
        }
        Scan(root);
        return found;
    }

    private static Color BrushColor(string key) => ((SolidColorBrush)Application.Current.FindResource(key)).Color;

    private static void Shot(Window w, string name)
    {
        if (_shotsDir == null) return;
        w.UpdateLayout();
        int width = Math.Max(1, (int)w.ActualWidth), height = Math.Max(1, (int)w.ActualHeight);
        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(w);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(Path.Combine(_shotsDir, name + ".png"));
        enc.Save(fs);
    }

    public static int Run()
    {
        // isolate the settings, recent files and autosave from the real user data
        var dataDir = Path.Combine(Path.GetTempPath(), "pixed-gui-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDir);
        Environment.SetEnvironmentVariable("PIXED_DATA_DIR", dataDir);
        // the start screen is a modal window that would block the run: switch it off in the isolated settings (it is opened explicitly below)
        File.WriteAllText(Path.Combine(dataDir, "settings.json"), "{\"ShowStartScreen\": false}");

        _shotsDir = Environment.GetEnvironmentVariable("PIXED_SMOKE_SHOTS");
        // PIXED_SMOKE_LANG=en: window shots in another language (some checks assume Russian and may fail)
        PixelEditor.ViewModels.Loc.Instance.SetLanguage(Environment.GetEnvironmentVariable("PIXED_SMOKE_LANG"));
        if (!string.IsNullOrEmpty(_shotsDir)) Directory.CreateDirectory(_shotsDir);

        var thread = new Thread(Body);
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        try { Directory.Delete(dataDir, recursive: true); } catch { }
        Console.WriteLine(_fail == 0 ? "\nВсе проверки пройдены" : $"\nПровалено проверок: {_fail}");
        return _fail == 0 ? 0 : 1;
    }

    private static void Body()
    {
        PresentationTraceSources.Refresh();
        PresentationTraceSources.DataBindingSource.Listeners.Add(Binding);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;

        var app = new PixelEditor.App.App();
        app.InitializeComponent();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        ThemeManager.Initialize();
        ThemeManager.Apply("Dark");

        PixelEditor.ViewModels.Loc.Instance.LanguageChanged += () => Console.WriteLine("DBGSTACK " + Environment.StackTrace);
        var win = new MainWindow();
        win.Show();
        Console.WriteLine("DBG lang=" + PixelEditor.ViewModels.Loc.Instance.CurrentLanguage + " env=" + Environment.GetEnvironmentVariable("PIXED_SMOKE_LANG") + " file=" + PixelEditor.ViewModels.Loc.Instance["menu.file"] + " ru=" + HasText(win, "Слои") + " en=" + HasText(win, "Layers"));
        Pump(); Console.WriteLine("DBG2 ru=" + HasText(win, "Слои") + " en=" + HasText(win, "Layers"));
        Pump();
        var vm = (MainViewModel)win.DataContext;
        var canvasHost = (Grid)win.FindName("CanvasHost");
        var canvasImage = (Image)win.FindName("CanvasImage");
        var selection = (System.Windows.Shapes.Rectangle)win.FindName("SelectionRect");
        var handle1 = (System.Windows.Shapes.Rectangle)win.FindName("Handle1");
        var sheetRect = (System.Windows.Shapes.Rectangle)win.FindName("SheetRect");

        Guard("окно связано с моделью", () =>
        {
            Check("DataContext это MainViewModel", win.DataContext is MainViewModel);
            Check("в заголовке название документа", HasText(win, vm.DocumentName), vm.DocumentName);
            int dw = vm.Session.Doc.Width, dh = vm.Session.Doc.Height;
            Check("размер по умолчанию 64×64 (если в настройках не менялся)", dw == 64 && dh == 64, $"{dw}×{dh}");
            Check("холст соответствует документу", canvasHost.Width == dw && canvasHost.Height == dh);
            Check("растр холста создан", canvasImage.Source is BitmapSource bs && bs.PixelWidth == dw && bs.PixelHeight == dh);
            Check("подпись над рабочей областью", HasText(win, $"{dw} × {dh} px"));
        });

        Guard("стили: токены темы и автоматическое оформление", () =>
        {
            Check("тёмная палитра: Surface #202020", BrushColor("Brush.Surface") == Color.FromRgb(0x20, 0x20, 0x20));
            Check("тёмная палитра: Accent #4CC2FF", BrushColor("Brush.Accent") == Color.FromRgb(0x4C, 0xC2, 0xFF));
            Check("окно получило фон из токена", ((SolidColorBrush)win.Background).Color == BrushColor("Brush.Surface"));

            // a new element without its own style picks up the shared look
            var probe = new Window { Width = 200, Height = 120, ShowInTaskbar = false, Content = new StackPanel { Children = { new Button { Content = "x" }, new TextBox(), new CheckBox() } } };
            probe.Show();
            Pump();
            var panel = (StackPanel)probe.Content;
            Check("новая кнопка оформлена (Surface3)", ((SolidColorBrush)((Button)panel.Children[0]).Background).Color == BrushColor("Brush.Surface3"));
            Check("новое поле ввода оформлено (Field)", ((SolidColorBrush)((TextBox)panel.Children[1]).Background).Color == BrushColor("Brush.Field"));
            Check("у нового окна стиль Window", ((SolidColorBrush)probe.Background).Color == BrushColor("Brush.Surface"));
            probe.Close();
        });

        Guard("штрих из модели виден в растре и меняет заголовок", () =>
        {
            vm.Colors.SetPrimary(Pixel.Rgb(255, 0, 0));
            vm.Canvas.PointerDown(1, 1, false, false);
            vm.Canvas.PointerMove(3, 1);
            vm.Canvas.PointerUp();
            Pump();
            var bmp = (BitmapSource)canvasImage.Source;
            Check("красный пиксель в растре", PixelAt(bmp, 2, 1) == 0xFFFF0000u, $"{PixelAt(bmp, 2, 1):X8}");
            Check("в заголовке отметка несохранённых правок", HasText(win, "Без имени •"), vm.DocumentName);

            // the status changes without changing the "state": this path used to not reach the screen
            vm.Canvas.Tool = Tool.Select;
            vm.Canvas.PointerDown(2, 2, false, false); vm.Canvas.PointerMove(12, 10); vm.Canvas.PointerUp();
            Pump();
            Check("статус-бар на экране показывает актуальный статус", HasText(win, vm.Session.StatusText), vm.Session.StatusText);
            Check("ручка выделения видна", handle1.Visibility == Visibility.Visible);
            {
                // hitting the handles is computed in the same coordinates where they are drawn (a geometry check without the mouse)
                var hit = typeof(MainWindow).GetMethod("HitScaleHandle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
                var zone = typeof(MainWindow).GetMethod("InRotateZone", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
                var rect = vm.Canvas.SelectionBounds!.Value;
                bool all = true;
                var got = new List<int>();
                for (int i = 0; i < 8; i++)
                {
                    var (hx, hy) = CanvasViewModel.HandlePoint(rect, i);
                    int r = (int)hit.Invoke(win, [new Point(hx + 0.05, hy - 0.05)])!;
                    got.Add(r);
                    all &= r == i;
                }
                Check("курсор-ручки: все 8 квадратиков находятся по их координатам", all, $"tool={vm.Canvas.Tool} zoom={vm.Canvas.Zoom} rect={rect} got={string.Join(",", got)}");
                var (cx, cy) = CanvasViewModel.HandlePoint(rect, 2);
                Check("зона вращения у угла снаружи рамки", (bool)zone.Invoke(win, [new Point(cx + 0.8, cy - 0.8)])!);
                Check("в центре рамки зоны вращения нет", !(bool)zone.Invoke(win, [new Point(rect.X + rect.Width / 2.0, rect.Y + rect.Height / 2.0)])!);
            }
            {
                // the canvas resize grips are hit-testable outside the canvas edges and resize the canvas through the model
                var host = (FrameworkElement)win.FindName("CanvasHost");
                var grip = (FrameworkElement)win.FindName("GripBR");
                var center = grip.TranslatePoint(new Point(grip.ActualWidth / 2, grip.ActualHeight / 2), host);
                var found = System.Windows.Media.VisualTreeHelper.HitTest(host, center)?.VisualHit as FrameworkElement;
                Check("ручка размера холста находится под курсором за краем холста", found?.Name == "GripBR", $"found={found?.Name} at {center}");
                Check("ручка размера холста за пределами холста", center.X > vm.Session.Doc.Width && center.Y > vm.Session.Doc.Height);
                int w0 = vm.Session.Doc.Width, h0 = vm.Session.Doc.Height;
                vm.Canvas.ResizeCanvasTo(w0 + 10, h0 + 6);
                Check("перетаскивание ручки меняет холст", vm.Session.Doc.Width == w0 + 10 && vm.Session.Doc.Height == h0 + 6);
                vm.UndoCommand.Execute(null);
                Check("размер холста возвращается отменой", vm.Session.Doc.Width == w0 && vm.Session.Doc.Height == h0);
            }
            vm.Canvas.Tool = Tool.Pencil;
            vm.Canvas.DeselectCommand.Execute(null);
        });

        Guard("перерисовка при отмене", () =>
        {
            vm.UndoCommand.Execute(null);
            Pump();
            Check("пиксель исчез из растра", PixelAt((BitmapSource)canvasImage.Source, 2, 1) == 0u);
        });

        Guard("масштаб: преобразование холста следует за моделью", () =>
        {
            for (int i = 0; i < 3; i++) vm.Canvas.ZoomInCommand.Execute(null);
            Pump();
            var scale = (ScaleTransform)canvasHost.LayoutTransform;
            Check("ScaleTransform равен масштабу модели", scale.ScaleX == vm.Canvas.Zoom, $"{scale.ScaleX} vs {vm.Canvas.Zoom}");
        });

        Guard("свой заголовок окна", () =>
        {
            var min = CaptionButton(win, "Minimize");
            var max = CaptionButton(win, "Maximize");
            var close = CaptionButton(win, "Close");
            Check("есть кнопки свернуть, развернуть, закрыть", min != null && max != null && close != null);
            Check("у главного окна все три кнопки видны", min!.IsVisible && max!.IsVisible && close!.IsVisible);
            Check("название приложения в заголовке", HasText(win, "Pixel Editor"));

            var theme = FindVisual<ToggleButton>(win, b => b.Command == vm.ToggleThemeCommand);
            Check("переключатель темы в заголовке", theme != null);
            Check("переключатель показывает текущую тему", HasText(win, vm.ThemeLabel), vm.ThemeLabel);

            max!.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Pump();
            Check("кнопка разворачивает окно", win.WindowState == WindowState.Maximized);
            max.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Pump();
            Check("повторное нажатие возвращает окно", win.WindowState == WindowState.Normal);
        });

        Guard("зум сразу после создания листа состояния", () =>
        {
            var scroller = (ScrollViewer)win.FindName("Scroller");
            var d = new Document(96, 128);
            d.SheetCellWidth = d.SheetCellHeight = 32;
            d.Rsi = new RsiBinding("x.rsi", "walk", 4, 3, 32, 32, RsiState.UniformDelays(4, 3, 0.1));
            vm.Session.SetDocument(d, null);
            double z0 = vm.Canvas.Zoom;
            var wheel = new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, 120)
            {
                RoutedEvent = UIElement.PreviewMouseWheelEvent,
                Source = scroller,
            };
            scroller.RaiseEvent(wheel);
            Check("колесо сразу увеличивает масштаб (без паузы)", vm.Canvas.Zoom > z0, $"{z0} -> {vm.Canvas.Zoom}");
            Pump();
            vm.Canvas.ZoomOutCommand.Execute(null);
            Pump();
            double z1 = vm.Canvas.Zoom;
            win.ZoomAt(new Point(scroller.ViewportWidth / 2, scroller.ViewportHeight / 2), +1);
            Check("ZoomAt работает на листе состояния", vm.Canvas.Zoom > z1, $"{z1} -> {vm.Canvas.Zoom}");
            vm.Session.SetDocument(new Document(32, 32), null);
            Pump();
        });

        Guard("колесо: масштаб вокруг точки под курсором", () =>
        {
            var scroller = (ScrollViewer)win.FindName("Scroller");
            vm.Session.SetDocument(new Document(256, 256), null);
            Pump();
            // bring the zoom to one where the canvas is surely larger than the viewport
            while (vm.Canvas.Zoom < 8) vm.Canvas.ZoomInCommand.Execute(null);
            Pump();
            scroller.UpdateLayout();
            Check("холст больше окна просмотра", scroller.ScrollableWidth > 0 && scroller.ScrollableHeight > 0,
                $"{scroller.ScrollableWidth}x{scroller.ScrollableHeight}");

            // stand in the center so the offset does not hit the edge
            scroller.ScrollToHorizontalOffset(scroller.ScrollableWidth / 2);
            scroller.ScrollToVerticalOffset(scroller.ScrollableHeight / 2);
            scroller.UpdateLayout();

            double Worst(Point v, int steps)
            {
                var before = scroller.TranslatePoint(v, canvasHost);
                win.ZoomAt(v, steps);
                var after = scroller.TranslatePoint(v, canvasHost);
                return Math.Max(Math.Abs(after.X - before.X), Math.Abs(after.Y - before.Y));
            }

            var p1 = new Point(scroller.ViewportWidth * 0.3, scroller.ViewportHeight * 0.4);
            double z0 = vm.Canvas.Zoom;
            double d1 = Worst(p1, +1);
            Check("увеличение: точка под курсором осталась на месте", d1 < 0.75, $"смещение {d1:F2} px документа");
            Check("масштаб вырос", vm.Canvas.Zoom > z0);

            var p2 = new Point(scroller.ViewportWidth * 0.7, scroller.ViewportHeight * 0.6);
            double d2 = Worst(p2, -1);
            Check("уменьшение: точка под курсором осталась на месте", d2 < 0.75, $"смещение {d2:F2} px документа");

            double d3 = Worst(p1, +2);
            Check("на две ступени сразу тоже без сдвига", d3 < 0.75, $"смещение {d3:F2} px документа");

            // the extreme zoom: nothing breaks
            for (int i = 0; i < 30; i++) win.ZoomAt(p1, +1);
            Check("предел масштаба 6400%", vm.Canvas.Zoom == 64);
            for (int i = 0; i < 30; i++) win.ZoomAt(p1, -1);
            Check("предел масштаба 6.25%", vm.Canvas.Zoom == 0.0625);

            vm.Session.SetDocument(new Document(32, 32), null);
            Pump();
        });

        Guard("выделение: рамка на холсте", () =>
        {
            vm.Canvas.SelectAllCommand.Execute(null);
            Pump();
            Check("рамка видна и равна холсту", selection.Visibility == Visibility.Visible && selection.Width == 32 && selection.Height == 32);
            vm.Canvas.DeselectCommand.Execute(null);
            Pump();
            Check("рамка скрыта после снятия", selection.Visibility == Visibility.Collapsed);
        });

        Guard("клавиша M переключает инструмент через представление", () =>
        {
            vm.Canvas.Tool = Tool.Pencil;
            PressKey(win, Key.M);
            Check("инструмент стал Select", vm.Canvas.Tool == Tool.Select);
            PressKey(win, Key.B);
            Check("инструмент стал Pencil", vm.Canvas.Tool == Tool.Pencil);
        });

        Guard("размер холста: растр и холст пересоздаются", () =>
        {
            vm.Session.Doc.ResizeCanvas(48, 40, 0, 0);
            Pump();
            Check("размер холста", canvasHost.Width == 48 && canvasHost.Height == 40);
            Check("новый растр", canvasImage.Source is BitmapSource { PixelWidth: 48, PixelHeight: 40 });
            Check("размер в статус-баре", HasText(win, "48 × 40"));
            vm.UndoCommand.Execute(null);
            Pump();
            Check("откат размера", canvasHost.Width == 32 && canvasImage.Source is BitmapSource { PixelWidth: 32 });
        });

        Guard("подсветка области под кистью", () =>
        {
            vm.Canvas.Tool = Tool.Pencil;
            vm.Canvas.BrushSize = 3;
            vm.Canvas.UpdatePosition(10, 10);
            Pump();
            var cursor = (System.Windows.Shapes.Path)win.FindName("BrushCursor");
            Check("под кистью подсвечена область", cursor.Visibility == Visibility.Visible && cursor.Data != null);
            Shot(win, "main-brush-cursor");
            vm.Canvas.ClearPosition();
            Pump();
            Check("курсор убран вне холста", cursor.Visibility == Visibility.Collapsed);
            vm.Canvas.BrushSize = 1;
        });

        Guard("координатная сетка выделения", () =>
        {
            vm.Canvas.Tool = Tool.Select;
            vm.Canvas.SelectAllCommand.Execute(null);
            vm.Canvas.ShowSelectionGrid = true;
            Pump();
            var grid = (Canvas)win.FindName("SelGrid");
            Check("сетка выделения нарисована", grid.Visibility == Visibility.Visible && grid.Children.Count > 40);
            Shot(win, "main-selection-grid");
            vm.Canvas.ShowSelectionGrid = false;
            Pump();
            Check("сетка выделения скрыта", grid.Visibility == Visibility.Collapsed);
            vm.Canvas.DeselectCommand.Execute(null);
            vm.Canvas.Tool = Tool.Pencil;
        });

        Guard("пиксельная сетка: View → Pixel grid включает и выключает наложение", () =>
        {
            var gridRect = (System.Windows.Shapes.Rectangle)win.FindName("GridRect");
            var selGrid = (Canvas)win.FindName("SelGrid");
            MenuItem? item = null;
            void Find(DependencyObject d)
            {
                if (d is MenuItem m && m.GetBindingExpression(MenuItem.IsCheckedProperty)?.ParentBinding.Path.Path == "Canvas.ShowPixelGrid") item = m;
                foreach (var ch in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>()) Find(ch);
            }
            Find(win);
            Check("пункт меню «Пиксельная сетка» найден", item != null);
            vm.Canvas.SetZoom(9);   // the grid is only drawn from 600%
            vm.Canvas.ShowPixelGrid = true;
            Pump();
            Check("сетка включена: наложение видно на 800%", gridRect.Visibility == Visibility.Visible, $"zoom={vm.Canvas.Zoom} vis={gridRect.Visibility}");
            Shot(win, "main-pixel-grid-on");
            // the user unticks the item in the menu (a click toggles IsChecked of a checkable item, the binding writes it to the model)
            if (item != null) item.IsChecked = !item.IsChecked;
            Pump();
            Check("после снятия галочки модель выключила сетку", !vm.Canvas.ShowPixelGrid, $"item.IsChecked={item?.IsChecked}");
            Check("после снятия галочки наложение скрыто", gridRect.Visibility == Visibility.Collapsed, $"vis={gridRect.Visibility}");
            Check("координатная сетка выделения не рисуется вместо неё", selGrid.Visibility == Visibility.Collapsed);
            Shot(win, "main-pixel-grid-off");
            if (item != null) item.IsChecked = !item.IsChecked;
            Pump();
            Check("повторный клик включает сетку обратно", vm.Canvas.ShowPixelGrid && gridRect.Visibility == Visibility.Visible);
            vm.Canvas.ShowPixelGrid = false;
            Pump();
            vm.Canvas.SetZoom(10); vm.Canvas.SetZoom(9);
            Pump();
            Check("сетка остаётся скрытой после смены масштаба и после перерисовки", gridRect.Visibility == Visibility.Collapsed);
            vm.Session.RaiseOverlaysChanged();
            Pump();
            Check("и после RaiseOverlaysChanged", gridRect.Visibility == Visibility.Collapsed);
            vm.Canvas.ShowPixelGrid = true;
            Pump();
        });

        Guard("новые инструменты: параметры и выделение формы", () =>
        {
            foreach (var t in new[] { Tool.Wand, Tool.Lasso, Tool.Polygon, Tool.Gradient, Tool.Shade, Tool.Text, Tool.Move, Tool.Select, Tool.Pencil })
            {
                vm.Canvas.Tool = t;
                Pump();
            }
            vm.Canvas.Tool = Tool.Wand;
            vm.Canvas.PointerDown(5, 5, false, false);
            vm.Canvas.PointerUp();
            Pump();
            var maskImage = (Image)win.FindName("SelMaskImage");
            Check("палочка: форма выделения показана", maskImage.Visibility == Visibility.Visible && vm.Canvas.SelectionShape != null);
            Shot(win, "main-wand");
            vm.Canvas.Tool = Tool.Gradient;
            Pump();
            Shot(win, "main-gradient-options");
            vm.Canvas.Tool = Tool.Shade;
            Pump();
            Shot(win, "main-shade-options");
            vm.Canvas.DeselectCommand.Execute(null);
            vm.Canvas.Tool = Tool.Pencil;
            Pump();
            Check("форма скрыта после снятия", maskImage.Visibility == Visibility.Collapsed);
        });

        Guard("RSI: зоны направлений и кадров на листе", () =>
        {
            var rsiDoc = new Document(96, 128);
            rsiDoc.SheetCellWidth = rsiDoc.SheetCellHeight = 32;
            rsiDoc.Rsi = new RsiBinding("x.rsi", "walk", 4, 3, 32, 32, RsiState.UniformDelays(4, 3, 0.1));
            vm.Session.SetDocument(rsiDoc, null);
            Pump();
            var zones = (Canvas)win.FindName("RsiZones");
            Check("зоны RSI нарисованы (12 зон и 12 подписей)", zones.Visibility == Visibility.Visible && zones.Children.Count == 24);
            Check("строка состояния RSI", HasText(win, vm.Rsi.StatusLine));
            Shot(win, "main-rsi-zones");
            vm.Session.SetDocument(new Document(32, 32), null);
            Pump();
            Check("зоны убраны у обычного документа", zones.Visibility == Visibility.Collapsed);
        });

        Guard("сетка листа: наложение и подпись ячейки", () =>
        {
            vm.Session.Doc.SheetCellWidth = vm.Session.Doc.SheetCellHeight = 16;
            vm.Session.RaiseOverlaysChanged();
            vm.Canvas.UpdatePosition(20, 3);
            Pump();
            Check("сетка видна", sheetRect.Visibility == Visibility.Visible);
            Check("подпись показывает ячейку под курсором", HasText(win, "32 × 32 px · Cell 2, 1"), vm.Canvas.WorkspaceLabel);
            vm.Session.Doc.SheetCellWidth = vm.Session.Doc.SheetCellHeight = 0;
            vm.Session.RaiseOverlaysChanged();
            Pump();
            Check("сетка скрыта", sheetRect.Visibility == Visibility.Collapsed);
        });

        Guard("слои и статистика обновляются через привязки", () =>
        {
            vm.Layers.AddCommand.Execute(null);
            vm.Colors.RefreshStats();
            Pump();
            Check("слоёв в модели 2", vm.Layers.Items.Count == 2);
            Check("выбран верхний", vm.Layers.SelectedItem?.Layer == vm.Session.ActiveLayer);
            Check("заголовок статистики", vm.Colors.StatsHeader.StartsWith("Используемые цвета"));
            Check("панель слоёв показывает команды видимости", HasText(win, "Только активный") && HasText(win, "Показать все"));
        });

        Guard("рисунок для снимков", () =>
        {
            vm.Canvas.Tool = Tool.Rect;
            vm.Canvas.FilledShapes = true;
            vm.Colors.SetPrimary(Pixel.Rgb(0xFF, 0x77, 0xA8));
            vm.Canvas.PointerDown(4, 4, false, false); vm.Canvas.PointerMove(20, 18); vm.Canvas.PointerUp();
            vm.Colors.SetPrimary(Pixel.Rgb(0x00, 0x78, 0xD4));
            vm.Canvas.Tool = Tool.Ellipse;
            vm.Canvas.PointerDown(10, 8, false, false); vm.Canvas.PointerMove(28, 28); vm.Canvas.PointerUp();
            vm.Canvas.Tool = Tool.Select;
            vm.Canvas.PointerDown(2, 2, false, false); vm.Canvas.PointerMove(12, 10); vm.Canvas.PointerUp();
            vm.Session.Doc.SheetCellWidth = vm.Session.Doc.SheetCellHeight = 16;
            vm.Session.RaiseOverlaysChanged();
            vm.Colors.RefreshStats();
            Pump();
            Shot(win, "main-dark");
            Check("снимок тёмной темы", _shotsDir == null || File.Exists(Path.Combine(_shotsDir, "main-dark.png")));
        });

        Guard("тема: палитра подменяется на лету", () =>
        {
            vm.SetThemeCommand.Execute("Light");
            Pump();
            Check("светлая палитра: Surface #F3F3F3", BrushColor("Brush.Surface") == Color.FromRgb(0xF3, 0xF3, 0xF3));
            Check("светлая палитра: Accent #0078D4", BrushColor("Brush.Accent") == Color.FromRgb(0x00, 0x78, 0xD4));
            Check("открытое окно перекрасилось", ((SolidColorBrush)win.Background).Color == Color.FromRgb(0xF3, 0xF3, 0xF3));
            Shot(win, "main-light");
            vm.SetThemeCommand.Execute("Dark");
            Pump();
            Check("возврат к тёмной", BrushColor("Brush.Surface") == Color.FromRgb(0x20, 0x20, 0x20));
            Check("флаги темы в модели", vm.IsThemeDark && !vm.IsThemeLight);
        });

        // dialogs: each opens on its own model through DialogService, takes a shot and closes with cancel
        var services = vm.Session.Services;
        var dialogs = new DialogService(win);
        if (Environment.GetEnvironmentVariable("PIXED_SMOKE_LANG") == "en") PixelEditor.ViewModels.Loc.Instance.SetLanguage("en");

        void ModalDialog(string name, DialogViewModel dialog, Action<Window>? inspect = null)
        {
            Guard($"диалог: {name}", () =>
            {
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                {
                    var w = Application.Current.Windows.OfType<Window>().First(x => ReferenceEquals(x.DataContext, dialog));
                    w.UpdateLayout();
                    if (Environment.GetEnvironmentVariable("PIXED_SMOKE_LANG") == "en")
                        Check($"{name}: английский, нет русских подписей", FindCyrillic(w).Count == 0, string.Join(" | ", FindCyrillic(w).Distinct().Take(6)));
                    inspect?.Invoke(w);
                    Shot(w, "dialog-" + name);
                    if (_shotsDir != null)
                    {
                        // the same dialog in the light theme (the palette swaps live)
                        vm.SetThemeCommand.Execute("Light");
                        w.UpdateLayout();
                        Shot(w, "dialog-" + name + "-light");
                        vm.SetThemeCommand.Execute("Dark");
                        w.UpdateLayout();
                    }
                    dialog.CancelCommand.Execute(null);
                }));
                var result = dialogs.ShowDialog(dialog);
                Check($"{name}: открылся и закрылся отменой", result == false, result?.ToString());
            });
        }

        var photo = new PixelImage(120, 90);
        for (int y = 0; y < 90; y++)
            for (int x = 0; x < 120; x++)
                photo.Set(x, y, (x - 60) * (x - 60) + (y - 45) * (y - 45) < 1600
                    ? Pixel.Rgb((byte)(x * 2), (byte)(y * 2), (byte)(255 - x)) : Pixel.Empty);

        ModalDialog("prompt", new PromptViewModel("Сетка листа", "Размер ячейки: ширина×высота, например 32x32.", "32x32"));
        ModalDialog("new-document", new NewDocumentViewModel(32, 32), w =>
        {
            Check("у диалога собственный заголовок", HasText(w, "Новый документ"));
            Check("у диалога видна только кнопка закрытия", CaptionButton(w, "Close")!.IsVisible && !CaptionButton(w, "Minimize")!.IsVisible && !CaptionButton(w, "Maximize")!.IsVisible);
        });
        ModalDialog("start-screen", new StartScreenViewModel(vm), w =>
        {
            Check("start screen: create, open and recent columns", HasText(w, "Создать") && HasText(w, "Открыть") && HasText(w, "Недавние"));
            Check("start screen: size buttons and the startup checkbox", HasText(w, "32×32") && FindVisual<CheckBox>(w, c => c.IsChecked == false) != null);
        });
        vm.SetThemeCommand.Execute("Light");
        Pump();
        ModalDialog("start-screen-light", new StartScreenViewModel(vm));
        vm.SetThemeCommand.Execute("Dark");
        Pump();
        ModalDialog("image-size", new ImageSizeViewModel(48, 32), w => Check("размер изображения: подсказка о способе", HasText(w, "Сейчас 48×32 px")));
        var rsiDir = Path.Combine(Path.GetTempPath(), "pixed-smoke-rsi");
        ModalDialog("rsi-project", new RsiProjectViewModel(services.Dialogs, true, rsiDir, "mob", "CC-BY-SA-3.0", "", 32, 32), w => Check("RSI: поля лицензии и размера кадра", HasText(w, "Лицензия (необязательно)") && HasText(w, "Ширина кадра, px")));
        ModalDialog("rsi-state", new RsiStateViewModel(32, 32, []), w => Check("RSI: подсказка о размере листа", HasText(w, "Лист 32×128 px: строки S, N, E, W; кадров в строке 1. Каждая зона 32×32.")));
        ModalDialog("animation-sheet", new RsiStateViewModel(32, 32, [], allowCustomRows: true, askSize: true, title: "Новый лист анимации", templates: vm.Settings.SheetTemplates, dialogs: services.Dialogs), w => Check("лист анимации: поле названий строк", HasText(w, "Новый лист анимации")));
        ModalDialog("rsi-open", new RsiOpenViewModel(services.Dialogs));
        ModalDialog("animation-export", new AnimationExportViewModel(services.Dialogs, rsiDir, "walk", "Лист 96×128: 4 напр. × 3 кадр. по 32×32."), w => Check("экспорт: форматы Unity и Godot", HasText(w, "Godot 4: ресурс SpriteFrames (.tres)")));
        ModalDialog("canvas-size", new CanvasSizeViewModel(32, 32) { Anchor = AnchorPosition.Center });
        ModalDialog("import", new ImportViewModel(services.Dialogs, services.Timers, photo, "photo.png", vm.Colors.Palette.ToList(), canAddLayer: true),
            w => Check("импорт: превью построено и показано", HasText(w, ((ImportViewModel)w.DataContext).ResultInfo)));
        // notifications in the shared style (instead of system MessageBox)
        ModalDialog("message-info", MessageViewModel.Info("GIF", "Сохранено кадров: 4, задержка 100 мс."),
            w => Check("уведомление: текст и кнопка OK на экране", HasText(w, "Сохранено кадров: 4, задержка 100 мс.") && HasText(w, "OK")));
        ModalDialog("message-warning", MessageViewModel.Warning("Пайплайн остановлен", "шаг 2 (resize): ширина должна быть больше нуля"));
        ModalDialog("message-error", MessageViewModel.Error("Не удалось открыть файл.", "System.IO.IOException: файл занят другим процессом"),
            w => Check("ошибка: подробности показаны отдельным полем", ((MessageViewModel)w.DataContext).HasDetails && HasText(w, "Не удалось открыть файл.")));
        ModalDialog("message-question", MessageViewModel.Confirm("Pixel Editor", "Найдено автосохранение. Восстановить?"),
            w => Check("вопрос: две кнопки", HasText(w, "Да") && HasText(w, "Нет")));
        ModalDialog("message-destructive", MessageViewModel.ConfirmDestructive("Удалить палитру", "Удалить палитру «Мои цвета»?"),
            w => Check("удаление: кнопки «Удалить» и «Отмена»", HasText(w, "Удалить") && HasText(w, "Отмена")));
        ModalDialog("message-save", MessageViewModel.SaveChanges(),
            w => Check("сохранение: три кнопки", HasText(w, "Сохранить") && HasText(w, "Не сохранять") && HasText(w, "Отмена")));

        // the palette and template libraries
        var sample = BuiltInPalettes.All[1];
        ModalDialog("name-description", new NameDescriptionViewModel("Сохранить палитру в библиотеку", "Мои цвета", "для интерфейса",
            colors: sample.Colors, hint: "Палитра будет доступна в любом проекте."),
            w => Check("название и описание: количество цветов", HasText(w, $"Цветов: {sample.Colors.Count}")));
        ModalDialog("palette-library", new PaletteLibraryViewModel(vm.Colors, vm.Settings.Palettes, services.Dialogs, services.Shell),
            w => Check("библиотека палитр: системные палитры в списке", HasText(w, "PICO-8") && HasText(w, "Game Boy")));
        Guard("предпросмотр в реальном размере", () =>
        {
            vm.OpenPreviewCommand.Execute(null);
            Pump();
            var w = Application.Current.Windows.OfType<Window>().First(x => x.DataContext is PreviewViewModel);
            w.UpdateLayout();
            Shot(w, "dialog-real-preview");
            Check("предпросмотр: подпись с размером", HasText(w, "32×32 px, масштаб 1×"));
            ((PreviewViewModel)w.DataContext).CancelCommand.Execute(null);
            Pump();
        });
        ModalDialog("template-library", new TemplateLibraryViewModel(vm.Session, vm.Automation),
            w => Check("менеджер шаблонов: системные шаблоны в списке", HasText(w, BuiltInTemplates.All[0].Name)));
        ModalDialog("pipeline-editor", vm.Automation.CreateEditor(BuiltInTemplates.All[0], asCopy: true),
            w => Check("конструктор: шаги и JSON показаны", HasText(w, "alphaThreshold") && HasText(w, "Сохранить в библиотеку")));
        ModalDialog("result-preview", new ResultPreviewViewModel("Фото в спрайт 32×32", photo, Pipeline.Parse(BuiltInTemplates.All[0].Json).Run(photo)),
            w => Check("предпросмотр результата: кнопка применения", HasText(w, "Применить к слою") || HasText(w, "Открыть в новом документе")));
        ModalDialog("pipeline-editor-new", vm.Automation.CreateEditor(null, asCopy: false));

        ModalDialog("batch", new BatchViewModel(services, vm.Settings.Templates));
        ModalDialog("reference", new ReferenceViewModel(services.Shell, vm.Settings.Templates.UserDirectory));
        var chatDialog = new ChatPipelineViewModel(services.Clipboard, () => "prompt", () => photo, null, services.Timers);
        chatDialog.Text = "Here you go:\n```json\n{\"steps\":[{\"op\":\"resise\",\"width\":8},{\"op\":\"trim\"}]}\n```";   // with an error: the "did you mean" hint is visible
        ModalDialog("chat-pipeline", chatDialog, w => Check("чат: найдена ошибка в имени операции", !chatDialog.IsValid && chatDialog.Messages.Count > 0));
        var chatOk = new ChatPipelineViewModel(services.Clipboard, () => "prompt", () => photo, null, services.Timers);
        chatOk.Text = "{\"name\":\"small\",\"steps\":[{\"op\":\"resize\",\"width\":16}]}";
        chatOk.AddVariantCommand.Execute(null);
        chatOk.Text = "{\"name\":\"tiny\",\"steps\":[{\"op\":\"resize\",\"width\":8}]}";
        chatOk.RunPreview();
        var chatSheet = new ChatPipelineViewModel(services.Clipboard, k => "prompt", () => photo, null, services.Timers);
        chatSheet.PasteText(ChatScenePrompt.Walker);
        ModalDialog("chat-sheet", chatSheet, w => Check("chat: the sheet drawn from primitives is previewed", chatSheet.Kind == ChatKind.Sheet && chatSheet.After != null && chatSheet.Variants.Count == 1));
        // the sheet kind with the layout selector (an attack template, 16x16, a delay override) and an answer that does not match it
        var chatLayout = new ChatPipelineViewModel(services.Clipboard, (k, l) => "prompt", () => photo, null, services.Timers,
            new ChatSheetSetup(vm.Settings.SheetTemplates, null, "system:attack-4", 16, 16, 0.2));
        chatLayout.PromptKind = ChatKind.Sheet;
        chatLayout.PasteText(ChatScenePrompt.Walker);
        ModalDialog("chat-sheet-layout", chatLayout, w => Check("chat: the layout selector is shown and the mismatch is reported",
            chatLayout.ShowLayoutSelector && chatLayout.CanApply && chatLayout.Messages.Any(m => m.StartsWith("⚠"))));
        var docLayout = new SheetLayoutSpec(16, 16, ["idle", "attack"], [new SheetFrame("a", 0.5), new SheetFrame("b", 0.25)]);
        var chatDoc = new ChatPipelineViewModel(services.Clipboard, (k, l) => "prompt", () => photo, null, services.Timers, new ChatSheetSetup(vm.Settings.SheetTemplates, docLayout));
        chatDoc.PromptKind = ChatKind.Sheet;
        ModalDialog("chat-sheet-document", chatDoc, w => Check("chat: the current sheet's layout is shown instead of the selector", chatDoc.ShowDocumentLayout && !chatDoc.ShowLayoutSelector));
        var chatSprite = new ChatPipelineViewModel(services.Clipboard, k => "prompt", () => photo, null, services.Timers);
        chatSprite.PasteText(ChatScenePrompt.Person);
        ModalDialog("chat-sprite", chatSprite, w => Check("chat: the sprite drawn from primitives is previewed", chatSprite.Kind == ChatKind.Sprite && !chatSprite.ShowBefore));
        var tuner = new OperationTunerViewModel(() => photo, "removeBackground", "{\"follow\":8}", services.Timers);
        ModalDialog("operation-tuner", tuner, w => Check("подбор параметров: поля и предпросмотр построены", tuner.Parameters.Count > 3 && tuner.After != null && HasText(w, "follow")));
        ModalDialog("chat-pipeline-ok", chatOk, w =>
        {
            Check("чат: предпросмотр и вариант построены", chatOk.HasPreview && chatOk.Variants.Count == 1);
            // open the steps expander so its chevron and body are captured in the shots
            var exp = FindVisual<Expander>(w, _ => true);
            Check("чат: раскрывающийся блок найден", exp != null);
            if (exp != null) { exp.IsExpanded = true; w.UpdateLayout(); }
            Check("неявные стили RadioButton и Expander подключены", Application.Current.TryFindResource(typeof(RadioButton)) is Style && Application.Current.TryFindResource(typeof(Expander)) is Style);
        });

        Guard("диалог: анимация (немодальное окно)", () =>
        {
            var frames = new List<PixelImage> { photo, photo, photo };
            var anim = new AnimationViewModel(services.Dialogs, services.Timers, frames, "Кадры: слои снизу вверх");
            dialogs.Show(anim);
            Pump();
            var w = Application.Current.Windows.OfType<Window>().First(x => ReferenceEquals(x.DataContext, anim));
            w.UpdateLayout();
            Shot(w, "dialog-animation");
            Check("анимация: подпись кадра на экране", HasText(w, anim.FrameLabel));
            anim.CancelCommand.Execute(null);
            Pump();
            Check("анимация: окно закрыто моделью", !w.IsVisible);
        });

        // The clipboard is shared by the whole system: the test overwrites it, so it runs only with PIXED_SMOKE_CLIPBOARD=1
        if (Environment.GetEnvironmentVariable("PIXED_SMOKE_CLIPBOARD") == "1")
        {
            Guard("буфер обмена: PNG, файлы, прозрачность", () =>
            {
                var service = new ClipboardService();
                var img = new PixelImage(3, 2, [Pixel.Rgb(255, 0, 0), 0, Pixel.Rgb(0, 0, 255), 0, Pixel.Rgb(0, 255, 0), Pixel.Rgb(1, 2, 3)]);

                static void Retry(Action a)
                {
                    for (int i = 0; ; i++)
                    {
                        try { a(); return; }
                        catch (System.Runtime.InteropServices.COMException) when (i < 8) { Thread.Sleep(120); }
                    }
                }

                // 1. PNG only, as browsers put it: transparency is kept
                var pngOnly = new DataObject();
                pngOnly.SetData("PNG", new MemoryStream(PixelEditor.Imaging.ImageIO.EncodePng(img)));
                Retry(() => Clipboard.SetDataObject(pngOnly, true));
                var fromPng = service.GetImage();
                Check("PNG из буфера читается", fromPng != null && fromPng.Pixels.SequenceEqual(img.Pixels));

                // 2. a picture file copied in Explorer
                var file = Path.Combine(Path.GetTempPath(), "pixed-clip-" + Guid.NewGuid().ToString("N") + ".png");
                try
                {
                    PixelEditor.Imaging.ImageIO.SavePng(img, file);
                    var files = new DataObject();
                    files.SetFileDropList([file]);
                    Retry(() => Clipboard.SetDataObject(files, true));
                    var fromFile = service.GetImage();
                    Check("файл картинки из буфера читается", fromFile != null && fromFile.Pixels.SequenceEqual(img.Pixels));
                }
                finally { try { File.Delete(file); } catch { } }

                // 3. our own copy: there and back without losing transparency
                bool set = false;
                Retry(() => set = service.TrySetImage(img));
                Check("копирование в буфер удалось", set);
                var roundTrip = service.GetImage();
                Check("копия возвращается с прозрачностью", roundTrip != null && roundTrip.Pixels.SequenceEqual(img.Pixels));
                Check("порядковый номер буфера меняется", service.SequenceNumber != 0);

                // 4. an empty clipboard gives no picture
                Retry(() => Clipboard.Clear());
                Check("пустой буфер: картинки нет", service.GetImage() == null);
            });
        }

        Guard("смена языка на лету", () =>
        {
            var loc = PixelEditor.ViewModels.Loc.Instance;
            Check("по умолчанию подписи русские", HasText(win, "Слои"));
            loc.SetLanguage("en");
            try { Check("после смены на английский подпись панели обновилась", HasText(win, "Layers")); }
            finally
            {
                // in the English mode no Russian letters should remain in the window: captions, menus, tooltips
                loc.SetLanguage("en");
                vm.Session.Status("Ready");   // an old status bar message is not recreated when the language changes
                Pump();
                var found = FindCyrillic(win);
                Check("английский режим: в главном окне нет русских подписей", found.Count == 0, string.Join(" | ", found.Distinct().Take(8)));
                loc.SetLanguage("ru");
            }
            Check("обратно на русский без перезапуска", HasText(win, "Слои"));
        });

        Guard("ошибки привязок отсутствуют", () =>
        {
            Check("нет сообщений об ошибках привязок", Binding.Errors.Count == 0, string.Join(" | ", Binding.Errors.Take(5)));
        });

        Guard("закрытие чистого документа не спрашивает", () =>
        {
            vm.Session.Dirty = false;
            win.Close();
            Check("курсор вращения собран (изогнутая стрелка)", PixelEditor.App.RotateCursorFactory.Succeeded);
            Check("окно закрыто", !win.IsVisible);
        });

        app.Shutdown();
    }
}
