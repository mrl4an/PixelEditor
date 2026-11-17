// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
namespace PixelEditor.Core;

public readonly record struct ColorCount(uint Color, int Count);

public static class ColorStats
{
    /// <summary>All used colors and their pixel counts. Sorted with the most frequent first.</summary>
    public static List<ColorCount> Count(uint[] pixels)
    {
        var map = new Dictionary<uint, int>();
        foreach (var p in pixels)
        {
            if (Pixel.IsEmpty(p)) continue;
            map[p] = map.GetValueOrDefault(p) + 1;
        }
        return map.Select(kv => new ColorCount(kv.Key, kv.Value))
            .OrderByDescending(c => c.Count)
            .ThenBy(c => c.Color)
            .ToList();
    }

    public static List<ColorCount> Count(PixelImage image) => Count(image.Pixels);

    /// <summary>Global color replacement on unlocked layers as one undo action. Returns the number of pixels.</summary>
    public static int ReplaceColor(Document doc, uint from, uint to)
    {
        if (from == to) return 0;
        var actions = new List<IUndoAction>();
        int total = 0;
        foreach (var layer in doc.Layers)
        {
            if (layer.Locked) continue;
            var batch = new PixelBatch(layer);
            for (int i = 0; i < layer.Pixels.Length; i++)
            {
                if (layer.Pixels[i] == from)
                {
                    batch.Set(i % layer.Width, i / layer.Width, to);
                    total++;
                }
            }
            var a = batch.ToAction();
            if (a != null) actions.Add(a);
        }
        if (actions.Count > 0) doc.History.Push(new CompositeAction(actions));
        return total;
    }
}
