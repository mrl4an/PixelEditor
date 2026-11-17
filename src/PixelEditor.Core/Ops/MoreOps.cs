// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
namespace PixelEditor.Core.Ops;

/// <summary>Color operations over the whole picture: transparency does not change, transparent pixels are skipped.</summary>
public static class ColorOps
{
    private static byte Cl(double v) => (byte)Math.Round(Math.Clamp(v, 0, 255));

    public static double Luma(uint p) => 0.299 * Pixel.R(p) + 0.587 * Pixel.G(p) + 0.114 * Pixel.B(p);

    private static PixelImage Map(PixelImage src, Func<uint, uint> f)
    {
        var dst = src.Clone();
        for (int i = 0; i < dst.Pixels.Length; i++)
        {
            uint p = dst.Pixels[i];
            if (Pixel.IsEmpty(p)) continue;
            dst.Pixels[i] = (p & 0xFF000000u) | (f(p) & 0x00FFFFFFu);
        }
        return dst;
    }

    public static PixelImage Invert(PixelImage src) =>
        Map(src, p => Pixel.Rgb((byte)(255 - Pixel.R(p)), (byte)(255 - Pixel.G(p)), (byte)(255 - Pixel.B(p))));

    public static PixelImage Grayscale(PixelImage src) => Map(src, p => { var g = Cl(Luma(p)); return Pixel.Rgb(g, g, g); });

    /// <summary>Hue rotation by degrees (HSV); saturation and value are multipliers (1 = unchanged).</summary>
    public static PixelImage HueShift(PixelImage src, double degrees, double saturation = 1, double value = 1) =>
        Map(src, p =>
        {
            var (h, s, v) = ToHsv(p);
            h = ((h + degrees) % 360 + 360) % 360;
            return FromHsv(h, Math.Clamp(s * saturation, 0, 1), Math.Clamp(v * value, 0, 1));
        });

    /// <summary>Gradient map: pixel brightness is turned into a color between dark, mid (if given) and light.</summary>
    public static PixelImage Tint(PixelImage src, uint dark, uint light, uint? mid = null) =>
        Map(src, p =>
        {
            double t = Luma(p) / 255.0;
            if (mid is not uint m) return Lerp(dark, light, t);
            return t < 0.5 ? Lerp(dark, m, t * 2) : Lerp(m, light, (t - 0.5) * 2);
        });

    /// <summary>Posterization: each channel is reduced to levels (2..64) steps.</summary>
    public static PixelImage Posterize(PixelImage src, int levels)
    {
        levels = Math.Clamp(levels, 2, 64);
        double step = 255.0 / (levels - 1);
        byte Q(byte v) => Cl(Math.Round(v / step) * step);
        return Map(src, p => Pixel.Rgb(Q(Pixel.R(p)), Q(Pixel.G(p)), Q(Pixel.B(p))));
    }

    /// <summary>Two tones: brightness &gt;= value gives light, the rest dark.</summary>
    public static PixelImage Threshold(PixelImage src, int value, uint dark, uint light) =>
        Map(src, p => Luma(p) >= value ? light : dark);

    /// <summary>
    /// Stretches the brightness range to 0..255 (auto levels). A single-tone picture does not change.
    /// cutoff: percent of the darkest and of the lightest pixels ignored when the range is measured (like Pillow's ImageOps.autocontrast),
    /// so a few stray bright or dark pixels do not keep the picture flat.
    /// </summary>
    public static PixelImage AutoLevels(PixelImage src, double cutoff = 0)
    {
        double min = 255, max = 0;
        var lumas = new List<double>();
        foreach (var p in src.Pixels)
        {
            if (Pixel.IsEmpty(p)) continue;
            double l = Luma(p);
            lumas.Add(l);
            if (l < min) min = l;
            if (l > max) max = l;
        }
        if (cutoff > 0 && lumas.Count > 10)
        {
            lumas.Sort();
            int skip = (int)(lumas.Count * Math.Clamp(cutoff, 0, 20) / 100.0);
            min = lumas[Math.Min(skip, lumas.Count - 1)];
            max = lumas[Math.Max(lumas.Count - 1 - skip, 0)];
        }
        if (max - min < 1) return src.Clone();
        double k = 255.0 / (max - min);
        return Map(src, p => Pixel.Rgb(Cl((Pixel.R(p) - min) * k), Cl((Pixel.G(p) - min) * k), Cl((Pixel.B(p) - min) * k)));
    }

    /// <summary>Swaps two colors (pixel transparency is kept).</summary>
    public static PixelImage SwapColors(PixelImage src, uint a, uint b)
    {
        uint ra = a & 0xFFFFFFu, rb = b & 0xFFFFFFu;
        return Map(src, p => (p & 0xFFFFFFu) == ra ? rb : (p & 0xFFFFFFu) == rb ? ra : p);
    }

    /// <summary>Fills transparent pixels with a color (the picture "on a background").</summary>
    public static PixelImage Background(PixelImage src, uint color)
    {
        var dst = src.Clone();
        for (int i = 0; i < dst.Pixels.Length; i++)
            if (Pixel.IsEmpty(dst.Pixels[i])) dst.Pixels[i] = color | 0xFF000000u;
        return dst;
    }

    private static uint Lerp(uint a, uint b, double t) =>
        Pixel.Rgb(Cl(Pixel.R(a) + (Pixel.R(b) - Pixel.R(a)) * t), Cl(Pixel.G(a) + (Pixel.G(b) - Pixel.G(a)) * t), Cl(Pixel.B(a) + (Pixel.B(b) - Pixel.B(a)) * t));

    private static (double H, double S, double V) ToHsv(uint p)
    {
        double r = Pixel.R(p) / 255.0, g = Pixel.G(p) / 255.0, b = Pixel.B(p) / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        double h = d < 1e-9 ? 0 : max == r ? 60 * (((g - b) / d) % 6) : max == g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
        if (h < 0) h += 360;
        return (h, max < 1e-9 ? 0 : d / max, max);
    }

    private static uint FromHsv(double h, double s, double v)
    {
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        (double r, double g, double b) = h < 60 ? (c, x, 0.0) : h < 120 ? (x, c, 0.0) : h < 180 ? (0.0, c, x) : h < 240 ? (0.0, x, c) : h < 300 ? (x, 0.0, c) : (c, 0.0, x);
        return Pixel.Rgb(Cl((r + m) * 255), Cl((g + m) * 255), Cl((b + m) * 255));
    }
}

/// <summary>Operations on shape and layout: trim, symmetry, tile, grow and shrink, shadow, smoothing.</summary>
public static class ShapeOps
{
    public static PixelImage Crop(PixelImage src, int x, int y, int width, int height)
    {
        int x0 = Math.Clamp(x, 0, src.Width), y0 = Math.Clamp(y, 0, src.Height);
        int x1 = Math.Clamp(x + width, 0, src.Width), y1 = Math.Clamp(y + height, 0, src.Height);
        if (x1 <= x0 || y1 <= y0) throw new ArgumentException(CoreText.T("crop: прямоугольник вне картинки или пустой"));
        var dst = new PixelImage(x1 - x0, y1 - y0);
        for (int yy = y0; yy < y1; yy++)
            Array.Copy(src.Pixels, yy * src.Width + x0, dst.Pixels, (yy - y0) * dst.Width, x1 - x0);
        return dst;
    }

    /// <summary>Symmetry: left/right/top/bottom copy one half mirrored onto the other, quad: the top left corner onto all four.</summary>
    public static PixelImage Mirror(PixelImage src, string mode)
    {
        var dst = src.Clone();
        int w = src.Width, h = src.Height;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool left = x < (w + 1) / 2, top = y < (h + 1) / 2;
                int sx = x, sy = y;
                switch (mode)
                {
                    case "left": if (!left) sx = w - 1 - x; break;
                    case "right": if (left && x < w / 2) sx = w - 1 - x; break;
                    case "top": if (!top) sy = h - 1 - y; break;
                    case "bottom": if (top && y < h / 2) sy = h - 1 - y; break;
                    case "quad": if (!left) sx = w - 1 - x; if (!top) sy = h - 1 - y; break;
                    default: throw new ArgumentException(CoreText.F("mirror: неизвестный mode '{0}' (left|right|top|bottom|quad)", mode));
                }
                dst.Pixels[y * w + x] = src.Pixels[sy * w + sx];
            }
        return dst;
    }

    public static PixelImage Tile(PixelImage src, int cols, int rows)
    {
        cols = Math.Clamp(cols, 1, 64); rows = Math.Clamp(rows, 1, 64);
        if ((long)src.Width * cols > 8192 || (long)src.Height * rows > 8192)
            throw new ArgumentException(CoreText.T("tile: результат больше 8192 пикселей"));
        var dst = new PixelImage(src.Width * cols, src.Height * rows);
        for (int ty = 0; ty < rows; ty++)
            for (int tx = 0; tx < cols; tx++)
                for (int y = 0; y < src.Height; y++)
                    Array.Copy(src.Pixels, y * src.Width, dst.Pixels, (ty * src.Height + y) * dst.Width + tx * src.Width, src.Width);
        return dst;
    }

    /// <summary>Grows the shape by amount pixels: new pixels take the neighbor color (the most frequent) or a given color.</summary>
    public static PixelImage Grow(PixelImage src, int amount, uint? color = null, bool diagonal = false)
    {
        var cur = src;
        for (int pass = 0; pass < Math.Clamp(amount, 0, 32); pass++)
        {
            var next = cur.Clone();
            for (int y = 0; y < cur.Height; y++)
                for (int x = 0; x < cur.Width; x++)
                {
                    if (!Pixel.IsEmpty(cur.Pixels[y * cur.Width + x])) continue;
                    var counts = new Dictionary<uint, int>();
                    foreach (var (dx, dy) in Neighbors(diagonal))
                    {
                        uint n = cur.Get(x + dx, y + dy);
                        if (!Pixel.IsEmpty(n)) counts[n] = counts.GetValueOrDefault(n) + 1;
                    }
                    if (counts.Count == 0) continue;
                    next.Pixels[y * cur.Width + x] = color is uint c ? c | 0xFF000000u : counts.OrderByDescending(p => p.Value).First().Key;
                }
            cur = next;
        }
        return cur == src ? src.Clone() : cur;
    }

    /// <summary>Shrinks the shape: removes edge pixels (next to transparency and at the canvas edge), amount times.</summary>
    public static PixelImage Shrink(PixelImage src, int amount, bool diagonal = false)
    {
        var cur = src;
        for (int pass = 0; pass < Math.Clamp(amount, 0, 32); pass++)
        {
            var next = cur.Clone();
            for (int y = 0; y < cur.Height; y++)
                for (int x = 0; x < cur.Width; x++)
                {
                    if (Pixel.IsEmpty(cur.Pixels[y * cur.Width + x])) continue;
                    if (Neighbors(diagonal).Any(d => Pixel.IsEmpty(cur.Get(x + d.dx, y + d.dy)))) next.Pixels[y * cur.Width + x] = Pixel.Empty;
                }
            cur = next;
        }
        return cur == src ? src.Clone() : cur;
    }

    /// <summary>Shadow: a copy of the shape shifted by dx, dy, under the picture itself. autoPad grows the canvas so the shadow is not cut off.</summary>
    public static PixelImage DropShadow(PixelImage src, int dx, int dy, uint color, bool autoPad)
    {
        var baseImg = autoPad ? Transform.Pad(src, Math.Max(0, -dx), Math.Max(0, -dy), Math.Max(0, dx), Math.Max(0, dy)) : src;
        var dst = new PixelImage(baseImg.Width, baseImg.Height);
        for (int y = 0; y < baseImg.Height; y++)
            for (int x = 0; x < baseImg.Width; x++)
                if (!Pixel.IsEmpty(baseImg.Pixels[y * baseImg.Width + x]) && dst.InBounds(x + dx, y + dy))
                    dst.Pixels[(y + dy) * dst.Width + x + dx] = color | 0xFF000000u;
        for (int i = 0; i < dst.Pixels.Length; i++)
            if (!Pixel.IsEmpty(baseImg.Pixels[i])) dst.Pixels[i] = baseImg.Pixels[i];
        return dst;
    }

    /// <summary>Removes noise: a pixel is replaced by the most frequent color among its 3×3 neighbors if that color occurs more often than its own.</summary>
    public static PixelImage Smooth(PixelImage src, int passes)
    {
        var cur = src.Clone();
        for (int pass = 0; pass < Math.Clamp(passes, 1, 8); pass++)
        {
            var next = cur.Clone();
            for (int y = 0; y < cur.Height; y++)
                for (int x = 0; x < cur.Width; x++)
                {
                    uint self = cur.Pixels[y * cur.Width + x];
                    if (Pixel.IsEmpty(self)) continue;
                    var counts = new Dictionary<uint, int>();
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            uint n = cur.Get(x + dx, y + dy);
                            if (!Pixel.IsEmpty(n)) counts[n] = counts.GetValueOrDefault(n) + 1;
                        }
                    var best = counts.OrderByDescending(p => p.Value).First();
                    if (best.Value > counts[self]) next.Pixels[y * cur.Width + x] = best.Key;
                }
            cur = next;
        }
        return cur;
    }

    /// <summary>Mosaic: size×size blocks are filled with the block's most frequent color; a block with less than half opaque pixels becomes transparent. The picture size does not change.</summary>
    public static PixelImage Pixelate(PixelImage src, int size)
    {
        size = Math.Clamp(size, 1, 256);
        var dst = new PixelImage(src.Width, src.Height);
        for (int by = 0; by < src.Height; by += size)
            for (int bx = 0; bx < src.Width; bx += size)
            {
                var counts = new Dictionary<uint, int>();
                int total = 0, opaque = 0;
                for (int y = by; y < Math.Min(by + size, src.Height); y++)
                    for (int x = bx; x < Math.Min(bx + size, src.Width); x++)
                    {
                        total++;
                        uint p = src.Pixels[y * src.Width + x];
                        if (Pixel.IsEmpty(p)) continue;
                        opaque++;
                        counts[p] = counts.GetValueOrDefault(p) + 1;
                    }
                if (opaque * 2 < total || counts.Count == 0) continue;
                uint c = counts.OrderByDescending(p => p.Value).First().Key;
                for (int y = by; y < Math.Min(by + size, src.Height); y++)
                    for (int x = bx; x < Math.Min(bx + size, src.Width); x++) dst.Pixels[y * src.Width + x] = c;
            }
        return dst;
    }

    private static IEnumerable<(int dx, int dy)> Neighbors(bool diagonal)
    {
        yield return (1, 0); yield return (-1, 0); yield return (0, 1); yield return (0, -1);
        if (!diagonal) yield break;
        yield return (1, 1); yield return (-1, 1); yield return (1, -1); yield return (-1, -1);
    }
}

/// <summary>
/// Color junk cleanup: small connected areas of one color (single pixels, "crumbs" after downscaling a photo and quantizing)
/// are repainted with the color of the neighboring area with which they share the longest border. Transparency is not touched:
/// opaque crumbs do not turn into a hole, and empty areas are not filled (removeIslands and fillHoles are for those).
/// </summary>
public static class Despeckle
{
    public static PixelImage Apply(PixelImage src, int maxArea, bool diagonal, int passes)
    {
        maxArea = Math.Clamp(maxArea, 1, 64);
        var cur = src.Clone();
        for (int pass = 0; pass < Math.Clamp(passes, 1, 8); pass++)
            if (!Pass(cur, maxArea, diagonal)) break;
        return cur;
    }

    private static bool Pass(PixelImage img, int maxArea, bool diagonal)
    {
        int w = img.Width, h = img.Height;
        var label = new int[w * h];
        Array.Fill(label, -1);
        var sizes = new List<int>();
        var members = new List<List<int>>();
        var stack = new Stack<int>();
        for (int i = 0; i < label.Length; i++)
        {
            if (label[i] >= 0) continue;
            int id = sizes.Count;
            var list = new List<int>();
            label[i] = id; stack.Push(i);
            uint color = img.Pixels[i];
            while (stack.Count > 0)
            {
                int c = stack.Pop();
                list.Add(c);
                int cx = c % w, cy = c / w;
                foreach (var (dx, dy) in Dirs(diagonal))   // areas are also connected diagonally: a thin diagonal line does not fall apart into "crumbs"
                {
                    int nx = cx + dx, ny = cy + dy;
                    if ((uint)nx >= (uint)w || (uint)ny >= (uint)h) continue;
                    int n = ny * w + nx;
                    if (label[n] < 0 && img.Pixels[n] == color) { label[n] = id; stack.Push(n); }
                }
            }
            sizes.Add(list.Count);
            members.Add(list);
        }

        var changes = new List<(List<int> Pixels, uint Color)>();
        foreach (int id in Enumerable.Range(0, sizes.Count).Where(r => sizes[r] <= maxArea && !Pixel.IsEmpty(img.Pixels[members[r][0]])).OrderBy(r => sizes[r]))
        {
            var border = new Dictionary<int, int>();
            foreach (int c in members[id])
            {
                int cx = c % w, cy = c / w;
                foreach (var (dx, dy) in Dirs(false))
                {
                    int nx = cx + dx, ny = cy + dy;
                    if ((uint)nx >= (uint)w || (uint)ny >= (uint)h) continue;
                    int l = label[ny * w + nx];
                    if (l == id || Pixel.IsEmpty(img.Pixels[ny * w + nx])) continue;
                    border[l] = border.GetValueOrDefault(l) + 1;
                }
            }
            if (border.Count == 0) continue;
            // larger neighbors first (not the same kind of crumb), otherwise two neighboring crumbs would swap colors
            var big = border.Where(p => sizes[p.Key] > maxArea).ToList();
            var pool = big.Count > 0 ? big : border.ToList();
            uint own = img.Pixels[members[id][0]];
            int best = pool.OrderByDescending(p => p.Value).ThenBy(p => Dist(own, img.Pixels[members[p.Key][0]])).First().Key;
            changes.Add((members[id], img.Pixels[members[best][0]]));
        }
        foreach (var (pixels, color) in changes)
            foreach (int c in pixels) img.Pixels[c] = color;
        return changes.Count > 0;
    }

    private static int Dist(uint a, uint b)
    {
        int dr = Pixel.R(a) - Pixel.R(b), dg = Pixel.G(a) - Pixel.G(b), db = Pixel.B(a) - Pixel.B(b);
        return dr * dr + dg * dg + db * db;
    }

    private static IEnumerable<(int dx, int dy)> Dirs(bool diagonal)
    {
        yield return (1, 0); yield return (-1, 0); yield return (0, 1); yield return (0, -1);
        if (!diagonal) yield break;
        yield return (1, 1); yield return (-1, 1); yield return (1, -1); yield return (-1, -1);
    }
}
