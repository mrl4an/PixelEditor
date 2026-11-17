// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PixelEditor.Core;

/// <summary>
/// Binding of a document to an RSI state (Robust Station Images format, Space Station 14): the document is a sheet of one state.
/// Sheet rows are directions in RSI order (S, N, E, W, SE, SW, NE, NW), columns are frames. Such a sheet matches the state PNG inside the .rsi exactly.
/// </summary>
public sealed record RsiBinding(string Folder, string State, int Directions, int Frames, int FrameWidth, int FrameHeight, double[][] Delays, string[]? Rows = null, string[]? FrameNames = null)
{
    /// <summary>Frame names (stand, step 1...) for zone captions and frame names on export; null: just numbers.</summary>
    public string? FrameName(int frame) => FrameNames is { Length: > 0 } n && frame < n.Length && !string.IsNullOrWhiteSpace(n[frame]) ? n[frame] : null;

    /// <summary>Sheet row names: your own (for an animation sheet for Unity and Godot) or the standard RSI directions.</summary>
    public IReadOnlyList<string> RowLabels => Rows is { Length: > 0 } r && r.Length == Directions ? r : RsiLayout.DirectionNames(Directions);

    /// <summary>true if the sheet is bound to an RSI folder (otherwise it is an ordinary animation sheet without meta.json).</summary>
    public bool IsRsi => !string.IsNullOrEmpty(Folder);

    public int SheetWidth => FrameWidth * Frames;
    public int SheetHeight => FrameHeight * Directions;
}

/// <summary>An RSI state in meta.json: name, number of directions (1, 4 or 8), number of frames and delays (seconds) per direction and frame.</summary>
public sealed class RsiState
{
    public string Name { get; set; } = "state";
    public int Directions { get; set; } = 1;
    public int Frames { get; set; } = 1;

    /// <summary>Frame delays in seconds: [direction][frame].</summary>
    public double[][] Delays { get; set; } = [[1.0]];

    public bool IsAnimated => Frames > 1;

    /// <summary>The original JSON object of this state (all fields, including unknown ones such as flags); null for a state created in code.</summary>
    internal JsonObject? Source { get; set; }

    /// <summary>true once the state was set through Upsert: only then are its known fields rewritten, otherwise the original JSON is kept as is.</summary>
    internal bool Touched { get; set; }

    public static double[][] UniformDelays(int directions, int frames, double delay) =>
        Enumerable.Range(0, directions).Select(_ => Enumerable.Repeat(delay, frames).ToArray()).ToArray();
}

/// <summary>meta.json: version, license, copyright, frame size and the list of states.</summary>
public sealed class RsiMeta
{
    public const int SupportedVersion = 1;

    public int Version { get; set; } = SupportedVersion;
    public string License { get; set; } = "CC-BY-SA-3.0";
    public string Copyright { get; set; } = "";
    public int FrameWidth { get; set; } = 32;
    public int FrameHeight { get; set; } = 32;
    public List<RsiState> States { get; } = new();

    /// <summary>The original root object of a parsed meta.json: unknown top-level fields live here and survive ToJson. null for a new meta.</summary>
    private JsonObject? _source;

    public RsiState? Find(string name) => States.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Adds a state or replaces one with the same name (the order is kept). Unknown fields of the replaced state (flags and so on) are kept.</summary>
    public void Upsert(RsiState state)
    {
        int i = States.FindIndex(s => s.Name.Equals(state.Name, StringComparison.OrdinalIgnoreCase));
        state.Touched = true;
        if (i >= 0)
        {
            state.Source ??= States[i].Source;
            States[i] = state;
        }
        else States.Add(state);
    }

    private static JsonNode? DelaysNode(double[][] delays) =>
        new JsonArray(delays.Select(row => (JsonNode?)new JsonArray(row.Select(d => (JsonNode?)JsonValue.Create(Math.Round(d, 4))).ToArray())).ToArray());

    /// <summary>Writes a known field only when it differs from what is there, so untouched values keep their original text (1.0 stays 1.0).</summary>
    private static void SetKnown(JsonObject o, string key, JsonNode value)
    {
        if (o[key] is { } cur && JsonNode.DeepEquals(cur, value)) return;
        o[key] = value;
    }

    private JsonObject StateNode(RsiState s)
    {
        if (s.Source != null && !s.Touched) return (JsonObject)s.Source.DeepClone();
        var o = s.Source != null ? (JsonObject)s.Source.DeepClone() : new JsonObject();
        SetKnown(o, "name", JsonValue.Create(s.Name));
        // directions and delays are removed only when the new value is the default (one direction, one frame)
        if (s.Directions > 1) SetKnown(o, "directions", JsonValue.Create(s.Directions)); else o.Remove("directions");
        if (s.Frames > 1) o["delays"] = DelaysNode(s.Delays); else o.Remove("delays");
        return o;
    }

    /// <summary>
    /// Serializes meta.json. For a parsed meta the original JSON is the base: unknown fields (top level and per state) and the order of states are kept,
    /// only version, license, copyright, size and the states set through Upsert are updated.
    /// </summary>
    public string ToJson()
    {
        var root = _source != null ? (JsonObject)_source.DeepClone() : new JsonObject();
        bool fresh = _source == null;
        SetKnown(root, "version", JsonValue.Create(Version));
        // an empty license or copyright is allowed; an existing field is updated, a missing one is not invented
        if (fresh || root["license"] != null || License.Length > 0) SetKnown(root, "license", JsonValue.Create(License));
        if (fresh || root["copyright"] != null || Copyright.Length > 0) SetKnown(root, "copyright", JsonValue.Create(Copyright));
        if (root["size"] is not JsonObject size) root["size"] = size = new JsonObject();
        SetKnown(size, "x", JsonValue.Create(FrameWidth));
        SetKnown(size, "y", JsonValue.Create(FrameHeight));
        var states = new JsonArray();
        foreach (var s in States) states.Add(StateNode(s));
        root["states"] = states;
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    /// <summary>Parses meta.json (extra fields such as flags and select are kept in the source nodes for a lossless ToJson).</summary>
    public static RsiMeta Parse(string json)
    {
        var root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }) as JsonObject
                   ?? throw new FormatException(CoreText.T("meta.json должен быть объектом"));
        var meta = new RsiMeta
        {
            Version = root["version"]?.GetValue<int>() ?? SupportedVersion,
            License = root["license"]?.GetValue<string>() ?? "",
            Copyright = root["copyright"]?.GetValue<string>() ?? "",
            _source = root,
        };
        var size = root["size"] as JsonObject ?? throw new FormatException(CoreText.T("В meta.json нет size {x, y}"));
        meta.FrameWidth = size["x"]?.GetValue<int>() ?? throw new FormatException("size.x");
        meta.FrameHeight = size["y"]?.GetValue<int>() ?? throw new FormatException("size.y");
        if (meta.FrameWidth < 1 || meta.FrameHeight < 1) throw new FormatException(CoreText.T("Размер кадра должен быть положительным"));

        foreach (var node in root["states"] as JsonArray ?? [])
        {
            if (node is not JsonObject s) continue;
            var st = new RsiState
            {
                Source = s,
                Name = s["name"]?.GetValue<string>() ?? throw new FormatException(CoreText.T("У состояния нет name")),
                Directions = s["directions"]?.GetValue<int>() ?? 1,
            };
            if (st.Directions is not (1 or 4 or 8)) throw new FormatException(CoreText.F("Состояние «{0}»: directions должно быть 1, 4 или 8", st.Name));
            if (s["delays"] is JsonArray rows && rows.Count > 0)
            {
                st.Delays = rows.Select(r => (r as JsonArray ?? []).Select(d => d!.GetValue<double>()).ToArray()).ToArray();
                st.Frames = Math.Max(1, st.Delays[0].Length);
                if (st.Delays.Length < st.Directions)
                    st.Delays = Enumerable.Range(0, st.Directions).Select(i => st.Delays[Math.Min(i, st.Delays.Length - 1)]).ToArray();
            }
            else
            {
                st.Frames = 1;
                st.Delays = RsiState.UniformDelays(st.Directions, 1, 1.0);
            }
            meta.States.Add(st);
        }
        return meta;
    }
}

/// <summary>One sheet zone: direction and frame, a rectangle in pixels and a caption.</summary>
public sealed record RsiZone(int Direction, int Frame, PixelRect Rect, string Label);

public static class RsiLayout
{
    private static readonly string[] Names = ["S", "N", "E", "W", "SE", "SW", "NE", "NW"];

    /// <summary>Direction names in RSI order. For 4 directions: S, N, E, W; 8 adds SE, SW, NE, NW.</summary>
    public static IReadOnlyList<string> DirectionNames(int directions) => Names.Take(directions).ToArray();

    public static string DirectionTitle(string code) => code switch
    {
        "S" => CoreText.T("юг (вниз)"), "N" => CoreText.T("север (вверх)"), "E" => CoreText.T("восток (вправо)"), "W" => CoreText.T("запад (влево)"),
        "SE" => CoreText.T("юго-восток"), "SW" => CoreText.T("юго-запад"), "NE" => CoreText.T("северо-восток"), "NW" => CoreText.T("северо-запад"), _ => code,
    };

    public static IReadOnlyList<RsiZone> Zones(RsiBinding b)
    {
        var names = b.RowLabels;
        var list = new List<RsiZone>();
        for (int d = 0; d < b.Directions; d++)
            for (int f = 0; f < b.Frames; f++)
                list.Add(new RsiZone(d, f, new PixelRect(f * b.FrameWidth, d * b.FrameHeight, b.FrameWidth, b.FrameHeight),
                    b.Frames > 1 ? $"{names[d]} · {b.FrameName(f) ?? (f + 1).ToString()}" : names[d]));
        return list;
    }

    /// <summary>
    /// Reads a PNG sheet from a foreign .rsi: cells go in direction-frame order, left to right and top to bottom, wrapping by the picture width.
    /// The result is laid out into our sheet (rows are directions, columns are frames).
    /// </summary>
    public static PixelImage Repack(PixelImage source, int frameWidth, int frameHeight, int directions, int frames)
    {
        int srcCols = Math.Max(1, source.Width / frameWidth);
        var dst = new PixelImage(frameWidth * frames, frameHeight * directions);
        for (int d = 0; d < directions; d++)
            for (int f = 0; f < frames; f++)
            {
                int index = d * frames + f;
                int sx = index % srcCols * frameWidth, sy = index / srcCols * frameHeight;
                for (int y = 0; y < frameHeight; y++)
                    for (int x = 0; x < frameWidth; x++)
                        if (sx + x < source.Width && sy + y < source.Height)
                            dst.Pixels[(d * frameHeight + y) * dst.Width + f * frameWidth + x] = source.Pixels[(sy + y) * source.Width + sx + x];
            }
        return dst;
    }

    public static string SafeName(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in name.Trim())
            sb.Append(char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_');
        return sb.Length == 0 ? "state" : sb.ToString();
    }
}

/// <summary>Animation for engines: sheet descriptions in the formats Unity understands (Aseprite-compatible JSON and grid slices) and Godot (SpriteFrames .tres).</summary>
public static partial class AnimationExport
{
    /// <summary>One animation: a name, a sheet row, frame delays (seconds) and whether it loops (Godot loop flag, "loop" in the simple JSON).</summary>
    public sealed record Clip(string Name, int Row, double[] Delays, bool Loop = true);

    /// <summary>
    /// Clips of a sheet, one per row. noLoopRows: row names (or full clip names) that must not loop, compared case-insensitively.
    /// </summary>
    public static List<Clip> Clips(string state, int directions, int frames, double[][] delays, IReadOnlyList<string>? rowNames = null, IEnumerable<string>? noLoopRows = null)
    {
        var noLoop = new HashSet<string>(noLoopRows ?? [], StringComparer.OrdinalIgnoreCase);
        var clips = new List<Clip>();
        var names = rowNames is { Count: > 0 } && rowNames.Count == directions ? rowNames : RsiLayout.DirectionNames(directions);
        for (int d = 0; d < directions; d++)
        {
            var row = delays[Math.Min(d, delays.Length - 1)];
            var delaysForRow = Enumerable.Range(0, frames).Select(i => i < row.Length ? row[i] : row[^1]).ToArray();
            var clipName = directions == 1 ? state : $"{state}_{names[d]}";
            clips.Add(new Clip(clipName, d, delaysForRow, !(noLoop.Contains(clipName) || noLoop.Contains(names[d]))));
        }
        return clips;
    }

    private static string F(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>
    /// JSON in the Aseprite format (a frames array, meta.frameTags): read by Unity importers and Godot plugins. Frames go along the sheet rows, tags are the animations per direction.
    /// </summary>
    public static string AsepriteJson(string imageFile, int sheetWidth, int sheetHeight, int frameWidth, int frameHeight, int frames, IReadOnlyList<Clip> clips)
    {
        var frameArray = new JsonArray();
        var tags = new JsonArray();
        int index = 0;
        foreach (var clip in clips)
        {
            int from = index;
            for (int f = 0; f < frames; f++)
            {
                frameArray.Add(new JsonObject
                {
                    ["filename"] = $"{clip.Name} {f}",
                    ["frame"] = new JsonObject { ["x"] = f * frameWidth, ["y"] = clip.Row * frameHeight, ["w"] = frameWidth, ["h"] = frameHeight },
                    ["rotated"] = false,
                    ["trimmed"] = false,
                    ["spriteSourceSize"] = new JsonObject { ["x"] = 0, ["y"] = 0, ["w"] = frameWidth, ["h"] = frameHeight },
                    ["sourceSize"] = new JsonObject { ["w"] = frameWidth, ["h"] = frameHeight },
                    ["duration"] = (int)Math.Round(clip.Delays[f] * 1000),
                });
                index++;
            }
            tags.Add(new JsonObject { ["name"] = clip.Name, ["from"] = from, ["to"] = index - 1, ["direction"] = "forward" });
        }
        var root = new JsonObject
        {
            ["frames"] = frameArray,
            ["meta"] = new JsonObject
            {
                ["app"] = "Pixel Editor",
                ["version"] = "1.0",
                ["image"] = imageFile,
                ["format"] = "RGBA8888",
                ["size"] = new JsonObject { ["w"] = sheetWidth, ["h"] = sheetHeight },
                ["scale"] = "1",
                ["frameTags"] = tags,
            },
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// A Godot 4 SpriteFrames resource (.tres): frames as AtlasTexture on the sheet, one animation per direction. Speed is 10 frames per second,
    /// a frame's duration is its delay in seconds × 10. A clip loops when its own Loop flag and the loop argument are both true. texturePath: a path inside the Godot project, for example res://art/state.png.
    /// </summary>
    public static string GodotSpriteFrames(string texturePath, int frameWidth, int frameHeight, int frames, IReadOnlyList<Clip> clips, bool loop = true)
    {
        const double speed = 10.0;
        var sb = new StringBuilder();
        int subCount = clips.Count * frames;
        sb.Append($"[gd_resource type=\"SpriteFrames\" load_steps={subCount + 2} format=3]\n\n");
        sb.Append($"[ext_resource type=\"Texture2D\" path=\"{texturePath}\" id=\"1\"]\n\n");
        int n = 0;
        foreach (var clip in clips)
            for (int f = 0; f < frames; f++, n++)
            {
                sb.Append($"[sub_resource type=\"AtlasTexture\" id=\"AtlasTexture_{n}\"]\n");
                sb.Append("atlas = ExtResource(\"1\")\n");
                sb.Append($"region = Rect2({f * frameWidth}, {clip.Row * frameHeight}, {frameWidth}, {frameHeight})\n\n");
            }
        sb.Append("[resource]\nanimations = [");
        n = 0;
        for (int c = 0; c < clips.Count; c++)
        {
            var clip = clips[c];
            sb.Append(c == 0 ? "{\n" : ", {\n");
            sb.Append("\"frames\": [");
            for (int f = 0; f < frames; f++, n++)
            {
                sb.Append(f == 0 ? "{\n" : ", {\n");
                sb.Append($"\"duration\": {F(clip.Delays[f] * speed)},\n");
                sb.Append($"\"texture\": SubResource(\"AtlasTexture_{n}\")\n}}");
            }
            sb.Append("],\n");
            sb.Append($"\"loop\": {(loop && clip.Loop ? "true" : "false")},\n");
            sb.Append($"\"name\": &\"{clip.Name}\",\n");
            sb.Append($"\"speed\": {F(speed)}\n}}");
        }
        sb.Append("]\n");
        return sb.ToString();
    }

    /// <summary>Plain JSON not tied to an engine: frame size, grid, animations with delays. Handy for your own loaders (for example a Unity C# script).</summary>
    public static string SimpleJson(string imageFile, int frameWidth, int frameHeight, int frames, int directions, IReadOnlyList<Clip> clips)
    {
        var root = new JsonObject
        {
            ["image"] = imageFile,
            ["frameWidth"] = frameWidth,
            ["frameHeight"] = frameHeight,
            ["columns"] = frames,
            ["rows"] = directions,
            ["animations"] = new JsonArray(clips.Select(c => (JsonNode?)new JsonObject
            {
                ["name"] = c.Name,
                ["row"] = c.Row,
                ["loop"] = c.Loop,
                ["frames"] = new JsonArray(Enumerable.Range(0, frames).Select(f => (JsonNode?)new JsonObject { ["column"] = f, ["delay"] = c.Delays[f] }).ToArray()),
            }).ToArray()),
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
