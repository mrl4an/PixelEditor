// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
namespace PixelEditor.Core.Ops;

/// <summary>Rotation by an arbitrary angle keeping the pixels crisp.</summary>
public static class Rotation
{
    /// <summary>
    /// Rotates clockwise by degrees. smooth: crisp rotation (the picture is enlarged 4 times, rotated by the nearest pixel and
    /// reduced with the "most frequent color" method): edges stay even, there are no new colors, best suited for pixel art. Without smooth: rotation by the nearest pixel directly,
    /// fast but with coarser steps. expand: grow the canvas so the rotated picture fits entirely; otherwise the size stays and the excess is cropped.
    /// </summary>
    public static PixelImage Rotate(PixelImage src, double degrees, bool smooth = true, bool expand = false)
    {
        degrees = ((degrees % 360) + 360) % 360;
        if (degrees == 0) return src.Clone();
        if (degrees == 90 && !expand && src.Width == src.Height) return Transform.Rotate90(src, 1);
        if (smooth && src.Width * src.Height <= 512 * 512)
        {
            const int k = 4;
            var big = Scale(src, k);
            var turned = RotateNearest(big, degrees, expand);
            return Resampler.Resize(turned, expand ? Math.Max(1, (int)Math.Round(turned.Width / (double)k)) : src.Width,
                expand ? Math.Max(1, (int)Math.Round(turned.Height / (double)k)) : src.Height, ResizeMethod.Mode);
        }
        return RotateNearest(src, degrees, expand);
    }

    private static PixelImage Scale(PixelImage src, int k)
    {
        var d = new PixelImage(src.Width * k, src.Height * k);
        for (int y = 0; y < d.Height; y++)
            for (int x = 0; x < d.Width; x++)
                d.Pixels[y * d.Width + x] = src.Pixels[(y / k) * src.Width + x / k];
        return d;
    }

    private static PixelImage RotateNearest(PixelImage src, double degrees, bool expand)
    {
        double rad = degrees * Math.PI / 180.0, cos = Math.Cos(rad), sin = Math.Sin(rad);
        int w = src.Width, h = src.Height;
        int dw = w, dh = h;
        if (expand)
        {
            dw = (int)Math.Ceiling(Math.Abs(w * cos) + Math.Abs(h * sin) - 1e-9);
            dh = (int)Math.Ceiling(Math.Abs(w * sin) + Math.Abs(h * cos) - 1e-9);
        }
        var dst = new PixelImage(Math.Max(1, dw), Math.Max(1, dh));
        double scx = w / 2.0, scy = h / 2.0, dcx = dst.Width / 2.0, dcy = dst.Height / 2.0;
        for (int y = 0; y < dst.Height; y++)
            for (int x = 0; x < dst.Width; x++)
            {
                double px = x + 0.5 - dcx, py = y + 0.5 - dcy;
                // inverse rotation: where in the source to take this pixel from (clockwise in screen coordinates)
                double sx = px * cos + py * sin + scx, sy = -px * sin + py * cos + scy;
                int ix = (int)Math.Floor(sx), iy = (int)Math.Floor(sy);
                if (ix < 0 || iy < 0 || ix >= w || iy >= h) continue;
                dst.Pixels[y * dst.Width + x] = src.Pixels[iy * w + ix];
            }
        return dst;
    }
}
