// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using PixelEditor.Core;
using PixelEditor.Core.Ops;

namespace PixelEditor.Tests;

public class LightingTests
{
    private static readonly uint Red = Pixel.Rgb(217, 83, 79), Ink = Pixel.Rgb(24, 22, 36);

    private static PixelImage Disc(int size, double r, uint color)
    {
        var img = new PixelImage(size, size);
        double c = (size - 1) / 2.0;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                if ((x - c) * (x - c) + (y - c) * (y - c) <= r * r) img.Set(x, y, color);
        return img;
    }

    private static PixelImage Light(PixelImage img, string from, Action<LightOptions>? tweak = null)
    {
        var o = new LightOptions { ToLight = Lighting.ToLight(from) };
        tweak?.Invoke(o);
        return Lighting.Light(img, o);
    }

    [Theory]
    [InlineData("N")] [InlineData("NE")] [InlineData("E")] [InlineData("SE")]
    [InlineData("S")] [InlineData("SW")] [InlineData("W")] [InlineData("NW")]
    public void LightSideIsBrighterThanShadowSide(string from)
    {
        var img = Disc(36, 15, Red);
        var lit = Light(img, from);
        var (dx, dy) = Lighting.ToLight(from);
        double near = 0, far = 0; int nn = 0, nf = 0;
        double c = 17.5;
        for (int y = 0; y < 36; y++)
            for (int x = 0; x < 36; x++)
            {
                if (Pixel.IsEmpty(lit.Get(x, y))) continue;
                double side = (x - c) * dx + (y - c) * dy;
                if (side > 4) { near += PaletteTools.Luma(lit.Get(x, y)); nn++; }
                else if (side < -4) { far += PaletteTools.Luma(lit.Get(x, y)); nf++; }
            }
        Assert.True(nn > 20 && nf > 20);
        Assert.True(near / nn > far / nf + 15, $"{from}: lit {near / nn:F1} vs shadow {far / nf:F1}");
    }

    [Fact]
    public void SilhouetteAndTransparencyAreKept_InputIsNotChanged()
    {
        var img = Disc(36, 15, Red);
        img.Set(17, 17, Pixel.Empty); // a hole
        var copy = img.Clone();
        var lit = Light(img, "NW", o => o.Rim = true);
        Assert.Equal(copy.Pixels, img.Pixels);
        for (int i = 0; i < img.Pixels.Length; i++)
            Assert.Equal(Pixel.IsEmpty(img.Pixels[i]), Pixel.IsEmpty(lit.Pixels[i]));
        Assert.NotEqual(img.Pixels, lit.Pixels);
        Assert.Equal(lit.Pixels, Light(img, "NW", o => o.Rim = true).Pixels); // deterministic
    }

    [Theory]
    [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void NumberOfColorsIsBoundedByBandsTimesBaseColors(int bands)
    {
        var img = Disc(40, 18, Red);
        for (int y = 14; y < 26; y++) for (int x = 14; x < 26; x++) img.Set(x, y, Pixel.Rgb(90, 140, 220));
        var lit = Light(img, "NW", o => { o.Bands = bands; o.Rim = true; });
        Assert.True(lit.DistinctColors() <= (bands + 1) * 2, $"{lit.DistinctColors()} colors");
        Assert.True(lit.DistinctColors() > 2);
    }

    [Fact]
    public void Outline_IsKept_AndNotPartOfTheVolume()
    {
        var body = Disc(30, 12, Pixel.Rgb(86, 170, 90));
        var img = body.Clone();
        for (int y = 0; y < 30; y++)
            for (int x = 0; x < 30; x++)
                if (Pixel.IsEmpty(body.Get(x, y)) && (!Pixel.IsEmpty(body.Get(x - 1, y)) || !Pixel.IsEmpty(body.Get(x + 1, y)) ||
                    !Pixel.IsEmpty(body.Get(x, y - 1)) || !Pixel.IsEmpty(body.Get(x, y + 1)))) img.Set(x, y, Ink);
        var lit = Light(img, "NW");
        int outline = 0;
        for (int i = 0; i < img.Pixels.Length; i++)
            if (img.Pixels[i] == Ink) { Assert.Equal(Ink, lit.Pixels[i]); outline++; }
        Assert.True(outline > 20);
        Assert.NotEqual(body.Pixels, Light(body, "NW").Pixels);
        // the body inside the outline is still lit from the NW
        Assert.True(PaletteTools.Luma(lit.Get(5, 15)) > PaletteTools.Luma(lit.Get(24, 15)));
    }

    [Fact]
    public void ThinShapes_AreNotShadedTwice()
    {
        var img = new PixelImage(30, 12);
        for (int x = 3; x < 27; x++) img.Set(x, 3, Red);                   // 1 px line: flat
        for (int x = 3; x < 27; x++) { img.Set(x, 8, Red); img.Set(x, 9, Red); } // 2 px bar
        var lit = Light(img, "NW");
        for (int x = 3; x < 27; x++) Assert.Equal(Red, lit.Get(x, 3));
        // every pixel has exactly one color from the ramp: the bar may split into a lit and a shadow row, never both in one pixel
        for (int x = 6; x < 24; x++) Assert.True(PaletteTools.Luma(lit.Get(x, 8)) >= PaletteTools.Luma(lit.Get(x, 9)));
        Assert.True(Enumerable.Range(0, 30).All(x => lit.Get(x, 0) == Pixel.Empty));
    }

    [Fact]
    public void CanvasBorderIsNotAnEdge()
    {
        var img = new PixelImage(24, 24);
        for (int y = 4; y < 20; y++) for (int x = 0; x < 12; x++) img.Set(x, y, Red);
        var lit = Light(img, "W"); // light from the left: the left border of the canvas must not get a highlight rim
        Assert.Equal(Red, lit.Get(0, 12));
        Assert.NotEqual(Red, lit.Get(11, 12)); // the real right edge is in shadow
    }

    [Fact]
    public void PaletteMode_NeverLeavesThePalette_EvenWhenAppliedTwice()
    {
        var pal = PaletteLibrary.Default.Find("PICO-8")!.Colors.Select(c => c | 0xFF000000u).ToList();
        var img = Disc(40, 17, pal[8]);
        for (int y = 14; y < 26; y++) for (int x = 14; x < 26; x++) img.Set(x, y, pal[12]);
        var once = Light(img, "NW", o => o.Palette = pal);
        var twice = Light(once, "SE", o => o.Palette = pal);
        foreach (var p in once.Pixels.Concat(twice.Pixels).Where(p => !Pixel.IsEmpty(p)))
            Assert.Contains(p, pal);
        Assert.True(once.DistinctColors() > 2);
        Assert.True(twice.DistinctColors() <= pal.Count);
    }

    [Fact]
    public void LegacyEdgeShade_StillLightsNorthWestAndShadesSouthEast()
    {
        var img = Disc(30, 12, Red);
        var lit = Lighting.EdgeShade(img, Lighting.ToLight("NW"), 1, 0.2, 0.25);
        Assert.True(PaletteTools.Luma(lit.Get(7, 11)) > PaletteTools.Luma(Red));
        Assert.True(PaletteTools.Luma(lit.Get(22, 18)) < PaletteTools.Luma(Red));
        Assert.Throws<ArgumentException>(() => Lighting.ToLight("up"));
    }

    [Fact]
    public void AllBlackOrFullyOpaquePicture_DoesNotCrash()
    {
        var full = new PixelImage(8, 8);
        for (int i = 0; i < 64; i++) full.Pixels[i] = Red;
        Assert.Equal(full.Pixels, Light(full, "NW", o => o.PerRegion = 0).Pixels);
        var empty = new PixelImage(8, 8);
        Assert.Equal(empty.Pixels, Light(empty, "NW").Pixels);
    }
}
