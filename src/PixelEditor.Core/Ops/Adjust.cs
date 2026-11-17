// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
namespace PixelEditor.Core.Ops;

/// <summary>Color correction before reducing to a palette: after downscaling, photo colors are usually dull, and contrast and saturation make pixel art "juicier".</summary>
public static class Adjust
{
    /// <summary>
    /// brightness: -100..100 (brightness shift), contrast: -100..100 (stretch around the midpoint), saturation: -100..100 (-100 gives gray).
    /// Transparent pixels do not change.
    /// </summary>
    public static PixelImage Apply(PixelImage src, double brightness, double contrast, double saturation)
    {
        var dst = src.Clone();
        double add = brightness / 100.0 * 255.0;
        double k = (100.0 + Math.Clamp(contrast, -100, 100)) / 100.0;
        k = k * k; // a smoother scale: +100 gives ×4
        double sat = 1.0 + Math.Clamp(saturation, -100, 100) / 100.0;

        for (int i = 0; i < dst.Pixels.Length; i++)
        {
            uint p = dst.Pixels[i];
            if (Pixel.IsEmpty(p)) continue;
            double r = (Pixel.R(p) - 128) * k + 128 + add;
            double g = (Pixel.G(p) - 128) * k + 128 + add;
            double b = (Pixel.B(p) - 128) * k + 128 + add;
            if (Math.Abs(sat - 1.0) > 1e-6)
            {
                double gray = 0.299 * r + 0.587 * g + 0.114 * b;
                r = gray + (r - gray) * sat;
                g = gray + (g - gray) * sat;
                b = gray + (b - gray) * sat;
            }
            dst.Pixels[i] = Pixel.Rgb(Clamp(r), Clamp(g), Clamp(b));
        }
        return dst;
    }

    private static byte Clamp(double v) => (byte)Math.Round(Math.Clamp(v, 0, 255));
}
