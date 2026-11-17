// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
namespace PixelEditor.Core.Ops;

/// <summary>Background removal and edge cleanup after cutting out (halos, dots, gaps at the join of background and drawing).</summary>
public static partial class Background
{
    /// <summary>The background color from the most frequent color among corner and edge pixels.</summary>
    public static uint GuessBackground(PixelImage src)
    {
        var edge = new List<uint>();
        for (int x = 0; x < src.Width; x++) { edge.Add(src.Pixels[x]); edge.Add(src.Pixels[(src.Height - 1) * src.Width + x]); }
        for (int y = 0; y < src.Height; y++) { edge.Add(src.Pixels[y * src.Width]); edge.Add(src.Pixels[y * src.Width + src.Width - 1]); }
        return edge.GroupBy(c => c).OrderByDescending(g => g.Count()).First().Key;
    }

    /// <summary>
    /// Edge cleanup after background removal, three steps in order:
    /// 1) matte (optional): edge pixels of the drawing similar to the backing color (a black or white halo) are erased, passes layers deep;
    /// 2) specks: lone pixels (almost without neighbors) are erased;
    /// 3) fillGaps: transparent gaps and "holes" in the drawing edge with at least minNeighbors opaque neighbors out of 8 are painted with the most frequent neighbor color.
    /// </summary>
    public static PixelImage CleanEdges(PixelImage src, uint? matte, int tolerance, int passes, bool specks, bool fillGaps, int minNeighbors)
    {
        var img = src.Clone();
        int w = img.Width, h = img.Height;
        bool Opaque(PixelImage i, int x, int y) => x >= 0 && y >= 0 && x < w && y < h && !Pixel.IsEmpty(i.Pixels[y * w + x]);
        int OpaqueNeighbors(PixelImage i, int x, int y)
        {
            int n = 0;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if ((dx != 0 || dy != 0) && Opaque(i, x + dx, y + dy)) n++;
            return n;
        }

        if (matte is uint m)
            for (int pass = 0; pass < Math.Max(1, passes); pass++)
            {
                var next = img.Clone();
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        if (!Opaque(img, x, y) || !SelectionMask.SimilarColor(img.Pixels[y * w + x], m, tolerance)) continue;
                        bool onEdge = !Opaque(img, x - 1, y) || !Opaque(img, x + 1, y) || !Opaque(img, x, y - 1) || !Opaque(img, x, y + 1);
                        if (onEdge) next.Pixels[y * w + x] = Pixel.Empty;
                    }
                img = next;
            }

        if (specks)
        {
            var next = img.Clone();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (Opaque(img, x, y) && OpaqueNeighbors(img, x, y) <= 1) next.Pixels[y * w + x] = Pixel.Empty;
            img = next;
        }

        if (fillGaps)
        {
            minNeighbors = Math.Clamp(minNeighbors, 3, 8);
            var next = img.Clone();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (Opaque(img, x, y) || OpaqueNeighbors(img, x, y) < minNeighbors) continue;
                    var around = new List<uint>();
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                            if ((dx != 0 || dy != 0) && Opaque(img, x + dx, y + dy)) around.Add(img.Pixels[(y + dy) * w + x + dx]);
                    next.Pixels[y * w + x] = around.GroupBy(c => c).OrderByDescending(g => g.Count()).First().Key;
                }
            img = next;
        }
        return img;
    }
}

/// <summary>Removal of a sprite outline (stroke), including a ragged one (of varying thickness and shade).</summary>
public static class OutlineRemoval
{
    /// <summary>
    /// Removes the outer outline. The outline is the edge pixels of the drawing (next to transparency) similar to the outline color.
    /// color: the outline color (null: the most frequent color among edge pixels). tolerance: per-channel tolerance.
    /// dark: treat any edge pixels darker than maxLuma as the outline (for a stroke of several dark shades).
    /// passes: how many layers to peel in a row (an outline 2-3 pixels thick or ragged takes several passes; it stops when no suitable edge pixels are left).
    /// diagonal: also count pixels touching transparency diagonally as the edge.
    /// </summary>
    public static PixelImage Remove(PixelImage src, uint? color, int tolerance, bool dark, int maxLuma, int passes, bool diagonal)
    {
        var img = src.Clone();
        int w = img.Width, h = img.Height;
        bool Opaque(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && !Pixel.IsEmpty(img.Pixels[y * w + x]);
        bool IsEdge(int x, int y)
        {
            foreach (var (dx, dy) in Cleanup.Neighbors(diagonal))
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= w || ny >= h || Pixel.IsEmpty(img.Pixels[ny * w + nx])) return true;
            }
            return false;
        }

        uint target = color ?? 0;
        if (color == null && !dark)
        {
            var edge = new List<uint>();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (Opaque(x, y) && IsEdge(x, y)) edge.Add(img.Pixels[y * w + x]);
            if (edge.Count == 0) return img;
            target = edge.GroupBy(c => c).OrderByDescending(g => g.Count()).First().Key;
        }

        bool Matches(uint c) => dark ? Luma(c) <= maxLuma : SelectionMask.SimilarColor(c, target, tolerance);

        for (int pass = 0; pass < Math.Clamp(passes, 1, 16); pass++)
        {
            var kill = new List<int>();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (Opaque(x, y) && IsEdge(x, y) && Matches(img.Pixels[y * w + x])) kill.Add(y * w + x);
            if (kill.Count == 0) break;
            foreach (var i in kill) img.Pixels[i] = Pixel.Empty;
        }
        return img;
    }

    private static int Luma(uint c) => (Pixel.R(c) * 299 + Pixel.G(c) * 587 + Pixel.B(c) * 114) / 1000;
}
