// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
namespace PixelEditor.Core.Ops;

public static partial class Background
{
    /// <summary>
    /// Background removal by a simplified copy. Painted or textured pictures confuse a color model (brush strokes, grain, soft transitions), but once the
    /// colors are reduced the background becomes a handful of exact colors. The mask is therefore computed on a copy that was optionally denoised (median,
    /// <paramref name="denoise"/> = radius) and quantized to <paramref name="colors"/> colors, and then applied to the untouched ORIGINAL pixels:
    /// whatever the copy lost to transparency is cleared in the original, everything else keeps its exact color.
    /// <paramref name="cut"/> computes the cut of the simplified copy (any removal: removeBackground, cutout).
    /// enclosed: "off", "auto" or "all": background-colored patches that the subject encloses (sky between the limbs of a rider, inside reins) are erased too,
    /// when their colors in the copy are colors that were mostly removed (a color shared by sky and subject is not touched) and the patch has at least
    /// <paramref name="enclosedMin"/> pixels (0: automatic, about 1/1500 of the picture, at least 8; "all" erases patches of any size).
    /// defringe: after the cut the antialiased rim of the original is cleaned (skipped for crisp pixel art).
    /// </summary>
    public static PixelImage RemoveViaSimplified(PixelImage src, int colors, int denoise, string enclosed, int enclosedMin, bool defringe, Func<PixelImage, PixelImage> cut)
    {
        int w = src.Width, h = src.Height;
        if (w == 0 || h == 0) return src.Clone();
        var simple = Simplify(src, colors, denoise);
        var cutSimple = cut(simple);

        var removed = new bool[w * h];
        for (int i = 0; i < removed.Length; i++)
            removed[i] = !Pixel.IsEmpty(src.Pixels[i]) && Pixel.IsEmpty(cutSimple.Pixels[i]);

        string mode = (enclosed ?? "auto").Trim().ToLowerInvariant();
        if (mode != "off") EraseEnclosedColors(simple, removed, w, h, mode == "all" ? 1 : enclosedMin > 0 ? enclosedMin : Math.Max(8, w * h / 1500));

        var dst = src.Clone();
        for (int i = 0; i < removed.Length; i++) if (removed[i]) dst.Pixels[i] = Pixel.Empty;
        if (defringe && !DistinctColorsAtMost(src, 24)) dst = CleanRim(src, dst, 10);
        return dst;
    }

    /// <summary>The copy the mask is computed on: median (radius 0 is off), then the colors reduced to <paramref name="colors"/> in Lab. Transparency is kept.</summary>
    public static PixelImage Simplify(PixelImage src, int colors, int denoise)
    {
        var img = denoise > 0 ? Filters.Median(src, Math.Clamp(denoise, 1, 4)) : src;
        colors = Math.Clamp(colors, 2, 64);
        var palette = PerceptualQuantizer.BuildPalette(img, colors);
        return palette.Count == 0 ? img.Clone() : PerceptualQuantizer.Apply(img, palette, false);
    }

    // Patches of an exactly-background color of the simplified copy that the fill did not reach: the colors of which most pixels were removed count as background.
    private static void EraseEnclosedColors(PixelImage simple, bool[] removed, int w, int h, int minArea)
    {
        var total = new Dictionary<uint, int>();
        var gone = new Dictionary<uint, int>();
        for (int i = 0; i < removed.Length; i++)
        {
            if (Pixel.IsEmpty(simple.Pixels[i])) continue;
            uint c = simple.Pixels[i] | 0xFF000000u;
            total[c] = total.GetValueOrDefault(c) + 1;
            if (removed[i]) gone[c] = gone.GetValueOrDefault(c) + 1;
        }
        var bgColors = gone.Where(kv => kv.Value * 2 >= total[kv.Key]).Select(kv => kv.Key).ToHashSet();
        if (bgColors.Count == 0) return;

        var candidate = new bool[w * h];
        for (int i = 0; i < candidate.Length; i++)
            candidate[i] = !removed[i] && !Pixel.IsEmpty(simple.Pixels[i]) && bgColors.Contains(simple.Pixels[i] | 0xFF000000u);
        foreach (var comp in Components(candidate, removed, w, h))
            if (comp.Count >= minArea)
                foreach (int i in comp) removed[i] = true;
    }
}
