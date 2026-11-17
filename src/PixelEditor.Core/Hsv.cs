// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
namespace PixelEditor.Core;

/// <summary>HSV (hue 0..360, saturation and value 0..1) to RGB and back. Colors are opaque 0xAARRGGBB pixels.</summary>
public static class Hsv
{
    /// <summary>Opaque pixel from HSV. Out-of-range input is wrapped (hue) or clamped (saturation, value).</summary>
    public static uint ToRgb(double h, double s, double v)
    {
        h %= 360;
        if (h < 0) h += 360;
        s = Math.Clamp(s, 0, 1);
        v = Math.Clamp(v, 0, 1);
        double c = v * s;
        double x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        double m = v - c;
        (double r, double g, double b) = (int)(h / 60) switch
        {
            0 => (c, x, 0.0),
            1 => (x, c, 0.0),
            2 => (0.0, c, x),
            3 => (0.0, x, c),
            4 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return Pixel.Rgb(To8(r + m), To8(g + m), To8(b + m));
    }

    /// <summary>HSV of a pixel (alpha is ignored). Greys give hue 0 and saturation 0; black gives value 0.</summary>
    public static (double H, double S, double V) FromRgb(uint p)
    {
        double r = Pixel.R(p) / 255.0, g = Pixel.G(p) / 255.0, b = Pixel.B(p) / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        if (d == 0) return (0, 0, max);
        double h = max == r ? (g - b) / d % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        h *= 60;
        if (h < 0) h += 360;
        return (h, d / max, max);
    }

    private static byte To8(double x) => (byte)Math.Clamp((int)Math.Round(x * 255), 0, 255);
}
