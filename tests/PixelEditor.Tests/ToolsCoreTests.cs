// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using PixelEditor.Core;
using PixelEditor.Core.Ops;

namespace PixelEditor.Tests;

public class SelectionMaskTests
{
    private static readonly uint Red = Pixel.Rgb(255, 0, 0), Blue = Pixel.Rgb(0, 0, 255);

    private static Layer Make(int w, int h) => new(w, h, "t");

    private static void Rect(Layer l, int x0, int y0, int w, int h, uint c)
    {
        for (int y = y0; y < y0 + h; y++)
            for (int x = x0; x < x0 + w; x++) l.Pixels[y * l.Width + x] = c;
    }

    [Fact]
    public void Wand_Contiguous_SelectsOnlyConnectedSameColor()
    {
        var l = Make(10, 10);
        Rect(l, 1, 1, 3, 3, Red);
        Rect(l, 6, 6, 2, 2, Red);
        var m = SelectionMask.Wand(l, 2, 2, 0, contiguous: true)!;
        Assert.Equal(9, m.Count);
        Assert.True(m.Contains(2, 2));
        Assert.False(m.Contains(6, 6));

        var all = SelectionMask.Wand(l, 2, 2, 0, contiguous: false)!;
        Assert.Equal(13, all.Count); // the second red square too
    }

    [Fact]
    public void Wand_Tolerance_MergesCloseColors_AndEmptyIsSelectable()
    {
        var l = Make(6, 1);
        l.Pixels[0] = Pixel.Rgb(100, 100, 100);
        l.Pixels[1] = Pixel.Rgb(108, 100, 100);
        l.Pixels[2] = Pixel.Rgb(200, 100, 100);
        Assert.Equal(1, SelectionMask.Wand(l, 0, 0, 0, true)!.Count);
        Assert.Equal(2, SelectionMask.Wand(l, 0, 0, 10, true)!.Count);
        Assert.Equal(3, SelectionMask.Wand(l, 5, 0, 0, true)!.Count); // the empty background on the right
    }

    [Fact]
    public void Polygon_FillsInside_AndKeepsOutlineForThinShapes()
    {
        var square = SelectionMask.Polygon([(2, 2), (7, 2), (7, 7), (2, 7)], 10, 10)!;
        Assert.True(square.Contains(4, 4));
        Assert.False(square.Contains(0, 0));
        Assert.Equal(36, square.Count);

        var line = SelectionMask.Polygon([(1, 1), (5, 1)], 10, 10)!;
        Assert.Equal(5, line.Count);
    }

    [Fact]
    public void Mask_CopyAndClear_TouchOnlyMaskedPixels()
    {
        var l = Make(6, 6);
        Rect(l, 0, 0, 6, 6, Red);
        var m = SelectionMask.From(6, 6, (x, y) => x == y)!;
        var img = m.Copy(l);
        Assert.Equal(Red, img.Pixels[0]);
        Assert.True(Pixel.IsEmpty(img.Pixels[1]));

        var batch = new PixelBatch(l);
        m.Clear(batch);
        Assert.True(Pixel.IsEmpty(l.Get(3, 3)));
        Assert.Equal(Red, l.Get(3, 2));
    }

    [Fact]
    public void Union_And_Subtract()
    {
        var a = SelectionMask.Rectangle(new PixelRect(0, 0, 2, 2));
        var b = SelectionMask.Rectangle(new PixelRect(1, 1, 2, 2));
        Assert.Equal(7, a.Union(b, 5, 5).Count);
        Assert.Equal(3, a.Subtract(b, 5, 5)!.Count);
    }

    [Fact]
    public void FloatingMask_LiftsOnlyShape_TransformsAndMoves()
    {
        var l = Make(10, 10);
        Rect(l, 2, 2, 4, 2, Red);
        Rect(l, 3, 2, 1, 2, Blue);
        var m = SelectionMask.From(10, 10, (x, y) => l.Get(x, y) == Red)!;
        var f = FloatingSelection.LiftMask(l, m);
        Assert.Equal(Blue, l.Get(3, 2)); // not in the mask: stayed

        f.Transform(img =>
        {
            var d = new PixelImage(img.Height, img.Width);
            for (int y = 0; y < img.Height; y++)
                for (int x = 0; x < img.Width; x++) d.Pixels[x * d.Width + y] = img.Pixels[y * img.Width + x];
            return d; // transposition
        });
        Assert.Equal(2, f.Bounds.Width);
        Assert.NotNull(f.Mask);
        Assert.Equal(f.Bounds.Width * f.Bounds.Height, f.Mask!.Length);
        f.MoveTo(0, 5);
        Assert.Equal(Red, l.Get(0, 5));
        Assert.True(Pixel.IsEmpty(l.Get(2, 2))); // the old place is cleared only by the mask
        Assert.Equal(Blue, l.Get(3, 2));
    }
}

public class GradientAndFontTests
{
    private static readonly uint Black = Pixel.Rgb(0, 0, 0), White = Pixel.Rgb(255, 255, 255);

    [Fact]
    public void Dither_UsesOnlyTwoColors_AndEndsAreSolid()
    {
        var l = new Layer(16, 4, "g");
        var b = new PixelBatch(l);
        Gradient.Fill(b, null, null, 0, 0, 15, 0, Black, White, GradientMode.Dither);
        Assert.All(l.Pixels, p => Assert.True(p == Black || p == White));
        Assert.Equal(Black, l.Get(0, 0));
        Assert.Equal(White, l.Get(15, 3));
        int whites = l.Pixels.Count(p => p == White);
        Assert.InRange(whites, 20, 44); // about half in the middle
    }

    [Fact]
    public void Smooth_AndSteps_AndTransparentEndForcesDither()
    {
        var l = new Layer(11, 1, "g");
        Gradient.Fill(new PixelBatch(l), null, null, 0, 0, 10, 0, Black, White, GradientMode.Smooth);
        Assert.Equal(Black, l.Get(0, 0));
        Assert.Equal(White, l.Get(10, 0));
        Assert.Equal(Pixel.Rgb(128, 128, 128), l.Get(5, 0));

        var s = new Layer(12, 1, "s");
        Gradient.Fill(new PixelBatch(s), null, null, 0, 0, 11, 0, Black, White, GradientMode.Steps, 3);
        Assert.Equal(3, s.Pixels.Distinct().Count());

        var t = new Layer(8, 1, "t");
        Gradient.Fill(new PixelBatch(t), null, null, 0, 0, 7, 0, White, Pixel.Empty, GradientMode.Smooth);
        Assert.All(t.Pixels, p => Assert.True(p == White || Pixel.IsEmpty(p)));
    }

    [Fact]
    public void Gradient_RespectsMask()
    {
        var l = new Layer(6, 1, "m");
        var mask = SelectionMask.From(6, 1, (x, _) => x >= 3)!;
        Gradient.Fill(new PixelBatch(l), null, mask, 3, 0, 5, 0, Black, White, GradientMode.Smooth);
        Assert.True(Pixel.IsEmpty(l.Get(0, 0)));
        Assert.Equal(Black, l.Get(3, 0));
    }

    [Fact]
    public void ColorShift_KeepsTransparent_AndHueRotates()
    {
        Assert.True(Pixel.IsEmpty(Gradient.Brightness(Pixel.Empty, 0.5)));
        var red = Pixel.Rgb(255, 0, 0);
        Assert.Equal(Pixel.Rgb(0, 255, 0), Gradient.Hue(red, 120));
        Assert.True(Pixel.R(Gradient.Brightness(Pixel.Rgb(100, 0, 0), 0.2)) > 100);
        Assert.True(Pixel.R(Gradient.Brightness(Pixel.Rgb(100, 0, 0), -0.2)) < 100);
    }

    [Fact]
    public void PixelFont_RendersLatinAndCyrillic_WithScaleAndLines()
    {
        var a = PixelFont.Render("A", White)!;
        Assert.Equal((5, 9), (a.Width, a.Height));   // the line box has room for descenders
        Assert.Equal(White, a.Pixels[1]); // the top of the letter A: ".###."
        Assert.True(Pixel.IsEmpty(a.Pixels[0]));

        var ab = PixelFont.Render("ab", White)!; // real lowercase now
        Assert.Equal(11, ab.Width);
        Assert.NotEqual(PixelFont.Render("AB", White)!.Pixels, ab.Pixels);

        Assert.True(PixelFont.Supports('Ж') && PixelFont.Supports('я') && PixelFont.Supports('7'));
        Assert.False(PixelFont.Supports('€'));

        var big = PixelFont.Render("A", White, 3)!;
        Assert.Equal((15, 27), (big.Width, big.Height));

        var two = PixelFont.Render("A\nA", White)!;
        Assert.Equal(9 + 1 + 9, two.Height);
        Assert.Null(PixelFont.Render("   ", White));
    }

    [Fact]
    public void PixelFonts_EveryGlyphHasTheCellSize_AndLookupByNameWorks()
    {
        foreach (var f in PixelFonts.All)
        {
            foreach (var c in f.Chars)
            {
                var rows = f.Find(c, false)!;
                Assert.Equal(f.Height, rows.Length);
                Assert.All(rows, r => Assert.Equal(f.CellWidth, r.Length));
            }
            Assert.Same(f, PixelFonts.Get(f.Id));
            Assert.Same(f, PixelFonts.Get(f.Name));
        }
        Assert.Same(PixelFonts.Default, PixelFonts.Get("no such font"));
        Assert.True(PixelFonts.All.Count >= 2);
    }

    [Fact]
    public void PixelFonts_LowercaseDiffersFromUppercase_CyrillicToo_AndMissingGlyphIsABox()
    {
        foreach (var id in new[] { "5x7", "wide" })
            foreach (var (lo, up) in new[] { ('a', 'A'), ('g', 'G'), ('я', 'Я'), ('ф', 'Ф'), ('ж', 'Ж') })
                Assert.NotEqual(PixelText.Render(up.ToString(), White, new TextStyle(Font: id)).Image!.Pixels,
                                PixelText.Render(lo.ToString(), White, new TextStyle(Font: id)).Image!.Pixels);
        var box = PixelText.Render("€", White);
        Assert.NotNull(box.Image);
        Assert.Equal(new[] { '€' }, box.Layout.Missing);
        Assert.Equal(PixelFonts.Default.CellWidth, box.Image!.Width);
        var wide = PixelText.Render("A", White, new TextStyle(Font: "wide")).Image!;
        Assert.Equal(10, wide.Width);
    }

    [Fact]
    public void PixelFont_AllGlyphsAreFiveBySeven()
    {
        foreach (var ch in "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789.,:;!?-+=/\\()[]_'\"*%<>#АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯ")
            Assert.True(PixelFont.Supports(ch), ch.ToString());
        foreach (var ch in "abcdefghijklmnopqrstuvwxyzабвгдеёжзийклмнопрстуфхцчшщъыьэюя")
            Assert.True(PixelFont.Supports(ch), ch.ToString());
        Assert.Equal(5, PixelFont.GlyphWidth);
    }
}

public class SceneTests
{
    private static readonly uint Red = Pixel.Rgb(255, 0, 0), Black = Pixel.Rgb(0, 0, 0);

    [Fact]
    public void Person_FromPrimitives_HasExpectedParts()
    {
        var img = Scene.Render("""
        { "size": [16, 24],
          "palette": { "skin": "#F1C27D", "coat": "#3B5BA5" },
          "items": [
            { "ellipse": [5, 1, 6, 6], "color": "skin" },
            { "rect": [4, 8, 8, 9], "color": "coat" },
            { "line": [4, 9, 1, 16], "color": "skin", "thickness": 2 },
            { "pixels": [[7, 3], [9, 3]], "color": "#000000" } ] }
        """);
        Assert.Equal((16, 24), (img.Width, img.Height));
        Assert.Equal(Pixel.Rgb(0x3B, 0x5B, 0xA5), img.Pixels[12 * 16 + 8]); // body
        Assert.Equal(Pixel.Rgb(0xF1, 0xC2, 0x7D), img.Pixels[4 * 16 + 8]); // head
        Assert.Equal(Black, img.Pixels[3 * 16 + 7]); // eye
        Assert.True(Pixel.IsEmpty(img.Pixels[0])); // the background is transparent
    }

    [Fact]
    public void Mirror_RepeatsShapesAcrossCenter_AndOutlineSurroundsDrawing()
    {
        var img = Scene.Render("""{ "size":[10,6], "mirror":"x", "items":[ { "rect":[1,2,2,2], "color":"#FF0000" } ] }""");
        Assert.Equal(Red, img.Pixels[2 * 10 + 1]);
        Assert.Equal(Red, img.Pixels[2 * 10 + 8]); // the mirrored copy on the right
        Assert.True(Pixel.IsEmpty(img.Pixels[2 * 10 + 5]));

        var framed = Scene.Render("""{ "size":[8,8], "outline":"#000000", "items":[ { "rect":[3,3,2,2], "color":"#FF0000" } ] }""");
        Assert.Equal(Black, framed.Pixels[2 * 8 + 3]);
        Assert.Equal(Red, framed.Pixels[3 * 8 + 3]);
    }

    [Fact]
    public void OutlineOnly_Poly_Fill_AndClear()
    {
        var img = Scene.Render("""
        { "size":[12,12], "items":[
          { "rect":[1,1,10,10], "color":"#FF0000" },
          { "rect":[3,3,6,6], "color":"clear" },
          { "poly":[[5,0],[11,11],[0,11]], "color":"#000000", "fill":false } ] }
        """);
        Assert.True(Pixel.IsEmpty(img.Pixels[6 * 12 + 6])); // knocked out
        Assert.Equal(Red, img.Pixels[1 * 12 + 1]);
        Assert.Equal(Black, img.Pixels[11 * 12 + 5]); // the outline line of the triangle

        var flood = Scene.Render("""{ "size":[6,6], "items":[ { "rect":[2,2,2,2], "color":"#FF0000" }, { "fill":[0,0], "color":"#0000FF" } ] }""");
        Assert.Equal(Pixel.Rgb(0, 0, 255), flood.Pixels[0]);
        Assert.Equal(Red, flood.Pixels[2 * 6 + 2]);
    }

    [Fact]
    public void Errors_NameTheShape()
    {
        var ex = Assert.Throws<ArgumentException>(() => Scene.Render("""{ "size":[8,8], "items":[ { "rect":[1,1,2,2], "color":"#FF0000" }, { "rect":[1,1,2], "color":"red" } ] }"""));
        Assert.Contains("фигура 2", ex.Message);
        Assert.Contains("rect", ex.Message);

        var color = Assert.Throws<ArgumentException>(() => Scene.Render("""{ "size":[8,8], "items":[ { "rect":[1,1,2,2], "color":"синий" } ] }"""));
        Assert.Contains("неизвестный цвет", color.Message);
        Assert.Throws<ArgumentException>(() => Scene.Render("""{ "items":[] }"""));
        Assert.Throws<ArgumentException>(() => Scene.Render("""{ "size":[9999,8], "items":[] }"""));
    }
}

public class BackgroundTests
{
    private static readonly uint White = Pixel.Rgb(255, 255, 255), Red = Pixel.Rgb(255, 0, 0), Black = Pixel.Rgb(0, 0, 0);

    private static PixelImage Frame()
    {
        var img = new PixelImage(12, 12);
        for (int i = 0; i < img.Pixels.Length; i++) img.Pixels[i] = White;
        for (int y = 3; y < 9; y++)
            for (int x = 3; x < 9; x++) img.Set(x, y, Red);
        img.Set(5, 5, White); // an eye white inside the drawing
        return img;
    }

    [Fact]
    public void RemoveBackground_EdgesOnly_KeepsInnerAreasOfSameColor()
    {
        var r = Background.Remove(Frame(), null, 10, edgesOnly: true);
        Assert.True(Pixel.IsEmpty(r.Pixels[0]));
        Assert.Equal(Red, r.Pixels[4 * 12 + 4]);
        Assert.Equal(White, r.Pixels[5 * 12 + 5]); // the inner one stays

        var all = Background.Remove(Frame(), null, 10, edgesOnly: false);
        Assert.True(Pixel.IsEmpty(all.Pixels[5 * 12 + 5]));
    }

    [Fact]
    public void RemoveBackground_UsesToleranceAndExplicitColor()
    {
        var img = new PixelImage(4, 1);
        img.Pixels[0] = Pixel.Rgb(250, 250, 250);
        img.Pixels[1] = Pixel.Rgb(240, 240, 240);
        img.Pixels[2] = Red;
        img.Pixels[3] = Pixel.Rgb(251, 251, 251);
        var r = Background.Remove(img, White, 16, true);
        Assert.True(Pixel.IsEmpty(r.Pixels[0]) && Pixel.IsEmpty(r.Pixels[1]) && Pixel.IsEmpty(r.Pixels[3]));
        Assert.Equal(Red, r.Pixels[2]);
        Assert.Equal(Red, Background.Remove(img, Black, 12, true).Pixels[2]); // a black background erases nothing
    }

    [Fact]
    public void CleanEdges_RemovesHaloSpecksAndFillsGaps()
    {
        var img = new PixelImage(12, 12);
        for (int y = 2; y < 10; y++)
            for (int x = 2; x < 10; x++) img.Set(x, y, Red);
        for (int x = 2; x < 10; x++) img.Set(x, 2, Black);   // a black halo on top
        img.Set(0, 0, Red);                                    // a lone dot
        img.Set(6, 9, Pixel.Empty);                            // a gap in the bottom edge
        var r = Background.CleanEdges(img, Black, 40, 1, specks: true, fillGaps: true, minNeighbors: 5);
        Assert.True(Pixel.IsEmpty(r.Pixels[2 * 12 + 5]));     // the halo is erased
        Assert.True(Pixel.IsEmpty(r.Pixels[0]));               // the dot is removed
        Assert.Equal(Red, r.Pixels[9 * 12 + 6]);               // the gap is closed with the neighbor color
        Assert.Equal(Red, r.Pixels[5 * 12 + 5]);
    }

    [Fact]
    public void Operations_AreRegistered_AndTemplateRuns()
    {
        Assert.Contains(Operations.All, o => o.Name == "removeBackground");
        Assert.Contains(Operations.All, o => o.Name == "cleanEdges");
        var t = BuiltInTemplates.All.First(x => x.Id == "system:remove-background");
        var res = Pipeline.Parse(t.Json).Run(Frame());
        Assert.True(res.Ok);
        Assert.True(Pixel.IsEmpty(res.Image.Pixels[0]));
    }
}
