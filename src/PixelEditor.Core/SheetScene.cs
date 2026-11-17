// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Text.Json;
using System.Text.Json.Nodes;
using PixelEditor.Core.Ops;

namespace PixelEditor.Core;

/// <summary>The rendered sheet and what describes it: frame size, row and frame names and delays (seconds).</summary>
public sealed record SheetResult(PixelImage Image, int FrameWidth, int FrameHeight, int Columns, string[] RowNames, string?[] FrameNames, double[][] Delays,
    bool[][]? ExplicitDelays = null)
{
    // ExplicitDelays[row][col]: the answer itself gave that delay (frame, row or sheet level); false: the default 0.15 was assumed.
    // SheetLayoutSpec.Complete replaces only the assumed ones with the delays the user chose.

    public int Rows => RowNames.Length;

    /// <summary>A short JSON description next to the PNG: for loaders, for the AI and for import into the editor.</summary>
    public string ToMetaJson()
    {
        var root = new JsonObject
        {
            ["frameSize"] = new JsonArray(FrameWidth, FrameHeight),
            ["columns"] = Columns,
            ["rows"] = new JsonArray(RowNames.Select(n => (JsonNode?)JsonValue.Create(n)).ToArray()),
            ["frameNames"] = new JsonArray(FrameNames.Select(n => (JsonNode?)JsonValue.Create(n ?? "")).ToArray()),
            ["delays"] = new JsonArray(Delays.Select(r => (JsonNode?)new JsonArray(r.Select(d => (JsonNode?)JsonValue.Create(Math.Round(d, 4))).ToArray())).ToArray()),
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}

/// <summary>
/// A sprite sheet built from primitives with custom frames, described by a short JSON (an extension of <see cref="Scene"/>):
/// <code>
/// { "frameSize": [24, 32],
///   "palette": { "skin": "#F1C27D" },            // shared by every frame
///   "outline": "#1B1B1B",                        // optional, per frame
///   "mirror": "x",                               // optional, as in a scene
///   "delay": 0.15,                               // default frame delay in seconds
///   "base": [ ...items drawn first in EVERY frame (for example the body) ],
///   "rows": [
///     { "name": "S", "base": [ ...items for this row only ],
///       "frames": [
///         { "name": "stand", "delay": 0.5, "items": [ ...items of this frame (for example the legs) ] },
///         { "name": "step1", "items": [ ... ], "offset": [0, -1] },     // offset shifts the whole frame (a bob)
///         { "name": "step2", "copyOf": "stand" } ] },                    // the same picture as another frame of the row
///     { "name": "W", "mirrorOf": "E" },                                  // every frame of row E flipped left-right
///     { "name": "N", "copyOf": "S" } ] }                                 // a copy of another row
/// </code>
/// Rows are directions or animations (S, N, E, W is the RSI order), columns are frames. Frame names and delays are taken for each column from the first row that sets them.
/// A frame is drawn as base + row base + the frame items, so frames only need to list what differs.
/// </summary>
public static class SheetScene
{
    private const int MaxFrame = 256, MaxRows = 16, MaxColumns = 32, MaxSheet = 4096;

    public static SheetResult Render(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        return Render(doc.RootElement);
    }

    public static SheetResult Render(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) throw new ArgumentException(CoreText.T("Лист должен быть объектом {frameSize, rows}"));
        var node = JsonNode.Parse(root.GetRawText())!.AsObject();

        var size = node["frameSize"] as JsonArray;
        if (size is not { Count: 2 }) throw new ArgumentException(CoreText.T("Не задан размер кадра: \"frameSize\": [ширина, высота]"));
        int fw = size[0]!.GetValue<int>(), fh = size[1]!.GetValue<int>();
        if (fw < 1 || fh < 1 || fw > MaxFrame || fh > MaxFrame) throw new ArgumentException(CoreText.F("Размер кадра {0}×{1} вне допустимого (1…{2})", fw, fh, MaxFrame));

        var rowsNode = node["rows"] as JsonArray;
        if (rowsNode == null || rowsNode.Count == 0) throw new ArgumentException(CoreText.T("Нет списка rows: [{\"name\":\"S\",\"frames\":[{\"items\":[...]}]}, ...]"));
        if (rowsNode.Count > MaxRows) throw new ArgumentException(CoreText.F("Строк не больше {0}", MaxRows));

        double defaultDelay = node["delay"]?.GetValue<double>() ?? 0.15;
        var baseItems = node["base"] as JsonArray ?? [];
        var rows = new List<RowData>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rn in rowsNode)
        {
            if (rn is not JsonObject ro) throw new ArgumentException(CoreText.T("Строка листа должна быть объектом"));
            string name = ro["name"]?.GetValue<string>() ?? throw new ArgumentException(CoreText.F("У строки {0} нет name", rows.Count + 1));
            if (!names.Add(name)) throw new ArgumentException(CoreText.F("Название строки «{0}» повторяется", name));
            rows.Add(new RowData(name, ro));
        }

        // pass 1: rows with their own frames
        foreach (var row in rows.Where(r => r.Json["frames"] is JsonArray))
        {
            var frames = (JsonArray)row.Json["frames"]!;
            if (frames.Count == 0 || frames.Count > MaxColumns) throw new ArgumentException(CoreText.F("Строка «{0}»: кадров от 1 до {1}", row.Name, MaxColumns));
            var rowBase = row.Json["base"] as JsonArray ?? [];
            for (int i = 0; i < frames.Count; i++)
            {
                if (frames[i] is not JsonObject fo) throw new ArgumentException(CoreText.F("Строка «{0}», кадр {1}: нужен объект", row.Name, i + 1));
                string? fname = fo["name"]?.GetValue<string>();
                double? given = fo["delay"]?.GetValue<double>() ?? row.Json["delay"]?.GetValue<double>() ?? node["delay"]?.GetValue<double>();
                double delay = given ?? defaultDelay;
                PixelImage image;
                if (fo["copyOf"]?.GetValue<string>() is { } other)
                {
                    int src = row.FrameNames.FindIndex(n => string.Equals(n, other, StringComparison.OrdinalIgnoreCase));
                    if (src < 0) throw new ArgumentException(CoreText.F("Строка «{0}», кадр {1}: нет кадра «{2}» раньше этого", row.Name, i + 1, other));
                    image = row.Images[src].Clone();
                }
                else
                {
                    try { image = DrawFrame(node, baseItems, rowBase, fo["items"] as JsonArray ?? [], fw, fh); }
                    catch (ArgumentException ex) { throw new ArgumentException(CoreText.F("Строка «{0}», кадр {1}: {2}", row.Name, i + 1, ex.Message)); }
                }
                if (fo["offset"] is JsonArray { Count: 2 } off)
                    image = Transform.Shift(image, off[0]!.GetValue<int>(), off[1]!.GetValue<int>(), false);
                row.Images.Add(image);
                row.FrameNames.Add(fname);
                row.Delays.Add(delay);
                row.Explicit.Add(given != null);
            }
        }

        // pass 2: rows that copy or mirror another row
        foreach (var row in rows.Where(r => r.Json["frames"] is not JsonArray))
        {
            string? mirrorOf = row.Json["mirrorOf"]?.GetValue<string>(), copyOf = row.Json["copyOf"]?.GetValue<string>();
            string? target = mirrorOf ?? copyOf;
            if (target == null) throw new ArgumentException(CoreText.F("Строка «{0}»: нужен список frames, либо mirrorOf, либо copyOf", row.Name));
            var other = rows.FirstOrDefault(r => string.Equals(r.Name, target, StringComparison.OrdinalIgnoreCase))
                        ?? throw new ArgumentException(CoreText.F("Строка «{0}»: нет строки «{1}»", row.Name, target));
            if (other.Json["frames"] is not JsonArray)
                throw new ArgumentException(CoreText.F("Строка «{0}»: «{1}» должна иметь собственные кадры (copyOf и mirrorOf не цепочкой)", row.Name, target));
            for (int i = 0; i < other.Images.Count; i++)
            {
                row.Images.Add(mirrorOf != null ? Transform.FlipH(other.Images[i]) : other.Images[i].Clone());
                row.FrameNames.Add(other.FrameNames[i]);
                row.Delays.Add(other.Delays[i]);
                row.Explicit.Add(other.Explicit[i]);
            }
        }

        int columns = rows.Max(r => r.Images.Count);
        if ((long)fw * columns > MaxSheet || (long)fh * rows.Count > MaxSheet)
            throw new ArgumentException(CoreText.F("Лист {0}×{1} больше {2}", fw * columns, fh * rows.Count, MaxSheet));

        var sheet = new PixelImage(fw * columns, fh * rows.Count);
        var frameNames = new string?[columns];
        var delays = new double[rows.Count][];
        var explicitDelays = new bool[rows.Count][];
        for (int r = 0; r < rows.Count; r++)
        {
            delays[r] = new double[columns];
            explicitDelays[r] = new bool[columns];
            for (int c = 0; c < columns; c++)
            {
                bool has = c < rows[r].Images.Count;
                delays[r][c] = has ? rows[r].Delays[c] : defaultDelay;
                explicitDelays[r][c] = has && rows[r].Explicit[c];
                if (!has) continue;
                frameNames[c] ??= rows[r].FrameNames[c];
                var img = rows[r].Images[c];
                for (int y = 0; y < fh; y++)
                    for (int x = 0; x < fw; x++)
                        sheet.Set(c * fw + x, r * fh + y, img.Get(x, y));
            }
        }
        return new SheetResult(sheet, fw, fh, columns, rows.Select(r => r.Name).ToArray(), frameNames, delays, explicitDelays);
    }

    private sealed class RowData(string name, JsonObject json)
    {
        public string Name { get; } = name;
        public JsonObject Json { get; } = json;
        public List<PixelImage> Images { get; } = new();
        public List<string?> FrameNames { get; } = new();
        public List<double> Delays { get; } = new();
        public List<bool> Explicit { get; } = new();
    }

    /// <summary>One frame is a scene: the shared palette, mirror and outline plus base + row base + the frame items.</summary>
    private static PixelImage DrawFrame(JsonObject sheet, JsonArray baseItems, JsonArray rowBase, JsonArray items, int w, int h)
    {
        var scene = new JsonObject { ["size"] = new JsonArray(w, h) };
        foreach (var key in new[] { "palette", "mirror", "outline", "background" })
            if (sheet[key] is { } v) scene[key] = v.DeepClone();
        var all = new JsonArray();
        foreach (var group in new[] { baseItems, rowBase, items })
            foreach (var item in group) all.Add(item?.DeepClone());
        scene["items"] = all;
        using var doc = JsonDocument.Parse(scene.ToJsonString());
        return Scene.Render(doc.RootElement);
    }
}
