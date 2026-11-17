// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
namespace PixelEditor.Core.Ops;

public enum ResizeMethod
{
    /// <summary>Nearest neighbor: fast, but loses details and gives noise when downscaling a lot.</summary>
    Nearest,
    /// <summary>Area averaging (alpha aware): an honest downscale of photos and gradients, creates new colors.</summary>
    Box,
    /// <summary>The most frequent color in a block: creates no new colors, the best option for pixel art.</summary>
    Mode,
    /// <summary>
    /// Edge preserving: in a block there are two color clusters (k-means), the mean color of the larger cluster is taken (the darker one on a tie).
    /// Outlines stay sharp and thin lines are not blurred as with averaging, but colors do not "stick" to neighboring ones as with the most frequent exact-match color.
    /// </summary>
    Smart,
}

public static class Resampler
{
    public static PixelImage Resize(PixelImage src, int dw, int dh, ResizeMethod method)
    {
        if (dw <= 0 || dh <= 0) throw new ArgumentOutOfRangeException(nameof(dw), CoreText.T("Размер должен быть > 0"));
        if (dw == src.Width && dh == src.Height) return src.Clone();

        var dst = new PixelImage(dw, dh);
        double sx = (double)src.Width / dw, sy = (double)src.Height / dh;

        for (int y = 0; y < dh; y++)
        {
            for (int x = 0; x < dw; x++)
            {
                dst.Pixels[y * dw + x] = method switch
                {
                    ResizeMethod.Nearest => src.Get(
                        Math.Min(src.Width - 1, (int)((x + 0.5) * sx)),
                        Math.Min(src.Height - 1, (int)((y + 0.5) * sy))),
                    ResizeMethod.Box => BoxSample(src, x * sx, (x + 1) * sx, y * sy, (y + 1) * sy),
                    ResizeMethod.Smart => SmartSample(src, x * sx, (x + 1) * sx, y * sy, (y + 1) * sy),
                    _ => ModeSample(src, x * sx, (x + 1) * sx, y * sy, (y + 1) * sy),
                };
            }
        }
        return dst;
    }

    private static uint BoxSample(PixelImage src, double x0, double x1, double y0, double y1)
    {
        double wSum = 0, aSum = 0, r = 0, g = 0, b = 0;
        int ix0 = (int)Math.Floor(x0), ix1 = (int)Math.Ceiling(x1) - 1;
        int iy0 = (int)Math.Floor(y0), iy1 = (int)Math.Ceiling(y1) - 1;
        for (int iy = iy0; iy <= iy1; iy++)
        {
            double wy = Math.Min(y1, iy + 1) - Math.Max(y0, iy);
            if (wy <= 0) continue;
            for (int ix = ix0; ix <= ix1; ix++)
            {
                double wx = Math.Min(x1, ix + 1) - Math.Max(x0, ix);
                if (wx <= 0) continue;
                double w = wx * wy;
                uint p = src.Get(Math.Clamp(ix, 0, src.Width - 1), Math.Clamp(iy, 0, src.Height - 1));
                double a = Pixel.A(p) / 255.0;
                wSum += w;
                aSum += w * a;
                r += w * a * Pixel.R(p);
                g += w * a * Pixel.G(p);
                b += w * a * Pixel.B(p);
            }
        }
        if (aSum <= 0 || wSum <= 0) return Pixel.Empty;
        byte alpha = (byte)Math.Round(255.0 * aSum / wSum);
        return Pixel.Argb(alpha, (byte)Math.Round(r / aSum), (byte)Math.Round(g / aSum), (byte)Math.Round(b / aSum));
    }

    private static uint SmartSample(PixelImage src, double x0, double x1, double y0, double y1)
    {
        int ix0 = (int)Math.Floor(x0), ix1 = Math.Max(ix0 + 1, (int)Math.Ceiling(x1));
        int iy0 = (int)Math.Floor(y0), iy1 = Math.Max(iy0 + 1, (int)Math.Ceiling(y1));
        var colors = new List<uint>();
        int total = 0;
        for (int iy = iy0; iy < iy1; iy++)
            for (int ix = ix0; ix < ix1; ix++)
            {
                total++;
                uint p = Pixel.Binarize(src.Get(Math.Clamp(ix, 0, src.Width - 1), Math.Clamp(iy, 0, src.Height - 1)));
                if (!Pixel.IsEmpty(p)) colors.Add(p);
            }
        if (colors.Count * 2 < total || colors.Count == 0) return Pixel.Empty;
        if (colors.Count == 1) return colors[0];

        static int Luma(uint c) => Pixel.R(c) * 299 + Pixel.G(c) * 587 + Pixel.B(c) * 114;
        uint a = colors.OrderBy(Luma).First(), b = colors.OrderBy(Luma).Last();
        if (a == b) return a;
        var groupA = new List<uint>();
        var groupB = new List<uint>();
        for (int iter = 0; iter < 4; iter++)
        {
            groupA.Clear();
            groupB.Clear();
            foreach (var c in colors)
                (Quantizer.Distance(c, a) <= Quantizer.Distance(c, b) ? groupA : groupB).Add(c);
            if (groupA.Count == 0 || groupB.Count == 0) break;
            a = Mean(groupA);
            b = Mean(groupB);
        }
        if (groupA.Count == 0) return Mean(groupB);
        if (groupB.Count == 0) return Mean(groupA);
        // the larger cluster wins, but a dark and clearly contrasting one (an outline, a thin line) wins already at a quarter of the block, otherwise lines thinner than half a block would disappear
        var (dark, light) = Luma(Mean(groupA)) <= Luma(Mean(groupB)) ? (groupA, groupB) : (groupB, groupA);
        bool contrast = Luma(Mean(light)) - Luma(Mean(dark)) > 90_000;
        var win = contrast && dark.Count * 4 >= dark.Count + light.Count ? dark
            : groupA.Count > groupB.Count ? groupA : groupB.Count > groupA.Count ? groupB : dark;
        return Mean(win);
    }

    private static uint Mean(List<uint> list) =>
        Pixel.Rgb((byte)Math.Round(list.Average(c => (double)Pixel.R(c))), (byte)Math.Round(list.Average(c => (double)Pixel.G(c))), (byte)Math.Round(list.Average(c => (double)Pixel.B(c))));

    private static uint ModeSample(PixelImage src, double x0, double x1, double y0, double y1)
    {
        int ix0 = (int)Math.Floor(x0);
        int ix1 = Math.Max(ix0 + 1, (int)Math.Ceiling(x1));
        int iy0 = (int)Math.Floor(y0);
        int iy1 = Math.Max(iy0 + 1, (int)Math.Ceiling(y1));

        var counts = new Dictionary<uint, int>();
        int total = 0, opaque = 0;
        for (int iy = iy0; iy < iy1; iy++)
            for (int ix = ix0; ix < ix1; ix++)
            {
                total++;
                uint p = Pixel.Binarize(src.Get(Math.Clamp(ix, 0, src.Width - 1), Math.Clamp(iy, 0, src.Height - 1)));
                if (Pixel.IsEmpty(p)) continue;
                opaque++;
                counts[p] = counts.GetValueOrDefault(p) + 1;
            }

        // a block with more than half empty pixels stays empty
        if (opaque * 2 < total || counts.Count == 0) return Pixel.Empty;

        uint best = 0;
        int bestCount = -1;
        foreach (var (color, n) in counts)
            if (n > bestCount || (n == bestCount && color < best)) { best = color; bestCount = n; }
        return best;
    }
}
