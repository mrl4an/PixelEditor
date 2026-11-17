// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using PixelEditor.Core;
using PixelEditor.Imaging;

namespace PixelEditor.App;

/// <summary>uint color to a brush. A transparent pixel gives a transparent brush (the checkerboard shows through).</summary>
public sealed class UIntToBrushConverter : IValueConverter
{
    private static readonly Dictionary<uint, Brush> Cache = new();

    public static Brush For(uint c)
    {
        if (Pixel.IsEmpty(c)) return Brushes.Transparent;
        if (!Cache.TryGetValue(c, out var b))
        {
            b = new SolidColorBrush(Color.FromRgb(Pixel.R(c), Pixel.G(c), Pixel.B(c)));
            b.Freeze();
            Cache[c] = b;
        }
        return b;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is uint c ? For(c) : Brushes.Transparent;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Binds a RadioButton to an enum: the parameter is the value name.</summary>
public sealed class EnumBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value != null && parameter != null && value.ToString() == parameter.ToString();

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true && parameter != null
            ? (targetType.IsEnum ? Enum.Parse(targetType, parameter.ToString()!) : System.Convert.ChangeType(parameter, Nullable.GetUnderlyingType(targetType) ?? targetType, culture))
            : Binding.DoNothing;
}

public sealed class UIntToHexConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is uint c ? Pixel.ToHex(c) : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Model raster (PixelImage) to a WPF image for binding to Image.Source.</summary>
public sealed class PixelImageToBitmapConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is PixelImage img ? ImageIO.ToBitmap(img) : null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Tool name (Line, Rect, ...) to a vector Icon.* from the resource dictionary.</summary>
public sealed class ToolIconConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value != null && System.Windows.Application.Current.TryFindResource("Icon." + value) is System.Windows.Media.Geometry g ? g : null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
