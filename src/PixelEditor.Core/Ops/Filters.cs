// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
namespace PixelEditor.Core.Ops;

/// <summary>
/// Photo filters in the spirit of Pillow (ImageFilter.MedianFilter, UnsharpMask, FIND_EDGES): they prepare a photo before downscaling and
/// quantizing, which is what makes the pixel art result clean. Transparent pixels stay transparent and never take part as neighbors.
/// </summary>
public static class Filters
{
    /// <summary>
    /// Median filter: every channel of a pixel becomes the median of the (2r+1)×(2r+1) window of opaque pixels. Removes JPEG noise and film grain
    /// but keeps sharp edges, unlike a blur. radius 1..4.
    /// </summary>
    public static PixelImage Median(PixelImage src, int radius)
    {
        radius = Math.Clamp(radius, 1, 4);
        int w = src.Width, h = src.Height;
        var dst = src.Clone();
        var r = new List<byte>(); var g = new List<byte>(); var b = new List<byte>();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                uint p = src.Pixels[y * w + x];
                if (Pixel.IsEmpty(p)) continue;
                r.Clear(); g.Clear(); b.Clear();
                for (int dy = -radius; dy <= radius; dy++)
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        uint q = src.Pixels[ny * w + nx];
                        if (Pixel.IsEmpty(q)) continue;
                        r.Add(Pixel.R(q)); g.Add(Pixel.G(q)); b.Add(Pixel.B(q));
                    }
                dst.Pixels[y * w + x] = Pixel.Rgb(Median(r), Median(g), Median(b));
            }
        return dst;
    }

    private static byte Median(List<byte> v)
    {
        v.Sort();
        return v[v.Count / 2];
    }

    /// <summary>Unsharp mask: image + amount × (image - blurred). Sharpens edges after a downscale so thin lines survive quantizing. radius 1..4, amount 0..3.</summary>
    public static PixelImage UnsharpMask(PixelImage src, int radius, double amount)
    {
        radius = Math.Clamp(radius, 1, 4);
        amount = Math.Clamp(amount, 0, 3);
        int w = src.Width, h = src.Height;
        var dst = src.Clone();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                uint p = src.Pixels[y * w + x];
                if (Pixel.IsEmpty(p)) continue;
                double sr = 0, sg = 0, sb = 0; int n = 0;
                for (int dy = -radius; dy <= radius; dy++)
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        uint q = src.Pixels[ny * w + nx];
                        if (Pixel.IsEmpty(q)) continue;
                        sr += Pixel.R(q); sg += Pixel.G(q); sb += Pixel.B(q); n++;
                    }
                byte Sharp(int c, double mean) => (byte)Math.Clamp(Math.Round(c + amount * (c - mean)), 0, 255);
                dst.Pixels[y * w + x] = Pixel.Rgb(Sharp(Pixel.R(p), sr / n), Sharp(Pixel.G(p), sg / n), Sharp(Pixel.B(p), sb / n));
            }
        return dst;
    }

    /// <summary>
    /// Edge lines (Sobel on brightness, like Pillow's FIND_EDGES): pixels where the brightness changes sharply become a line of the given color.
    /// threshold 0..255 (smaller: more lines). thin: only the strongest pixels across the edge are kept, so lines are one pixel wide.
    /// mode overlay: the lines are drawn over the picture, only: the lines on a transparent background (a line-art outline of the photo).
    /// </summary>
    public static PixelImage EdgeLines(PixelImage src, int threshold, uint color, bool thin, bool onlyLines)
    {
        int w = src.Width, h = src.Height;
        var luma = new double[w * h];
        for (int i = 0; i < luma.Length; i++)
        {
            uint p = src.Pixels[i];
            luma[i] = Pixel.IsEmpty(p) ? -1 : 0.299 * Pixel.R(p) + 0.587 * Pixel.G(p) + 0.114 * Pixel.B(p);
        }
        double L(int x, int y, double fallback)
        {
            double v = luma[Math.Clamp(y, 0, h - 1) * w + Math.Clamp(x, 0, w - 1)];
            return v < 0 ? fallback : v;
        }
        var mag = new double[w * h];
        var gx = new double[w * h];
        var gy = new double[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double c = luma[y * w + x];
                if (c < 0) continue;
                double sx = -L(x - 1, y - 1, c) - 2 * L(x - 1, y, c) - L(x - 1, y + 1, c) + L(x + 1, y - 1, c) + 2 * L(x + 1, y, c) + L(x + 1, y + 1, c);
                double sy = -L(x - 1, y - 1, c) - 2 * L(x, y - 1, c) - L(x + 1, y - 1, c) + L(x - 1, y + 1, c) + 2 * L(x, y + 1, c) + L(x + 1, y + 1, c);
                gx[y * w + x] = sx; gy[y * w + x] = sy;
                mag[y * w + x] = Math.Sqrt(sx * sx + sy * sy) / 4;     // 0..~255 for a full black/white step
            }
        var dst = onlyLines ? new PixelImage(w, h) : src.Clone();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (mag[i] < threshold || Pixel.IsEmpty(src.Pixels[i])) continue;
                if (thin)
                {
                    // non-maximum suppression across the edge direction (the Canny step), which makes lines one pixel wide
                    double ax = Math.Abs(gx[i]), ay = Math.Abs(gy[i]);
                    int ox = ax >= ay ? 1 : 0, oy = ax >= ay ? 0 : 1;
                    double m1 = x - ox >= 0 && y - oy >= 0 ? mag[(y - oy) * w + x - ox] : 0;
                    double m2 = x + ox < w && y + oy < h ? mag[(y + oy) * w + x + ox] : 0;
                    if (mag[i] < m1 || mag[i] <= m2) continue;
                }
                dst.Pixels[i] = color | 0xFF000000u;
            }
        return dst;
    }
}
