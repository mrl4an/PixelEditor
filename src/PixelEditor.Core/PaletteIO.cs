// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Globalization;

namespace PixelEditor.Core;

/// <summary>Palettes: .hex (one color per line, Lospec format) and .gpl (GIMP).</summary>
public static class PaletteIO
{
    public static List<uint> Load(string path)
    {
        var colors = new List<uint>();
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') && line.Length != 7 && line.Length != 4) continue;
            if (line.StartsWith("GIMP", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Name", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Columns", StringComparison.OrdinalIgnoreCase)) continue;

            if (Pixel.TryParseHex(line, out var c) && !Pixel.IsEmpty(c))
            {
                colors.Add(c);
                continue;
            }

            // gpl: "R G B  name"
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3 &&
                byte.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var r) &&
                byte.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var g) &&
                byte.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var b))
                colors.Add(Pixel.Rgb(r, g, b));
        }
        return colors.Distinct().ToList();
    }

    public static void Save(string path, IEnumerable<uint> colors)
    {
        var list = colors.Where(c => !Pixel.IsEmpty(c)).ToList();
        if (Path.GetExtension(path).Equals(".gpl", StringComparison.OrdinalIgnoreCase))
        {
            var lines = new List<string> { "GIMP Palette", $"Name: {Path.GetFileNameWithoutExtension(path)}", "Columns: 8", "#" };
            lines.AddRange(list.Select(c => $"{Pixel.R(c),3} {Pixel.G(c),3} {Pixel.B(c),3}\t{Pixel.ToHex(c)}"));
            File.WriteAllLines(path, lines);
        }
        else
        {
            File.WriteAllLines(path, list.Select(c => (c & 0xFFFFFFu).ToString("x6")));
        }
    }
}
