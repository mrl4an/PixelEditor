// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
namespace PixelEditor.Core.Ops;

public sealed record ImageDifference(int Recolored, int Added, int Removed, PixelImage Map);

public static class ImageComparison
{
    /// <summary>Compares pixels in the original canvas coordinates.</summary>
    public static ImageDifference Compare(PixelImage before, PixelImage after)
    {
        if (before.Width != after.Width || before.Height != after.Height)
            throw new ArgumentException("Images must have the same dimensions.");

        var map = new PixelImage(before.Width, before.Height);
        int recolored = 0, added = 0, removed = 0;
        for (int i = 0; i < map.Pixels.Length; i++)
        {
            uint original = before.Pixels[i], result = after.Pixels[i];
            if (original == result) continue;
            if (Pixel.IsEmpty(original))
            {
                if (Pixel.IsEmpty(result)) continue;
                added++;
                map.Pixels[i] = Pixel.Rgb(0, 170, 90);
            }
            else if (Pixel.IsEmpty(result))
            {
                removed++;
                map.Pixels[i] = Pixel.Rgb(230, 50, 65);
            }
            else
            {
                recolored++;
                map.Pixels[i] = Pixel.Rgb(255, 175, 0);
            }
        }
        return new ImageDifference(recolored, added, removed, map);
    }
}
