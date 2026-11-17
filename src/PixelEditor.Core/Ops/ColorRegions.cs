// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
namespace PixelEditor.Core.Ops;

public static class ColorRegions
{
    /// <summary>Repaints small blobs with a close color of a neighboring larger area.</summary>
    public static PixelImage Simplify(PixelImage source, int maxArea, int tolerance)
    {
        if (maxArea < 1 || tolerance is < 0 or > 255) throw new ArgumentOutOfRangeException();
        int[] labels = new int[source.Pixels.Length];
        int[] queue = new int[source.Pixels.Length];
        var sizes = new List<int> { 0 };
        var colors = new List<uint> { Pixel.Empty };
        var neighbors = Cleanup.Neighbors(diagonal: false);

        for (int start = 0; start < labels.Length; start++)
        {
            uint color = source.Pixels[start];
            if (labels[start] != 0 || Pixel.IsEmpty(color)) continue;
            int label = sizes.Count;
            int count = 1;
            queue[0] = start;
            labels[start] = label;
            for (int head = 0; head < count; head++)
            {
                int pixel = queue[head];
                foreach (var (dx, dy) in neighbors)
                {
                    int x = pixel % source.Width + dx, y = pixel / source.Width + dy;
                    if (!source.InBounds(x, y)) continue;
                    int n = y * source.Width + x;
                    if (labels[n] != 0 || source.Pixels[n] != color) continue;
                    labels[n] = label;
                    queue[count++] = n;
                }
            }
            sizes.Add(count);
            colors.Add(color);
        }

        var contacts = new Dictionary<int, int>?[sizes.Count];
        for (int i = 0; i < labels.Length; i++)
        {
            int region = labels[i];
            if (region == 0 || sizes[region] > maxArea) continue;
            foreach (var (dx, dy) in neighbors)
            {
                int x = i % source.Width + dx, y = i / source.Width + dy;
                if (!source.InBounds(x, y)) continue;
                int other = labels[y * source.Width + x];
                if (sizes[other] <= sizes[region] || !SelectionMask.SimilarColor(colors[region], colors[other], tolerance)) continue;
                var votes = contacts[region] ??= new Dictionary<int, int>();
                votes[other] = votes.GetValueOrDefault(other) + 1;
            }
        }

        var replacements = new uint[sizes.Count];
        for (int i = 1; i < replacements.Length; i++)
            if (contacts[i] is { } votes)
                replacements[i] = colors[votes.OrderByDescending(v => v.Value)
                    .ThenBy(v => Quantizer.Distance(colors[i], colors[v.Key])).ThenBy(v => v.Key).First().Key];

        var result = source.Clone();
        for (int i = 0; i < labels.Length; i++)
        {
            uint color = replacements[labels[i]];
            if (!Pixel.IsEmpty(color))
                result.Pixels[i] = (result.Pixels[i] & 0xFF000000u) | (color & 0xFFFFFFu);
        }
        return result;
    }
}
