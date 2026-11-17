// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
namespace PixelEditor.Core.Ops;

public enum OutlineMode { Outside, Inside, Both }

public static class Outline
{
    /// <summary>
    /// Colored outline (sel-out): instead of one color each outline pixel gets a darker and slightly more saturated shade
    /// of the neighboring drawing pixel. The outline looks softer than a black frame and blends with the subject color. darken 0…1 (a fraction of brightness).
    /// </summary>
    private static PixelImage Shaded(PixelImage src, OutlineMode mode, bool diagonal, int thickness, double darken)
    {
        darken = Math.Clamp(darken, 0.05, 0.95);
        uint Shade(uint c)
        {
            var (h, s, v) = Gradient.ToHsv(c);
            return Gradient.FromHsv(h, Math.Min(1, s * 1.1 + 0.05), v * (1 - darken));
        }
        var cur = src.Clone();
        var nb = Cleanup.Neighbors(diagonal);
        for (int pass = 0; pass < Math.Max(1, thickness); pass++)
        {
            var next = cur.Clone();
            for (int y = 0; y < cur.Height; y++)
                for (int x = 0; x < cur.Width; x++)
                {
                    uint self = cur.Pixels[y * cur.Width + x];
                    bool empty = Pixel.IsEmpty(self);
                    var around = new List<uint>();
                    bool touchesEmpty = false;
                    foreach (var (dx, dy) in nb)
                    {
                        uint p = cur.Get(x + dx, y + dy);
                        if (Pixel.IsEmpty(p)) touchesEmpty = true; else around.Add(p);
                    }
                    if (empty && around.Count > 0 && mode != OutlineMode.Inside)
                        next.Pixels[y * cur.Width + x] = Shade(around.GroupBy(c => c).OrderByDescending(g => g.Count()).First().Key);
                    else if (!empty && touchesEmpty && mode != OutlineMode.Outside)
                        next.Pixels[y * cur.Width + x] = Shade(self);
                }
            cur = next;
            if (mode == OutlineMode.Inside) break;
            mode = mode == OutlineMode.Both ? OutlineMode.Outside : mode;
        }
        return cur;
    }

    /// <summary>
    /// Sprite stroke. Outside paints empty pixels next to opaque ones (the canvas must be grown: see Transform.Pad),
    /// Inside paints the edge of the shape itself. diagonal=false gives a 4-connected (thinner and "cleaner") outline.
    /// </summary>
    public static PixelImage Apply(PixelImage src, uint color, OutlineMode mode, bool diagonal, int thickness = 1, double? shadeDarken = null)
    {
        if (shadeDarken is double dk) return Shaded(src, mode, diagonal, thickness, dk);
        var cur = src.Clone();
        var nb = Cleanup.Neighbors(diagonal);
        for (int pass = 0; pass < Math.Max(1, thickness); pass++)
        {
            var next = cur.Clone();
            for (int y = 0; y < cur.Height; y++)
                for (int x = 0; x < cur.Width; x++)
                {
                    bool empty = Pixel.IsEmpty(cur.Pixels[y * cur.Width + x]);
                    bool touchesOpaque = false, touchesEmpty = false;
                    foreach (var (dx, dy) in nb)
                    {
                        // beyond the frame counts as empty
                        uint p = cur.Get(x + dx, y + dy);
                        if (Pixel.IsEmpty(p)) touchesEmpty = true; else touchesOpaque = true;
                    }
                    if (empty && touchesOpaque && mode != OutlineMode.Inside)
                        next.Pixels[y * cur.Width + x] = color;
                    else if (!empty && touchesEmpty && mode != OutlineMode.Outside)
                        next.Pixels[y * cur.Width + x] = color;
                }
            cur = next;
            // for the following passes the inner outline is not recomputed
            if (mode == OutlineMode.Inside) break;
            mode = mode == OutlineMode.Both ? OutlineMode.Outside : mode;
        }
        return cur;
    }
}
