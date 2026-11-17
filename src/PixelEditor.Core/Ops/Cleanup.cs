// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
namespace PixelEditor.Core.Ops;

/// <summary>Cleanup of junk: single pixels, holes, rare colors.</summary>
public static class Cleanup
{
    private static readonly (int, int)[] N4 = [(1, 0), (-1, 0), (0, 1), (0, -1)];
    private static readonly (int, int)[] N8 = [(1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, 1), (1, -1), (-1, -1)];

    public static (int, int)[] Neighbors(bool diagonal) => diagonal ? N8 : N4;

    /// <summary>Removes connected islands of opaque pixels of size &lt;= maxSize.</summary>
    public static PixelImage RemoveIslands(PixelImage src, int maxSize, bool diagonal = true)
    {
        var dst = src.Clone();
        var seen = new bool[src.Pixels.Length];
        var nb = Neighbors(diagonal);
        var comp = new List<int>();
        var stack = new Stack<int>();

        for (int start = 0; start < src.Pixels.Length; start++)
        {
            if (seen[start] || Pixel.IsEmpty(src.Pixels[start])) continue;
            comp.Clear();
            stack.Push(start);
            seen[start] = true;
            while (stack.Count > 0)
            {
                int cur = stack.Pop();
                comp.Add(cur);
                int cx = cur % src.Width, cy = cur / src.Width;
                foreach (var (dx, dy) in nb)
                {
                    int nx = cx + dx, ny = cy + dy;
                    if (!src.InBounds(nx, ny)) continue;
                    int ni = ny * src.Width + nx;
                    if (seen[ni] || Pixel.IsEmpty(src.Pixels[ni])) continue;
                    seen[ni] = true;
                    stack.Push(ni);
                }
            }
            if (comp.Count <= maxSize)
                foreach (var i in comp) dst.Pixels[i] = Pixel.Empty;
        }
        return dst;
    }

    /// <summary>Paints empty areas not connected to the image edge (size &lt;= maxSize) with the neighbor color.</summary>
    public static PixelImage FillHoles(PixelImage src, int maxSize = int.MaxValue)
    {
        var dst = src.Clone();
        var seen = new bool[src.Pixels.Length];
        var stack = new Stack<int>();

        // mark the empty pixels reachable from the border
        void Flood(int start)
        {
            if (seen[start] || !Pixel.IsEmpty(src.Pixels[start])) return;
            seen[start] = true;
            stack.Push(start);
            while (stack.Count > 0)
            {
                int cur = stack.Pop();
                int cx = cur % src.Width, cy = cur / src.Width;
                foreach (var (dx, dy) in N4)
                {
                    int nx = cx + dx, ny = cy + dy;
                    if (!src.InBounds(nx, ny)) continue;
                    int ni = ny * src.Width + nx;
                    if (seen[ni] || !Pixel.IsEmpty(src.Pixels[ni])) continue;
                    seen[ni] = true;
                    stack.Push(ni);
                }
            }
        }
        for (int x = 0; x < src.Width; x++) { Flood(x); Flood((src.Height - 1) * src.Width + x); }
        for (int y = 0; y < src.Height; y++) { Flood(y * src.Width); Flood(y * src.Width + src.Width - 1); }

        var comp = new List<int>();
        for (int start = 0; start < src.Pixels.Length; start++)
        {
            if (seen[start] || !Pixel.IsEmpty(src.Pixels[start])) continue;
            comp.Clear();
            var borderColors = new Dictionary<uint, int>();
            seen[start] = true;
            stack.Push(start);
            while (stack.Count > 0)
            {
                int cur = stack.Pop();
                comp.Add(cur);
                int cx = cur % src.Width, cy = cur / src.Width;
                foreach (var (dx, dy) in N4)
                {
                    int nx = cx + dx, ny = cy + dy;
                    if (!src.InBounds(nx, ny)) continue;
                    int ni = ny * src.Width + nx;
                    uint p = src.Pixels[ni];
                    if (!Pixel.IsEmpty(p)) { borderColors[p] = borderColors.GetValueOrDefault(p) + 1; continue; }
                    if (seen[ni]) continue;
                    seen[ni] = true;
                    stack.Push(ni);
                }
            }
            if (comp.Count > maxSize || borderColors.Count == 0) continue;
            uint fill = borderColors.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).First().Key;
            foreach (var i in comp) dst.Pixels[i] = fill;
        }
        return dst;
    }

    /// <summary>Colors with fewer than minCount pixels are replaced by the nearest "normal" color.</summary>
    public static PixelImage MergeRareColors(PixelImage src, int minCount)
    {
        var counts = ColorStats.Count(src);
        var keep = counts.Where(c => c.Count >= minCount).Select(c => c.Color).ToList();
        if (keep.Count == 0) return src.Clone();

        var map = new Dictionary<uint, uint>();
        foreach (var c in counts)
            if (c.Count < minCount) map[c.Color] = Quantizer.Nearest(c.Color, keep);

        var dst = src.Clone();
        for (int i = 0; i < dst.Pixels.Length; i++)
            if (map.TryGetValue(dst.Pixels[i], out var to)) dst.Pixels[i] = to;
        return dst;
    }

    public static PixelImage ReplaceColor(PixelImage src, uint from, uint to)
    {
        var dst = src.Clone();
        for (int i = 0; i < dst.Pixels.Length; i++)
            if (dst.Pixels[i] == from) dst.Pixels[i] = to;
        return dst;
    }
}
