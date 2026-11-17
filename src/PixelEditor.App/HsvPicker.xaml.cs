// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PixelEditor.Core;

namespace PixelEditor.App;

/// <summary>
/// Saturation/value square with a hue strip. Display and pointer input only: the three bindable values
/// (Hue 0..360, Saturation and Value 0..1) are owned by the view model.
/// </summary>
public partial class HsvPicker : UserControl
{
    public static readonly DependencyProperty HueProperty = Reg(nameof(Hue));
    public static readonly DependencyProperty SaturationProperty = Reg(nameof(Saturation));
    public static readonly DependencyProperty ValueProperty = Reg(nameof(Value));

    private static DependencyProperty Reg(string name) =>
        DependencyProperty.Register(name, typeof(double), typeof(HsvPicker),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((HsvPicker)d).UpdateVisuals()));

    public HsvPicker()
    {
        InitializeComponent();
        Square.SizeChanged += (_, _) => UpdateVisuals();
        Strip.SizeChanged += (_, _) => UpdateVisuals();
    }

    public double Hue { get => (double)GetValue(HueProperty); set => SetValue(HueProperty, value); }
    public double Saturation { get => (double)GetValue(SaturationProperty); set => SetValue(SaturationProperty, value); }
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    private void UpdateVisuals()
    {
        if (HueFill == null) return;
        var rgb = Hsv.ToRgb(Hue, 1, 1);
        HueFill.Fill = new SolidColorBrush(Color.FromRgb(Pixel.R(rgb), Pixel.G(rgb), Pixel.B(rgb)));

        double w = Square.ActualWidth, h = Square.ActualHeight;
        Canvas.SetLeft(SquareMark, Saturation * w - SquareMark.Width / 2);
        Canvas.SetTop(SquareMark, (1 - Value) * h - SquareMark.Height / 2);
        Canvas.SetLeft(SquareMarkOuter, Saturation * w - SquareMarkOuter.Width / 2);
        Canvas.SetTop(SquareMarkOuter, (1 - Value) * h - SquareMarkOuter.Height / 2);

        Canvas.SetLeft(StripMark, Math.Clamp(Hue / 360, 0, 1) * (Strip.ActualWidth - StripMark.Width));
    }

    private void Square_Down(object sender, MouseButtonEventArgs e)
    {
        Square.CaptureMouse();
        SetFromSquare(e.GetPosition(Square));
        e.Handled = true;
    }

    private void Square_Move(object sender, MouseEventArgs e)
    {
        if (Square.IsMouseCaptured) SetFromSquare(e.GetPosition(Square));
    }

    private void Strip_Down(object sender, MouseButtonEventArgs e)
    {
        Strip.CaptureMouse();
        SetFromStrip(e.GetPosition(Strip));
        e.Handled = true;
    }

    private void Strip_Move(object sender, MouseEventArgs e)
    {
        if (Strip.IsMouseCaptured) SetFromStrip(e.GetPosition(Strip));
    }

    private void Drag_Up(object sender, MouseButtonEventArgs e) => ((UIElement)sender).ReleaseMouseCapture();

    private void SetFromSquare(Point p)
    {
        if (Square.ActualWidth < 1 || Square.ActualHeight < 1) return;
        // two property writes: the view model recomputes the color after each, the second one is the final value
        Saturation = Math.Clamp(p.X / Square.ActualWidth, 0, 1);
        Value = 1 - Math.Clamp(p.Y / Square.ActualHeight, 0, 1);
    }

    private void SetFromStrip(Point p)
    {
        if (Strip.ActualWidth < 1) return;
        // the end of the strip is the same red as the start: keep it just below 360 so it does not wrap to 0 visually
        Hue = Math.Clamp(p.X / Strip.ActualWidth, 0, 1) * 359.999;
    }
}
