// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
namespace PixelEditor.Core;

public enum GradientMode
{
    /// <summary>Smooth transition (many intermediate colors).</summary>
    Smooth,
    /// <summary>Steps: the transition is split into several even bands.</summary>
    Steps,
    /// <summary>Dithering: only the two source colors, a Bayer checkerboard pattern. Creates no new colors, a pixel-art classic.</summary>
    Dither,
}

/// <summary>A gradient between two colors along a segment and a color shift (brightness, hue).</summary>
public static class Gradient
{
    private static readonly int[,] Bayer4 =
    {
        { 0, 8, 2, 10 },
        { 12, 4, 14, 6 },
        { 3, 11, 1, 9 },
        { 15, 7, 13, 5 },
    };

    /// <summary>
    /// Fills an area with a gradient (rect or mask, otherwise the whole layer): from color c0 at point (x0, y0) to c1 at point (x1, y1).
    /// If one of the colors is transparent, dithering is always used (this gives a "dissolve" into transparency).
    /// </summary>
    public static void Fill(PixelBatch batch, PixelRect? rect, SelectionMask? mask, int x0, int y0, int x1, int y1,
        uint c0, uint c1, GradientMode mode, int steps = 4)
    {
        var layer = batch.Layer;
        var area = mask?.Bounds ?? rect ?? new PixelRect(0, 0, layer.Width, layer.Height);
        area = area.ClampTo(layer.Width, layer.Height);
        if (Pixel.IsEmpty(c0) || Pixel.IsEmpty(c1)) mode = GradientMode.Dither;
        steps = Math.Clamp(steps, 2, 16);

        double dx = x1 - x0, dy = y1 - y0, len2 = dx * dx + dy * dy;
        for (int y = area.Y; y < area.Y + area.Height; y++)
            for (int x = area.X; x < area.X + area.Width; x++)
            {
                if (mask != null && !mask.Contains(x, y)) continue;
                double t = len2 == 0 ? 0 : Math.Clamp(((x - x0) * dx + (y - y0) * dy) / len2, 0, 1);
                uint color;
                switch (mode)
                {
                    case GradientMode.Dither:
                        color = t > (Bayer4[y & 3, x & 3] + 0.5) / 16 ? c1 : c0;
                        break;
                    case GradientMode.Steps:
                        color = Lerp(c0, c1, Math.Min(steps - 1, Math.Floor(t * steps)) / (steps - 1));
                        break;
                    default:
                        color = Lerp(c0, c1, t);
                        break;
                }
                batch.Set(x, y, color);
            }
    }

    public static uint Lerp(uint a, uint b, double t) =>
        Pixel.Rgb((byte)Math.Round(Pixel.R(a) + (Pixel.R(b) - Pixel.R(a)) * t),
                  (byte)Math.Round(Pixel.G(a) + (Pixel.G(b) - Pixel.G(a)) * t),
                  (byte)Math.Round(Pixel.B(a) + (Pixel.B(b) - Pixel.B(a)) * t));

    // ---- color shift ----

    /// <summary>Lightens (amount &gt; 0) or darkens (&lt; 0): amount in brightness fractions, from -1 to 1. A transparent pixel does not change.</summary>
    public static uint Brightness(uint c, double amount)
    {
        if (Pixel.IsEmpty(c)) return c;
        var (h, s, v) = ToHsv(c);
        // dark colors are lightened toward lower saturation so gray shades do not appear; light colors darken by brightness
        v = Math.Clamp(v + amount, 0, 1);
        return FromHsv(h, s, v);
    }

    /// <summary>Rotates the hue by degrees. Gray and transparent pixels do not change.</summary>
    public static uint Hue(uint c, double degrees)
    {
        if (Pixel.IsEmpty(c)) return c;
        var (h, s, v) = ToHsv(c);
        h = ((h + degrees) % 360 + 360) % 360;
        return FromHsv(h, s, v);
    }

    public static (double H, double S, double V) ToHsv(uint c)
    {
        double r = Pixel.R(c) / 255.0, g = Pixel.G(c) / 255.0, b = Pixel.B(c) / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        double h = 0;
        if (d > 0)
        {
            if (max == r) h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * ((b - r) / d + 2);
            else h = 60 * ((r - g) / d + 4);
            if (h < 0) h += 360;
        }
        return (h, max == 0 ? 0 : d / max, max);
    }

    public static uint FromHsv(double h, double s, double v)
    {
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        (double r, double g, double b) = (int)(h / 60) switch
        {
            0 => (c, x, 0.0), 1 => (x, c, 0.0), 2 => (0.0, c, x), 3 => (0.0, x, c), 4 => (x, 0.0, c), _ => (c, 0.0, x),
        };
        return Pixel.Rgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }
}
