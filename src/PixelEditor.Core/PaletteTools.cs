// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
namespace PixelEditor.Core;

public static class PaletteTools
{
    public static double Luma(uint c) => 0.299 * Pixel.R(c) + 0.587 * Pixel.G(c) + 0.114 * Pixel.B(c);

    public static (double Hue, double Sat) HueSat(uint c)
    {
        double r = Pixel.R(c) / 255.0, g = Pixel.G(c) / 255.0, b = Pixel.B(c) / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        if (d == 0) return (0, 0);
        double h = max == r ? ((g - b) / d) % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        h *= 60;
        if (h < 0) h += 360;
        return (h, max == 0 ? 0 : d / max);
    }

    public static List<uint> SortByLuminance(IEnumerable<uint> colors) =>
        colors.OrderBy(Luma).ThenBy(c => c).ToList();

    /// <summary>By hue; grays (low saturation) come first, by brightness.</summary>
    public static List<uint> SortByHue(IEnumerable<uint> colors)
    {
        var all = colors.ToList();
        var grays = all.Where(c => HueSat(c).Sat < 0.12).OrderBy(Luma);
        var chroma = all.Where(c => HueSat(c).Sat >= 0.12).OrderBy(c => Math.Round(HueSat(c).Hue / 15)).ThenBy(Luma);
        return grays.Concat(chroma).ToList();
    }

    /// <summary>
    /// A five-step shade ramp around a color, darkest to lightest: two darker steps, the color itself, two lighter ones.
    /// Pixel-art hue shift: shadows drift toward blue/purple and gain saturation, highlights drift toward yellow and lose it.
    /// Neutral greys stay neutral. Colors that coincide (black, white) are dropped, so the result can be shorter than five.
    /// </summary>
    public static List<uint> ShadeRamp(uint color)
    {
        var ramp = new List<uint>();
        for (int k = -2; k <= 2; k++) ramp.Add(ShadeStep(color, k));
        // keep the order by brightness and drop repeats
        var result = new List<uint>();
        foreach (var c in ramp.OrderBy(Luma).ThenBy(c => c))
            if (!result.Contains(c)) result.Add(c);
        return result;
    }

    /// <summary>
    /// One step of the shade ramp: k = 0 is the color itself, k &lt; 0 darker (hue toward blue/purple, more saturated), k &gt; 0 lighter
    /// (hue toward yellow, less saturated). lightScale and shadowScale multiply the size of a step (1 = the ShadeRamp steps).
    /// </summary>
    public static uint ShadeStep(uint color, int k, double lightScale = 1, double shadowScale = 1)
    {
        var plain = Pixel.Rgb(Pixel.R(color), Pixel.G(color), Pixel.B(color));
        if (k == 0) return plain;
        var (h, s, v) = Hsv.FromRgb(color);
        double steps = Math.Abs(k) * (k < 0 ? shadowScale : lightScale);
        double nh = h, ns = s;
        if (s >= 0.04) // neutral greys stay neutral
        {
            nh = h + Math.Clamp(Shortest(h, k < 0 ? 255 : 55), -12 * steps, 12 * steps);
            ns = k < 0 ? s + 0.07 * steps : s - 0.10 * steps;
            ns = Math.Clamp(ns, 0.05, 1);
        }
        double nv = k < 0 ? v - 0.15 * steps : v + 0.13 * steps;
        return Hsv.ToRgb(nh, ns, Math.Clamp(nv, 0, 1));
    }

    /// <summary>Signed shortest angular distance from one hue to another, in degrees.</summary>
    private static double Shortest(double from, double to)
    {
        double d = (to - from) % 360;
        if (d > 180) d -= 360;
        if (d < -180) d += 360;
        return d;
    }

    /// <summary>Keeps in the palette only the colors that really occur in the picture.</summary>
    public static List<uint> RemoveUnused(IEnumerable<uint> palette, PixelImage image)
    {
        var used = ColorStats.Count(image).Select(c => c.Color).ToHashSet();
        return palette.Where(used.Contains).ToList();
    }
}
