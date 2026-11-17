// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
namespace PixelEditor.Core;

/// <summary>A color in the Lab space, already multiplied by <see cref="ColorMetric.Scale"/>, so the plain Euclidean distance is in "tolerance units".</summary>
public readonly record struct LabColor(float L, float A, float B);

/// <summary>
/// The perceptual color distance used by the magic wand and background removal. It is the CIE76 difference (Euclidean distance in Lab) multiplied
/// by <see cref="Scale"/> = 2.5, which makes one unit about one step of an RGB channel on mid grays: a tolerance of 24 selects roughly what the old
/// "largest channel difference" rule selected on plain colors, but it no longer depends on the hue (a blue and a yellow step of equal visibility count the same).
/// A tolerance of 0 always means an exactly equal color.
/// </summary>
public static class ColorMetric
{
    public const double Scale = 2.5;

    private static readonly double[] Linear = BuildLinear();

    private static double[] BuildLinear()
    {
        var t = new double[256];
        for (int i = 0; i < 256; i++)
        {
            double v = i / 255.0;
            t[i] = v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
        return t;
    }

    private static double F(double t) => t > 0.008856 ? Math.Cbrt(t) : 7.787 * t + 16.0 / 116.0;

    /// <summary>The scaled Lab color of a pixel (alpha is ignored).</summary>
    public static LabColor ToLab(uint c)
    {
        double r = Linear[Pixel.R(c)], g = Linear[Pixel.G(c)], b = Linear[Pixel.B(c)];
        double x = (0.4124 * r + 0.3576 * g + 0.1805 * b) / 0.95047;
        double y = 0.2126 * r + 0.7152 * g + 0.0722 * b;
        double z = (0.0193 * r + 0.1192 * g + 0.9505 * b) / 1.08883;
        double fx = F(x), fy = F(y), fz = F(z);
        return new LabColor((float)((116 * fy - 16) * Scale), (float)(500 * (fx - fy) * Scale), (float)(200 * (fy - fz) * Scale));
    }

    /// <summary>Lab colors of all pixels (transparent pixels get a zero color: look at the pixel itself to tell them apart).</summary>
    public static LabColor[] ToLab(uint[] pixels)
    {
        var res = new LabColor[pixels.Length];
        for (int i = 0; i < pixels.Length; i++) if (!Pixel.IsEmpty(pixels[i])) res[i] = ToLab(pixels[i]);
        return res;
    }

    public static float Distance(in LabColor a, in LabColor b)
    {
        float dl = a.L - b.L, da = a.A - b.A, db = a.B - b.B;
        return MathF.Sqrt(dl * dl + da * da + db * db);
    }

    /// <summary>The distance between two opaque colors in tolerance units.</summary>
    public static double Distance(uint a, uint b) => Distance(ToLab(a), ToLab(b));

    /// <summary>
    /// Color match: transparent only with transparent; tolerance 0 or less means exactly the same RGB;
    /// otherwise the perceptual distance must not exceed the tolerance.
    /// </summary>
    public static bool Similar(uint a, uint b, double tolerance)
    {
        bool ea = Pixel.IsEmpty(a), eb = Pixel.IsEmpty(b);
        if (ea || eb) return ea && eb;
        if (((a ^ b) & 0xFFFFFFu) == 0) return true;
        return tolerance > 0 && Distance(a, b) <= tolerance;
    }
}
