// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Text.Json;
using System.Text.Json.Nodes;
using PixelEditor.Core;
using PixelEditor.Core.Ops;

namespace PixelEditor.Tests;

/// <summary>
/// Every registered operation runs on a real picture with sample arguments and must give the expected result.
/// A new operation without an entry in the Samples table fails the test: this way the catalog does not grow unchecked.
/// </summary>
public class OperationsCatalogTests
{
    private static readonly uint Red = Pixel.Rgb(220, 40, 40), Blue = Pixel.Rgb(40, 60, 200), Green = Pixel.Rgb(40, 180, 70), Ink = Pixel.Rgb(20, 20, 30);

    /// <summary>A sprite on a transparent background: a 12×12 body with an outline, a noise dot and a hole.</summary>
    private static PixelImage Sprite()
    {
        var img = new PixelImage(24, 24);
        for (int y = 6; y < 18; y++)
            for (int x = 6; x < 18; x++)
            {
                bool edge = x == 6 || x == 17 || y == 6 || y == 17;
                img.Set(x, y, edge ? Ink : x < 12 ? Red : (y < 12 ? Blue : Green));
            }
        img.Set(10, 10, Pixel.Empty);              // a hole
        img.Set(2, 2, Red);                         // a lone pixel
        img.Set(14, 14, Pixel.Rgb(230, 230, 230)); // a color outlier inside a blue/green area
        img.Set(9, 14, Pixel.Rgb(225, 45, 45));    // an almost red pixel among red ones (JPEG noise)
        return img;
    }

    /// <summary>A "photo": an opaque gradient background and a subject in the middle.</summary>
    private static PixelImage Photo()
    {
        var img = new PixelImage(32, 32);
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                bool item = x is >= 10 and < 22 && y is >= 10 and < 22;
                img.Set(x, y, item ? Pixel.Rgb((byte)(200 + x), (byte)(60 + y), 50) : Pixel.Rgb(250, (byte)(250 - x / 8), 250));
            }
        return img;
    }

    private sealed record Sample(string Args, bool OnPhoto = false, bool MustChange = true);

    private static Dictionary<string, Sample> Samples()
    {
        var profile = SpriteStyle.ToJson(SpriteStyle.Extract(Sprite(), 4));
        return new(StringComparer.OrdinalIgnoreCase)
        {
            ["resize"] = new("""{"width":12,"height":12,"method":"mode"}"""),
            ["alphaThreshold"] = new("""{"value":128}""", MustChange: false),
            ["quantize"] = new("""{"colors":4}""", OnPhoto: true),
            ["applyPalette"] = new("""{"paletteName":"PICO-8"}"""),
            ["removeIslands"] = new("""{"maxSize":1}"""),
            ["fillHoles"] = new("""{"maxSize":4}"""),
            ["removeBackground"] = new("""{"tolerance":24}""", OnPhoto: true),
            ["adjust"] = new("""{"brightness":20,"contrast":10,"saturation":15}"""),
            ["polygonMask"] = new("""{"points":[[8,8],[22,8],[22,22],[8,22]]}""", OnPhoto: true),
            ["edgeShade"] = new("""{"from":"NW","depth":1}"""),
            ["light"] = new("""{"from":"NW","bands":4,"rim":true}"""),
            ["cutout"] = new("""{"clusters":3,"tolerance":30}""", OnPhoto: true),
            ["removeOutline"] = new("""{"color":"#14141E","passes":1}"""),
            ["cleanEdges"] = new("{}", MustChange: false),
            ["mergeRareColors"] = new("""{"minCount":3}"""),
            ["despeckle"] = new("""{"maxArea":3,"diagonal":false}""", OnPhoto: true),
            ["simplifyColorRegions"] = new("""{"maxArea":4,"tolerance":60}"""),
            ["applySpriteStyle"] = new($"{{\"profile\":{profile}}}"),
            ["patchPixels"] = new("""{"edits":[[3,3,"#FF0000"],[8,8,"transparent"]]}"""),
            ["replaceColor"] = new($"{{\"from\":\"{Pixel.ToHex(Red)}\",\"to\":\"#00FF00\"}}"),
            ["outline"] = new("""{"color":"#FFFFFF","mode":"outside"}"""),
            ["pad"] = new("""{"amount":2}"""),
            ["trim"] = new("{}"),
            ["shift"] = new("""{"dx":3,"dy":1,"wrap":false}"""),
            ["resizeCanvas"] = new("""{"width":40,"height":30,"anchor":"center"}"""),
            ["rotate"] = new("""{"angle":30}"""),
            ["flipH"] = new("{}"),
            ["flipV"] = new("{}"),
            ["rotate90"] = new("""{"times":1}"""),
            ["invert"] = new("{}"),
            ["grayscale"] = new("{}"),
            ["hueShift"] = new("""{"degrees":120}"""),
            ["tint"] = new("""{"dark":"#201040","light":"#FFE0A0"}"""),
            ["posterize"] = new("""{"levels":3}""", OnPhoto: true),
            ["threshold"] = new("""{"value":120}"""),
            ["autoLevels"] = new("""{"cutoff":1}""", OnPhoto: true),
            ["median"] = new("""{"radius":1}""", OnPhoto: true),
            ["unsharp"] = new("""{"radius":1,"amount":1}""", OnPhoto: true),
            ["edges"] = new("""{"threshold":30}""", OnPhoto: true),
            ["swapColors"] = new($"{{\"a\":\"{Pixel.ToHex(Red)}\",\"b\":\"{Pixel.ToHex(Blue)}\"}}"),
            ["background"] = new("""{"color":"#FFFFFF"}"""),
            ["crop"] = new("""{"x":4,"y":4,"width":16,"height":16}"""),
            ["mirror"] = new("""{"mode":"left"}"""),
            ["tile"] = new("""{"cols":2,"rows":3}"""),
            ["grow"] = new("""{"amount":1}"""),
            ["shrink"] = new("""{"amount":1}"""),
            ["dropShadow"] = new("""{"dx":2,"dy":2,"color":"#000000"}"""),
            ["smooth"] = new("""{"passes":1}"""),
            ["pixelate"] = new("""{"size":3}"""),
        };
    }

    private static PixelImage RunOp(string name, string args, PixelImage input)
    {
        using var doc = JsonDocument.Parse(args);
        return Operations.Get(name).Run(input, new OpArgs(doc.RootElement.Clone(), null));
    }

    private static bool Same(PixelImage a, PixelImage b) => a.Width == b.Width && a.Height == b.Height && a.Pixels.SequenceEqual(b.Pixels);

    [Fact]
    public void EveryRegisteredOperation_HasASample()
    {
        var samples = Samples();
        var missing = Operations.All.Where(o => !samples.ContainsKey(o.Name) && o.Source == "builtin").Select(o => o.Name).ToList();
        Assert.True(missing.Count == 0, "Нет примера для операций: " + string.Join(", ", missing));
        Assert.True(Operations.All.Count(o => o.Source == "builtin") >= 40, "каталог операций стал короче");
    }

    [Fact]
    public void EveryOperation_RunsAndDoesWhatItSays_AndNeverTouchesTheInput()
    {
        foreach (var (name, sample) in Samples())
        {
            var input = sample.OnPhoto ? Photo() : Sprite();
            var before = input.Clone();
            PixelImage result;
            try { result = RunOp(name, sample.Args, input); }
            catch (Exception ex) { throw new Xunit.Sdk.XunitException($"{name}: упала с {ex.GetType().Name}: {ex.Message}"); }

            Assert.True(Same(input, before), $"{name}: изменила входную картинку");
            Assert.True(result.Width > 0 && result.Height > 0 && result.Pixels.Length == result.Width * result.Height, $"{name}: неверный размер результата");
            if (sample.MustChange) Assert.False(Same(result, input), $"{name}: ничего не изменила на примере");
        }
    }

    [Fact]
    public void EveryOperation_WorksInsideAPipeline_AndOnTinyImages()
    {
        foreach (var (name, sample) in Samples())
        {
            var step = JsonNode.Parse(sample.Args)!.AsObject();
            step["op"] = name;
            var pipeline = new JsonObject { ["name"] = "t", ["steps"] = new JsonArray(step), ["checks"] = new JsonArray() };
            var p = Pipeline.Parse(pipeline.ToJsonString());
            Assert.Empty(p.Validate());
            var run = p.Run(sample.OnPhoto ? Photo() : Sprite());
            Assert.True(run.Ok, $"{name}: шаг в пайплайне упал: {string.Join("; ", run.Log.Where(s => s.Error != null).Select(s => s.Error))}");
        }

        // tiny and empty pictures must not crash the simple operations
        foreach (var name in new[] { "invert", "grayscale", "hueShift", "tint", "posterize", "threshold", "autoLevels", "background", "mirror", "tile", "grow", "shrink", "dropShadow", "smooth", "pixelate", "flipH", "flipV", "rotate90", "trim" })
            foreach (var img in new[] { new PixelImage(1, 1), new PixelImage(3, 2) })
                RunOp(name, Samples()[name].Args, img);
    }

    [Fact]
    public void ColorOperations_GiveExactResults()
    {
        var one = new PixelImage(2, 1);
        one.Set(0, 0, Pixel.Rgb(200, 100, 50));
        Assert.Equal(Pixel.Rgb(55, 155, 205), RunOp("invert", "{}", one).Get(0, 0));
        Assert.True(Pixel.IsEmpty(RunOp("invert", "{}", one).Get(1, 0)));               // transparent stays transparent

        var gray = RunOp("grayscale", "{}", one).Get(0, 0);
        Assert.True(Pixel.R(gray) == Pixel.G(gray) && Pixel.G(gray) == Pixel.B(gray));

        var red = new PixelImage(1, 1); red.Set(0, 0, Pixel.Rgb(255, 0, 0));
        var shifted = RunOp("hueShift", """{"degrees":120}""", red).Get(0, 0);
        Assert.Equal(Pixel.Rgb(0, 255, 0), shifted);                                    // red at 120° is green

        var white = new PixelImage(1, 1); white.Set(0, 0, Pixel.Rgb(255, 255, 255));
        Assert.Equal(Pixel.Rgb(255, 224, 160), RunOp("tint", """{"dark":"#201040","light":"#FFE0A0"}""", white).Get(0, 0));
        Assert.Equal(Pixel.Rgb(0, 0, 0), RunOp("threshold", """{"value":120}""", one.Clone().Also(i => i.Set(0, 0, Pixel.Rgb(10, 10, 10)))).Get(0, 0));
        Assert.Equal(Pixel.Rgb(255, 255, 255), RunOp("threshold", """{"value":120}""", white).Get(0, 0));

        var post = RunOp("posterize", """{"levels":2}""", one).Get(0, 0);
        Assert.All(new[] { Pixel.R(post), Pixel.G(post), Pixel.B(post) }, c => Assert.True(c is 0 or 255));

        var two = new PixelImage(2, 1); two.Set(0, 0, Pixel.Rgb(100, 100, 100)); two.Set(1, 0, Pixel.Rgb(150, 150, 150));
        var lv = RunOp("autoLevels", "{}", two);
        Assert.Equal(Pixel.Rgb(0, 0, 0), lv.Get(0, 0));
        Assert.Equal(Pixel.Rgb(255, 255, 255), lv.Get(1, 0));

        var swap = RunOp("swapColors", $"{{\"a\":\"{Pixel.ToHex(Red)}\",\"b\":\"{Pixel.ToHex(Blue)}\"}}", Sprite());
        Assert.Equal(Blue, swap.Get(7, 7));
        Assert.Equal(Red, swap.Get(14, 7));

        var bg = RunOp("background", """{"color":"#112233"}""", Sprite());
        Assert.Equal(Pixel.Rgb(0x11, 0x22, 0x33), bg.Get(0, 0));
        Assert.Equal(Ink, bg.Get(6, 6));
    }

    [Fact]
    public void Despeckle_RemovesColorSpeckleButKeepsThinLinesAndTransparency()
    {
        // a photo-like picture: two color fields and a scatter of single pixels of another color
        var img = new PixelImage(20, 20);
        var rnd = new Random(3);
        for (int y = 0; y < 20; y++) for (int x = 0; x < 20; x++) img.Set(x, y, x < 10 ? Red : Blue);
        for (int i = 0; i < 25; i++) img.Set(rnd.Next(1, 9), rnd.Next(1, 19), Green);       // crumbs on red
        int before = RunOp("despeckle", """{"maxArea":3,"diagonal":false}""", new PixelImage(1, 1)).Width;
        var clean = RunOp("despeckle", """{"maxArea":3,"diagonal":false,"passes":4}""", img);
        Assert.Equal(0, clean.Pixels.Count(p => p == Green));
        Assert.Equal(200, clean.Pixels.Count(p => p == Red));                                // the left field is entirely red again
        Assert.Equal(200, clean.Pixels.Count(p => p == Blue));

        // a thin diagonal line on a transparent background does not fall apart and is not erased (8-connectivity by default)
        var line = new PixelImage(12, 12);
        for (int i = 0; i < 12; i++) line.Set(i, i, Ink);
        Assert.True(Same(line, RunOp("despeckle", """{"maxArea":3}""", line)));
        // a single dot without opaque neighbors stays (removeIslands removes it), the hole is not filled
        var dot = new PixelImage(5, 5); dot.Set(2, 2, Red);
        Assert.Equal(1, RunOp("despeckle", "{}", dot).OpaqueCount());
        var ring = new PixelImage(5, 5);
        for (int y = 0; y < 5; y++) for (int x = 0; x < 5; x++) if (x is 0 or 4 || y is 0 or 4 || (x != 2 || y != 2)) ring.Set(x, y, Red);
        Assert.True(Pixel.IsEmpty(RunOp("despeckle", "{}", ring).Get(2, 2)));
    }

    [Fact]
    public void ShapeOperations_GiveExactResults()
    {
        var s = Sprite();
        var crop = RunOp("crop", """{"x":4,"y":4,"width":16,"height":16}""", s);
        Assert.Equal((16, 16), (crop.Width, crop.Height));
        Assert.Equal(Ink, crop.Get(2, 2));                                              // the outline was at (6,6)
        Assert.Throws<ArgumentException>(() => RunOp("crop", """{"x":100,"y":100,"width":4,"height":4}""", s));

        var tile = RunOp("tile", """{"cols":2,"rows":3}""", s);
        Assert.Equal((48, 72), (tile.Width, tile.Height));
        Assert.Equal(s.Get(7, 7), tile.Get(7 + 24, 7 + 48));

        var mirror = RunOp("mirror", """{"mode":"left"}""", s);
        for (int y = 0; y < 24; y++) Assert.Equal(mirror.Get(5, y), mirror.Get(18, y));   // the left half is mirrored to the right
        var quad = RunOp("mirror", """{"mode":"quad"}""", s);
        Assert.Equal(quad.Get(7, 7), quad.Get(16, 16));
        Assert.Throws<ArgumentException>(() => RunOp("mirror", """{"mode":"diagonal"}""", s));

        var box = new PixelImage(9, 9);
        for (int y = 3; y < 6; y++) for (int x = 3; x < 6; x++) box.Set(x, y, Red);
        Assert.Equal(9 + 12, RunOp("grow", """{"amount":1}""", box).OpaqueCount());       // a 3×3 square plus 4 sides of 3
        Assert.Equal(9 + 12 + 4, RunOp("grow", """{"amount":1,"diagonal":true}""", box).OpaqueCount());
        Assert.Equal(1, RunOp("shrink", """{"amount":1}""", box).OpaqueCount());
        Assert.Equal(Green, RunOp("grow", """{"amount":1,"color":"#28B446"}""", box).Get(2, 4));

        var shadow = RunOp("dropShadow", """{"dx":2,"dy":3,"color":"#000000"}""", box);
        Assert.Equal((11, 12), (shadow.Width, shadow.Height));                             // the canvas grew exactly by the shadow
        Assert.Equal(Red, shadow.Get(3, 3));
        Assert.Equal(Pixel.Rgb(0, 0, 0), shadow.Get(7, 8));
        Assert.Equal(18, shadow.OpaqueCount());                                            // 9 shape pixels and 9 shadow pixels (they do not overlap)

        var noisy = new PixelImage(5, 5);
        Array.Fill(noisy.Pixels, Red);
        noisy.Set(2, 2, Blue);
        Assert.Equal(Red, RunOp("smooth", """{"passes":1}""", noisy).Get(2, 2));          // the noise is removed
        var edge = RunOp("smooth", """{"passes":1}""", s);
        Assert.Equal(Ink, edge.Get(6, 6));                                                 // the outline is not eaten away

        var pix = RunOp("pixelate", """{"size":4}""", s);
        Assert.Equal((24, 24), (pix.Width, pix.Height));
        Assert.True(pix.DistinctColors() <= s.DistinctColors());
    }
}

internal static class TestExt
{
    public static T Also<T>(this T value, Action<T> action) { action(value); return value; }
}
