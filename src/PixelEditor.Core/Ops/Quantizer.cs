// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
namespace PixelEditor.Core.Ops;

public static class Quantizer
{
    /// <summary>Builds a palette of N colors by median cut over the opaque pixels.</summary>
    public static List<uint> BuildPalette(PixelImage image, int colors) =>
        BuildPalette(image.Pixels, colors, excluded: null);

    /// <summary>Builds one palette for a set of images and reserves places for the given colors.</summary>
    public static List<uint> BuildSharedPalette(IEnumerable<PixelImage> images, int colors, IEnumerable<uint> fixedColors)
    {
        if (colors is < 1 or > 256) throw new ArgumentOutOfRangeException(nameof(colors));
        var reserved = fixedColors.Select(p => p | 0xFF000000u).ToHashSet();
        if (reserved.Count > colors) throw new ArgumentException("There are more fixed colors than palette slots.");
        if (reserved.Count == colors) return reserved.Order().ToList();

        return BuildPalette(images.SelectMany(image => image.Pixels), colors - reserved.Count, reserved)
            .Concat(reserved).Distinct().Order().ToList();
    }

    private static List<uint> BuildPalette(IEnumerable<uint> pixels, int colors, HashSet<uint>? excluded)
    {
        var counts = new Dictionary<uint, long>();
        foreach (var p in pixels)
        {
            if (Pixel.A(p) < 128) continue;
            uint c = p | 0xFF000000u;
            if (excluded?.Contains(c) == true) continue;
            counts[c] = counts.GetValueOrDefault(c) + 1;
        }
        if (counts.Count <= colors) return counts.Keys.OrderBy(c => c).ToList();

        var boxes = new List<List<(uint Color, long Count)>> { counts.Select(kv => (kv.Key, kv.Value)).ToList() };
        while (boxes.Count < colors)
        {
            // split the "largest" box: channel range * number of pixels
            int bestIdx = -1;
            double bestScore = 0;
            for (int i = 0; i < boxes.Count; i++)
            {
                if (boxes[i].Count < 2) continue;
                double score = Range(boxes[i]).Range * boxes[i].Sum(e => (double)e.Count);
                if (score > bestScore) { bestScore = score; bestIdx = i; }
            }
            if (bestIdx < 0) break;

            var box = boxes[bestIdx];
            int channel = Range(box).Channel;
            box.Sort((a, b) => Channel(a.Color, channel).CompareTo(Channel(b.Color, channel)));
            long half = box.Sum(e => e.Count) / 2, acc = 0;
            int split = 1;
            for (int i = 0; i < box.Count - 1; i++)
            {
                acc += box[i].Count;
                split = i + 1;
                if (acc >= half) break;
            }
            boxes[bestIdx] = box.GetRange(0, split);
            boxes.Add(box.GetRange(split, box.Count - split));
        }

        return boxes.Select(Average).Distinct().OrderBy(c => c).ToList();
    }

    private static int Channel(uint c, int ch) => ch switch { 0 => Pixel.R(c), 1 => Pixel.G(c), _ => Pixel.B(c) };

    private static (int Channel, int Range) Range(List<(uint Color, long Count)> box)
    {
        int bestCh = 0, bestRange = -1;
        for (int ch = 0; ch < 3; ch++)
        {
            int min = 255, max = 0;
            foreach (var (c, _) in box)
            {
                int v = Channel(c, ch);
                if (v < min) min = v;
                if (v > max) max = v;
            }
            if (max - min > bestRange) { bestRange = max - min; bestCh = ch; }
        }
        return (bestCh, bestRange);
    }

    private static uint Average(List<(uint Color, long Count)> box)
    {
        double n = box.Sum(e => (double)e.Count), r = 0, g = 0, b = 0;
        foreach (var (c, cnt) in box)
        {
            r += Pixel.R(c) * cnt;
            g += Pixel.G(c) * cnt;
            b += Pixel.B(c) * cnt;
        }
        return Pixel.Rgb((byte)Math.Round(r / n), (byte)Math.Round(g / n), (byte)Math.Round(b / n));
    }

    public static uint Nearest(uint color, IReadOnlyList<uint> palette)
    {
        uint best = palette[0];
        long bestD = long.MaxValue;
        foreach (var p in palette)
        {
            long d = Distance(color, p);
            if (d < bestD) { bestD = d; best = p; if (d == 0) break; }
        }
        return best;
    }

    /// <summary>Weighted distance in RGB (the eye is more sensitive to green).</summary>
    public static long Distance(uint a, uint b)
    {
        long dr = Pixel.R(a) - Pixel.R(b), dg = Pixel.G(a) - Pixel.G(b), db = Pixel.B(a) - Pixel.B(b);
        return 2 * dr * dr + 4 * dg * dg + 3 * db * db;
    }

    /// <summary>Maps a picture onto a palette; optionally with Floyd-Steinberg dithering.</summary>
    public static PixelImage Apply(PixelImage src, IReadOnlyList<uint> palette, bool dither)
    {
        if (palette.Count == 0) throw new ArgumentException(CoreText.T("Палитра пуста"));
        var dst = new PixelImage(src.Width, src.Height);
        var err = dither ? new float[src.Width * src.Height * 3] : null;

        for (int y = 0; y < src.Height; y++)
            for (int x = 0; x < src.Width; x++)
            {
                int i = y * src.Width + x;
                uint p = src.Pixels[i];
                if (Pixel.A(p) < 128) continue;

                double r = Pixel.R(p), g = Pixel.G(p), b = Pixel.B(p);
                if (err != null)
                {
                    r += err[i * 3]; g += err[i * 3 + 1]; b += err[i * 3 + 2];
                }
                uint want = Pixel.Rgb(
                    (byte)Math.Clamp((int)Math.Round(r), 0, 255),
                    (byte)Math.Clamp((int)Math.Round(g), 0, 255),
                    (byte)Math.Clamp((int)Math.Round(b), 0, 255));
                uint got = Nearest(want, palette);
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
                Spread(1, 0, 7 / 16f);
                Spread(-1, 1, 3 / 16f);
                Spread(0, 1, 5 / 16f);
                Spread(1, 1, 1 / 16f);
            }
        return dst;
    }
}
