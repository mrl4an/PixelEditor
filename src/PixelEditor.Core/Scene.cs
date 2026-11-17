// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Text.Json;
using PixelEditor.Core.Ops;

namespace PixelEditor.Core;

/// <summary>
/// A scene: a drawing made of primitives described by a short JSON. This is how an AI makes a placeholder (a little person, a tree, a chest) cheaply in tokens:
/// not pixel by pixel but "canvas 24×32, an ellipse head here of this color, a rectangle body here".
/// <code>
/// { "size": [24, 32],
///   "palette": { "skin": "#F1C27D", "coat": "#3B5BA5" },
///   "mirror": "x",                       // optional: everything is also drawn mirrored about the canvas center
///   "outline": "#000000",                // optional: a 1 px outline around the finished drawing (the canvas does not grow)
///   "items": [
///     { "ellipse": [8, 2, 8, 8], "color": "skin" },
///     { "rect": [7, 10, 10, 12], "color": "coat" },
///     { "line": [7, 12, 3, 20], "color": "skin", "thickness": 2 },
///     { "poly": [[12,24],[16,31],[8,31]], "color": "#222222" },
///     { "pixels": [[10,5],[13,5]], "color": "#000000" },
///     { "fill": [1, 1], "color": "#CCEEFF" } ] }
/// </code>
/// Shapes: rect and ellipse by the frame [x, y, width, height]; line [x1, y1, x2, y2]; poly by points; pixels single pixels; fill an area fill from a point.
/// Shape options: color (#RRGGBB, a name from palette or "clear" to erase), fill (true by default, false draws only the outline), thickness (outline and line thickness),
/// mirror ("x", "y", "xy") for a single shape. Shapes are drawn in order, later ones on top of earlier ones.
/// </summary>
public static class Scene
{
    private static readonly string[] ShapeKeys = ["rect", "ellipse", "line", "poly", "pixels", "fill"];

    /// <summary>Draws a scene. Errors (an unknown color, a wrong number of coordinates) name the shape number.</summary>
    public static PixelImage Render(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        return Render(doc.RootElement);
    }

    public static PixelImage Render(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) throw new ArgumentException(CoreText.T("Сцена должна быть объектом {size, items}"));
        var (w, h) = ReadSize(root);

        var palette = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("palette", out var pal) && pal.ValueKind == JsonValueKind.Object)
            foreach (var p in pal.EnumerateObject())
                palette[p.Name] = ParseColor(p.Value.GetString(), palette, $"palette.{p.Name}");

        var layer = new Layer(w, h, "scene");
        string sceneMirror = root.TryGetProperty("mirror", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString()! : "";

        if (root.TryGetProperty("background", out var bg) && bg.ValueKind == JsonValueKind.String)
        {
            uint bgc = ParseColor(bg.GetString(), palette, "background");
            for (int i = 0; i < layer.Pixels.Length; i++) layer.Pixels[i] = bgc;
        }

        if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            int n = 0;
            foreach (var item in items.EnumerateArray())
            {
                n++;
                try { DrawItem(layer, item, palette, sceneMirror); }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException or FormatException)
                {
                    throw new ArgumentException(CoreText.F("фигура {0}: {1}", n, ex.Message));
                }
            }
        }
        else throw new ArgumentException(CoreText.T("Нет списка items: [{\"rect\":[x,y,w,h],\"color\":\"#RRGGBB\"}, ...]"));

        var image = layer.ToImage();
        if (root.TryGetProperty("outline", out var ol) && ol.ValueKind == JsonValueKind.String)
        {
            var args = JsonDocument.Parse($"{{\"color\":\"{ol.GetString()}\",\"mode\":\"outside\",\"autoPad\":false}}");
            image = Ops.Operations.Get("outline").Run(image, new Ops.OpArgs(args.RootElement, null));
        }
        return image;
    }

    private static (int W, int H) ReadSize(JsonElement root)
    {
        int w, h;
        if (root.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Array && s.GetArrayLength() == 2)
            (w, h) = (s[0].GetInt32(), s[1].GetInt32());
        else if (root.TryGetProperty("width", out var ww) && root.TryGetProperty("height", out var hh))
            (w, h) = (ww.GetInt32(), hh.GetInt32());
        else throw new ArgumentException(CoreText.T("Не задан размер: \"size\": [ширина, высота]"));
        if (w < 1 || h < 1 || w > 512 || h > 512) throw new ArgumentException(CoreText.F("Размер {0}×{1} вне допустимого (1…512)", w, h));
        return (w, h);
    }

    /// <summary>#RRGGBB, RRGGBB, a name from palette or clear/none/transparent (erase).</summary>
    public static uint ParseColor(string? text, IReadOnlyDictionary<string, uint> palette, string where)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0) throw new ArgumentException(CoreText.F("{0}: цвет не задан", where));
        if (text.Equals("clear", StringComparison.OrdinalIgnoreCase) || text.Equals("none", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("transparent", StringComparison.OrdinalIgnoreCase)) return Pixel.Empty;
        if (palette.TryGetValue(text, out var named)) return named;
        if (Pixel.TryParseHex(text, out var c)) return c;
        throw new ArgumentException(CoreText.F("{0}: неизвестный цвет «{1}» (нужен #RRGGBB, имя из palette или clear)", where, text));
    }

    private static int[] Ints(JsonElement e, int count, string what)
    {
        if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() != count)
            throw new ArgumentException(CoreText.F("{0}: нужно {1} чисел", what, count));
        return e.EnumerateArray().Select(v => (int)Math.Round(v.GetDouble())).ToArray();
    }

    private static List<(int X, int Y)> Points(JsonElement e, string what)
    {
        if (e.ValueKind != JsonValueKind.Array) throw new ArgumentException(CoreText.F("{0}: нужен список точек [[x,y], ...]", what));
        return e.EnumerateArray().Select(p => { var v = Ints(p, 2, what); return (v[0], v[1]); }).ToList();
    }

    private static void DrawItem(Layer layer, JsonElement item, Dictionary<string, uint> palette, string sceneMirror, int depth = 0)
    {
        if (item.ValueKind != JsonValueKind.Object) throw new ArgumentException(CoreText.T("фигура должна быть объектом"));
        if (item.TryGetProperty("group", out var children))
        {
            if (children.ValueKind != JsonValueKind.Array || depth >= 8)
                throw new ArgumentException("A scene group requires an array and supports up to eight levels.");
            var offset = item.TryGetProperty("offset", out var o) ? Ints(o, 2, "offset") : new[] { 0, 0 };
            var group = new Layer(layer.Width, layer.Height, "group");
            foreach (var child in children.EnumerateArray()) DrawItem(group, child, palette, "", depth + 1);
            var image = group.ToImage();
            if (item.TryGetProperty("lightFrom", out var light))
                image = Lighting.EdgeShade(image, Lighting.ToLight(light.GetString()), 1, 0.25, 0.25);

            var context = new PaintContext(new PixelBatch(layer));
            var groupMirror = item.TryGetProperty("mirror", out var gm) ? gm.GetString() ?? "" : sceneMirror;
            context.MirrorX = groupMirror.Contains('x', StringComparison.OrdinalIgnoreCase);
            context.MirrorY = groupMirror.Contains('y', StringComparison.OrdinalIgnoreCase);
            for (int y = 0; y < image.Height; y++)
                for (int x = 0; x < image.Width; x++)
                {
                    var pixel = image.Get(x, y);
                    long dx = (long)x + offset[0], dy = (long)y + offset[1];
                    if (Pixel.IsEmpty(pixel) || dx < 0 || dy < 0 || dx >= layer.Width || dy >= layer.Height) continue;
                    context.Color = pixel;
                    context.Plot((int)dx, (int)dy);
                }
            return;
        }
        var key = ShapeKeys.FirstOrDefault(k => item.TryGetProperty(k, out _))
                  ?? throw new ArgumentException(CoreText.F("не найдено ни одного из: {0}", string.Join(", ", ShapeKeys)));
        var shape = item.GetProperty(key);

        uint color = ParseColor(item.TryGetProperty("color", out var c) ? c.GetString() : null, palette, key);
        // the fill flag (true/false) belongs to shapes; for the fill shape itself "fill" is a point
        bool fill = key == "fill" || !item.TryGetProperty("fill", out var f) || f.ValueKind != JsonValueKind.False;
        int thickness = item.TryGetProperty("thickness", out var t) ? Math.Clamp(t.GetInt32(), 1, 32) : 1;
        string mirror = item.TryGetProperty("mirror", out var mm) && mm.ValueKind == JsonValueKind.String ? mm.GetString()! : sceneMirror;

        var batch = new PixelBatch(layer);
        var ctx = new PaintContext(batch)
        {
            Color = color,
            BrushSize = fill && key is "rect" or "ellipse" or "poly" ? 1 : thickness,
            MirrorX = mirror.Contains('x', StringComparison.OrdinalIgnoreCase),
            MirrorY = mirror.Contains('y', StringComparison.OrdinalIgnoreCase),
        };

        switch (key)
        {
            case "rect":
            {
                var v = Ints(shape, 4, "rect");
                if (item.TryGetProperty("radius", out var radius))
                    Drawing.RoundedRectangle(new PixelRect(v[0], v[1], v[2], v[3]), radius.GetInt32(), fill,
                        new PixelRect(0, 0, layer.Width, layer.Height), ctx.Plot);
                else
                    Drawing.Rectangle(v[0], v[1], v[0] + v[2] - 1, v[1] + v[3] - 1, fill, ctx.Plot);
                break;
            }
            case "ellipse":
            {
                var v = Ints(shape, 4, "ellipse");
                Drawing.Ellipse(v[0], v[1], v[0] + v[2] - 1, v[1] + v[3] - 1, fill, ctx.Plot);
                break;
            }
            case "line":
            {
                var v = Ints(shape, 4, "line");
                Drawing.Line(v[0], v[1], v[2], v[3], ctx.Plot);
                break;
            }
            case "poly":
            {
                var pts = Points(shape, "poly");
                if (pts.Count < 2) throw new ArgumentException(CoreText.T("poly: нужно минимум 2 точки"));
                if (fill)
                {
                    var mask = SelectionMask.Polygon(pts, layer.Width, layer.Height);
                    if (mask != null)
                        for (int y = 0; y < mask.Bounds.Height; y++)
                            for (int x = 0; x < mask.Bounds.Width; x++)
                                if (mask.Bits[y * mask.Bounds.Width + x]) ctx.Plot(mask.Bounds.X + x, mask.Bounds.Y + y);
                }
                else
                    for (int i = 0; i < pts.Count; i++)
                    {
                        var a = pts[i];
                        var b = pts[(i + 1) % pts.Count];
                        Drawing.Line(a.X, a.Y, b.X, b.Y, ctx.Plot);
                    }
                break;
            }
            case "pixels":
                foreach (var (x, y) in Points(shape, "pixels")) ctx.Plot(x, y);
                break;
            case "fill":
            {
                var v = Ints(shape, 2, "fill");
                Drawing.FloodFill(batch, v[0], v[1], color);
                break;
            }
        }
    }
}
