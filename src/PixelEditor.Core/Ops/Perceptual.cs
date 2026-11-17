// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
namespace PixelEditor.Core.Ops;

/// <summary>The Lab color space: distances in it are close to how the eye tells colors apart. Needed for quantization and palette matching.</summary>
public static class Lab
{
    public static (double L, double A, double B) From(uint c)
    {
        double R(double v) { v /= 255.0; return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
        double r = R(Pixel.R(c)), g = R(Pixel.G(c)), b = R(Pixel.B(c));
        double x = (0.4124 * r + 0.3576 * g + 0.1805 * b) / 0.95047;
        double y = 0.2126 * r + 0.7152 * g + 0.0722 * b;
        double z = (0.0193 * r + 0.1192 * g + 0.9505 * b) / 1.08883;
        double F(double t) => t > 0.008856 ? Math.Cbrt(t) : 7.787 * t + 16.0 / 116.0;
        double fx = F(x), fy = F(y), fz = F(z);
        return (116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz));
    }

    public static double Distance((double L, double A, double B) p, (double L, double A, double B) q)
    {
        double dl = p.L - q.L, da = p.A - q.A, db = p.B - q.B;
        return dl * dl + da * da + db * db;
    }

    /// <summary>The nearest palette color by Lab.</summary>
    public static uint Nearest(uint color, IReadOnlyList<uint> palette, IReadOnlyList<(double L, double A, double B)> paletteLab)
    {
        var lab = From(color);
        int best = 0;
        double bd = double.MaxValue;
        for (int i = 0; i < palette.Count; i++)
        {
            double d = Distance(lab, paletteLab[i]);
            if (d < bd) { bd = d; best = i; if (d == 0) break; }
        }
        return palette[best];
    }
}

/// <summary>A palette and reduction to it with Lab distance (the result is closer to what the eye sees than RGB median cut).</summary>
public static class PerceptualQuantizer
{
    /// <summary>
    /// A palette of N colors: first median cut, then several k-means steps in Lab weighted by color frequency.
    /// Rare but noticeable colors (accents) are preserved better than with plain averaging in RGB.
    /// </summary>
    public static List<uint> BuildPalette(PixelImage image, int colors, int iterations = 6)
    {
        var start = Quantizer.BuildPalette(image, colors);
        if (start.Count <= 1) return start;

        var counts = new Dictionary<uint, int>();
        foreach (var p in image.Pixels)
        {
            if (Pixel.A(p) < 128) continue;
            uint c = p | 0xFF000000u;
            counts[c] = counts.GetValueOrDefault(c) + 1;
        }
        var colorsLab = counts.Keys.Select(c => (Color: c, Lab: Lab.From(c), Count: counts[c])).ToList();
        var centers = start.Select(Lab.From).ToList();

        for (int it = 0; it < iterations; it++)
        {
            var sum = centers.Select(_ => (L: 0.0, A: 0.0, B: 0.0, N: 0.0)).ToArray();
            foreach (var e in colorsLab)
            {
                int best = 0;
                double bd = double.MaxValue;
                for (int i = 0; i < centers.Count; i++)
                {
                    double d = Lab.Distance(e.Lab, centers[i]);
                    if (d < bd) { bd = d; best = i; }
                }
                sum[best] = (sum[best].L + e.Lab.L * e.Count, sum[best].A + e.Lab.A * e.Count, sum[best].B + e.Lab.B * e.Count, sum[best].N + e.Count);
            }
            for (int i = 0; i < centers.Count; i++)
                if (sum[i].N > 0) centers[i] = (sum[i].L / sum[i].N, sum[i].A / sum[i].N, sum[i].B / sum[i].N);
        }

        // the final palette color: a real image color nearest to the center (so the palette gets no "nonexistent" shades)
        var palette = new List<uint>();
        foreach (var c in centers)
        {
            var nearest = colorsLab.OrderBy(e => Lab.Distance(e.Lab, c)).First().Color;
            if (!palette.Contains(nearest)) palette.Add(nearest);
        }
        return palette.OrderBy(c => c).ToList();
    }

    /// <summary>Reduction to a palette by the nearest in Lab; dither uses Floyd-Steinberg error in RGB.</summary>
    public static PixelImage Apply(PixelImage src, IReadOnlyList<uint> palette, bool dither)
    {
        if (palette.Count == 0) throw new ArgumentException(CoreText.T("Палитра пуста"));
        var lab = palette.Select(Lab.From).ToList();
        var dst = new PixelImage(src.Width, src.Height);
        var err = dither ? new float[src.Width * src.Height * 3] : null;
        for (int y = 0; y < src.Height; y++)
            for (int x = 0; x < src.Width; x++)
            {
                int i = y * src.Width + x;
                uint p = src.Pixels[i];
                if (Pixel.A(p) < 128) continue;
                double r = Pixel.R(p), g = Pixel.G(p), b = Pixel.B(p);
                if (err != null) { r += err[i * 3]; g += err[i * 3 + 1]; b += err[i * 3 + 2]; }
                uint want = Pixel.Rgb((byte)Math.Clamp((int)Math.Round(r), 0, 255), (byte)Math.Clamp((int)Math.Round(g), 0, 255), (byte)Math.Clamp((int)Math.Round(b), 0, 255));
                uint got = Lab.Nearest(want, palette, lab);
                dst.Pixels[i] = got;
                if (err == null) continue;
                float er = (float)(r - Pixel.R(got)), eg = (float)(g - Pixel.G(got)), eb = (float)(b - Pixel.B(got));
                void Spread(int dx, int dy, float k)
                {
                    int nx = x + dx, ny = y + dy;
                    if (!src.InBounds(nx, ny)) return;
                    int j = (ny * src.Width + nx) * 3;
                    err[j] += er * k; err[j + 1] += eg * k; err[j + 2] += eb * k;
                }
                Spread(1, 0, 7 / 16f); Spread(-1, 1, 3 / 16f); Spread(0, 1, 5 / 16f); Spread(1, 1, 1 / 16f);
            }
        return dst;
    }
}
