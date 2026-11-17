// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
namespace PixelEditor.Core;

/// <summary>
/// A free-form selection: the Bounds frame and one bit per pixel inside it (Bits, row by row).
/// Produced by the magic wand or lasso; a rectangular selection stays a plain PixelRect.
/// </summary>
public sealed class SelectionMask
{
    public SelectionMask(PixelRect bounds, bool[] bits)
    {
        if (bits.Length != bounds.Width * bounds.Height) throw new ArgumentException(CoreText.T("Размер маски не совпадает с рамкой"));
        Bounds = bounds;
        Bits = bits;
    }

    public PixelRect Bounds { get; }
    public bool[] Bits { get; }

    public bool Contains(int x, int y) => Bounds.Contains(x, y) && Bits[(y - Bounds.Y) * Bounds.Width + (x - Bounds.X)];

    public int Count => Bits.Count(b => b);

    /// <summary>A mask by condition within width×height; null if no pixel matched. The frame is fitted to the chosen pixels.</summary>
    public static SelectionMask? From(int width, int height, Func<int, int, bool> pick)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        var all = new bool[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                if (pick(x, y))
                {
                    all[y * width + x] = true;
                    minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                }
        if (maxX < 0) return null;
        var b = new PixelRect(minX, minY, maxX - minX + 1, maxY - minY + 1);
        var bits = new bool[b.Width * b.Height];
        for (int y = 0; y < b.Height; y++)
            for (int x = 0; x < b.Width; x++)
                bits[y * b.Width + x] = all[(b.Y + y) * width + b.X + x];
        return new SelectionMask(b, bits);
    }

    /// <summary>Union of two masks (within width×height).</summary>
    public SelectionMask Union(SelectionMask other, int width, int height) =>
        From(width, height, (x, y) => Contains(x, y) || other.Contains(x, y))!;

    /// <summary>Subtraction of another mask.</summary>
    public SelectionMask? Subtract(SelectionMask other, int width, int height) =>
        From(width, height, (x, y) => Contains(x, y) && !other.Contains(x, y));

    /// <summary>A rectangular area as a mask.</summary>
    public static SelectionMask Rectangle(PixelRect rect) => new(rect, Enumerable.Repeat(true, rect.Width * rect.Height).ToArray());

    /// <summary>Color match with tolerance: transparent only with transparent, the others by the largest R, G, B channel difference.</summary>
    public static bool SimilarColor(uint a, uint b, int tolerance)
    {
        bool ea = Pixel.IsEmpty(a), eb = Pixel.IsEmpty(b);
        if (ea || eb) return ea && eb;
        int d = Math.Max(Math.Abs(Pixel.R(a) - Pixel.R(b)), Math.Max(Math.Abs(Pixel.G(a) - Pixel.G(b)), Math.Abs(Pixel.B(a) - Pixel.B(b))));
        return d <= tolerance;
    }

    /// <summary>
    /// Magic wand: selects pixels similar to the color under (x, y). contiguous: only the area connected to the point (by sides),
    /// otherwise all matching pixels of the layer. The empty background can be selected too.
    /// </summary>
    public static SelectionMask? Wand(Layer layer, int x, int y, int tolerance, bool contiguous, bool follow = false)
    {
        if (!layer.InBounds(x, y)) return null;
        int w = layer.Width, h = layer.Height;
        uint target = layer.Get(x, y);
        // The tolerance is a perceptual distance (ColorMetric, about one RGB step on mid grays); 0 is an exact match.
        if (!contiguous) return From(w, h, (px, py) => ColorMetric.Similar(layer.Get(px, py), target, tolerance));

        // follow: a pixel is also taken when it differs little (step) from the neighbor the fill came from, as long as it stays within limit of the clicked
        // color: a click on a gradient or a noisy background runs along it and stops at a sharp edge. The step is the tolerance but at least 8, so noise passes.
        double step = Math.Max(tolerance, 8), limit = Math.Max(tolerance * 5.0, 60);
        var seen = new bool[w * h];
        var stack = new Stack<(int X, int Y)>();
        stack.Push((x, y));
        seen[y * w + x] = true;
        while (stack.Count > 0)
        {
            var (cx, cy) = stack.Pop();
            uint from = layer.Get(cx, cy);
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int nx = cx + dx, ny = cy + dy;
                if (nx < 0 || ny < 0 || nx >= w || ny >= h || seen[ny * w + nx]) continue;
                uint p = layer.Get(nx, ny);
                if (!ColorMetric.Similar(p, target, tolerance)
                    && !(follow && !Pixel.IsEmpty(p) && !Pixel.IsEmpty(from) && ColorMetric.Similar(p, from, step) && ColorMetric.Similar(p, target, limit))) continue;
                seen[ny * w + nx] = true;
                stack.Push((nx, ny));
            }
        }
        return From(w, h, (px, py) => seen[py * w + px]);
    }

    /// <summary>
    /// "Object": all non-empty pixels connected to (x, y) by adjacency (diagonals included). Neighboring objects and the empty background are not touched.
    /// null if (x, y) is empty.
    /// </summary>
    public static SelectionMask? Object(Layer layer, int x, int y)
    {
        int w = layer.Width, h = layer.Height;
        if (x < 0 || y < 0 || x >= w || y >= h || Pixel.IsEmpty(layer.Get(x, y))) return null;
        var seen = new bool[w * h];
        var stack = new Stack<(int X, int Y)>();
        stack.Push((x, y));
        seen[y * w + x] = true;
        while (stack.Count > 0)
        {
            var (cx, cy) = stack.Pop();
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = cx + dx, ny = cy + dy;
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h || seen[ny * w + nx] || Pixel.IsEmpty(layer.Get(nx, ny))) continue;
                    seen[ny * w + nx] = true;
                    stack.Push((nx, ny));
                }
        }
        return From(w, h, (px, py) => seen[py * w + px]);
    }

    /// <summary>
    /// Lasso: a closed polygon through the points (the last one connects to the first). The interior is taken (even-odd rule on pixel
    /// centers, row by row) together with the outline lines themselves, so even a thin loop selects something. Works within the points' frame, so it is fast.
    /// </summary>
    public static SelectionMask? Polygon(IReadOnlyList<(int X, int Y)> points, int width, int height)
    {
        if (points.Count == 0) return null;
        int minX = Math.Max(0, points.Min(p => p.X)), maxX = Math.Min(width - 1, points.Max(p => p.X));
        int minY = Math.Max(0, points.Min(p => p.Y)), maxY = Math.Min(height - 1, points.Max(p => p.Y));
        if (maxX < minX || maxY < minY) return null;
        var b = new PixelRect(minX, minY, maxX - minX + 1, maxY - minY + 1);
        var bits = new bool[b.Width * b.Height];
        void Set(int px, int py)
        {
            if (b.Contains(px, py)) bits[(py - b.Y) * b.Width + (px - b.X)] = true;
        }

        for (int i = 0; i < points.Count; i++)
        {
            var a = points[i];
            var c = points[(i + 1) % points.Count];
            Drawing.Line(a.X, a.Y, c.X, c.Y, Set);
        }

        var xs = new List<double>();
        for (int py = b.Y; py < b.Y + b.Height; py++)
        {
            double cy = py + 0.5;
            xs.Clear();
            for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
            {
                double yi = points[i].Y + 0.5, yj = points[j].Y + 0.5;
                if ((yi > cy) == (yj > cy)) continue;
                double xi = points[i].X + 0.5, xj = points[j].X + 0.5;
                xs.Add((xj - xi) * (cy - yi) / (yj - yi) + xi);
            }
            xs.Sort();
            for (int k = 0; k + 1 < xs.Count; k += 2)
            {
                int from = (int)Math.Ceiling(xs[k] - 0.5), to = (int)Math.Ceiling(xs[k + 1] - 0.5) - 1;
                for (int px = Math.Max(from, b.X); px <= Math.Min(to, b.X + b.Width - 1); px++) Set(px, py);
            }
        }
        return new SelectionMask(b, bits);
    }

    /// <summary>A copy of the layer pixels only under the mask (transparent outside the mask), the size of the frame.</summary>
    public PixelImage Copy(Layer layer)
    {
        var img = new PixelImage(Bounds.Width, Bounds.Height);
        for (int y = 0; y < Bounds.Height; y++)
            for (int x = 0; x < Bounds.Width; x++)
                if (Bits[y * Bounds.Width + x]) img.Pixels[y * Bounds.Width + x] = layer.Get(Bounds.X + x, Bounds.Y + y);
        return img;
    }

    /// <summary>Erases the pixels under the mask.</summary>
    public void Clear(PixelBatch batch)
    {
        for (int y = 0; y < Bounds.Height; y++)
            for (int x = 0; x < Bounds.Width; x++)
                if (Bits[y * Bounds.Width + x]) batch.Set(Bounds.X + x, Bounds.Y + y, Pixel.Empty);
    }

    /// <summary>The same mask shifted by (dx, dy).</summary>
    public SelectionMask Translate(int dx, int dy) => new(new PixelRect(Bounds.X + dx, Bounds.Y + dy, Bounds.Width, Bounds.Height), Bits);
}
