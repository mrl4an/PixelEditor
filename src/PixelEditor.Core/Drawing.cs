// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
namespace PixelEditor.Core;

/// <summary>Raster primitives. Each function hands out pixels through a callback.</summary>
public static class Drawing
{
    public static void Line(int x0, int y0, int x1, int y1, Action<int, int> plot)
    {
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        while (true)
        {
            plot(x0, y0);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    public static void Rectangle(int x0, int y0, int x1, int y1, bool filled, Action<int, int> plot)
    {
        int l = Math.Min(x0, x1), r = Math.Max(x0, x1);
        int t = Math.Min(y0, y1), b = Math.Max(y0, y1);
        for (int y = t; y <= b; y++)
            for (int x = l; x <= r; x++)
                if (filled || x == l || x == r || y == t || y == b)
                    plot(x, y);
    }

    public static void RoundedRectangle(PixelRect bounds, int radius, bool filled, PixelRect clip, Action<int, int> plot)
    {
        if (bounds.IsEmpty) return;
        radius = Math.Clamp(radius, 0, Math.Min(bounds.Width, bounds.Height) / 2);
        bool Inside(int x, int y)
        {
            if (!bounds.Contains(x, y)) return false;
            int localX = x - bounds.X, localY = y - bounds.Y;
            long dx = Math.Max(0, radius - Math.Min(localX, bounds.Width - 1 - localX));
            long dy = Math.Max(0, radius - Math.Min(localY, bounds.Height - 1 - localY));
            return dx * dx + dy * dy <= (long)radius * radius;
        }

        int left = Math.Max(bounds.X, clip.X), top = Math.Max(bounds.Y, clip.Y);
        int right = Math.Min(bounds.X + bounds.Width, clip.X + clip.Width);
        int bottom = Math.Min(bounds.Y + bounds.Height, clip.Y + clip.Height);
        for (int y = top; y < bottom; y++)
            for (int x = left; x < right; x++)
                if (Inside(x, y) && (filled || !Inside(x - 1, y) || !Inside(x + 1, y) || !Inside(x, y - 1) || !Inside(x, y + 1)))
                    plot(x, y);
    }

    /// <summary>An ellipse inscribed in a rectangle. The outline has no holes: it is the border of the filled shape.</summary>
    public static void Ellipse(int x0, int y0, int x1, int y1, bool filled, Action<int, int> plot)
    {
        int l = Math.Min(x0, x1), r = Math.Max(x0, x1);
        int t = Math.Min(y0, y1), b = Math.Max(y0, y1);
        double cx = (l + r) / 2.0, cy = (t + b) / 2.0;
        double a = (r - l) / 2.0 + 0.5, bb = (b - t) / 2.0 + 0.5;

        bool Inside(int x, int y)
        {
            double nx = (x - cx) / a, ny = (y - cy) / bb;
            return nx * nx + ny * ny <= 1.0;
        }

        for (int y = t; y <= b; y++)
            for (int x = l; x <= r; x++)
            {
                if (!Inside(x, y)) continue;
                if (filled || !Inside(x - 1, y) || !Inside(x + 1, y) || !Inside(x, y - 1) || !Inside(x, y + 1))
                    plot(x, y);
            }
    }

    /// <summary>Fills a 4-connected area of the same color on the layer.</summary>
    public static void FloodFill(PixelBatch batch, int x, int y, uint color) => FloodFill(batch, x, y, color, 0, false);

    /// <summary>
    /// Fill with tolerance: a pixel counts as similar if each of its channels (transparency included)
    /// differs from the sample by no more than <paramref name="tolerance"/> (0..255).
    /// <paramref name="wholeLayer"/>: paints all similar pixels of the layer, not only the connected area.
    /// </summary>
    public static void FloodFill(PixelBatch batch, int x, int y, uint color, int tolerance, bool wholeLayer)
    {
        var layer = batch.Layer;
        if (!layer.InBounds(x, y)) return;
        tolerance = Math.Clamp(tolerance, 0, 255);
        uint target = layer.Get(x, y);
        if (target == color && tolerance == 0) return;

        int w = layer.Width, h = layer.Height;
        // compare with the original colors: the sample and "similarity" are computed before the fill
        var src = (uint[])layer.Pixels.Clone();
        bool Similar(uint p) => p == target || (tolerance > 0 && Near(p, target, tolerance));

        if (wholeLayer)
        {
            for (int i = 0; i < src.Length; i++)
                if (Similar(src[i])) batch.Set(i % w, i / w, color);
            return;
        }

        var seen = new bool[w * h];
        var stack = new Stack<(int, int)>();
        stack.Push((x, y));
        while (stack.Count > 0)
        {
            var (cx, cy) = stack.Pop();
            if (cx < 0 || cy < 0 || cx >= w || cy >= h) continue;
            int i = cy * w + cx;
            if (seen[i] || !Similar(src[i])) continue;
            seen[i] = true;
            batch.Set(cx, cy, color);
            stack.Push((cx + 1, cy));
            stack.Push((cx - 1, cy));
            stack.Push((cx, cy + 1));
            stack.Push((cx, cy - 1));
        }
    }

    private static bool Near(uint a, uint b, int t) =>
        Math.Abs((int)(a >> 24) - (int)(b >> 24)) <= t &&
        Math.Abs((int)((a >> 16) & 255) - (int)((b >> 16) & 255)) <= t &&
        Math.Abs((int)((a >> 8) & 255) - (int)((b >> 8) & 255)) <= t &&
        Math.Abs((int)(a & 255) - (int)(b & 255)) <= t;
}
