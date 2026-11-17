// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using PixelEditor.Core;

namespace PixelEditor.Tests;

public class HsvTests
{
    [Fact]
    public void RoundTrip_EveryCoarseColorComesBackExactly()
    {
        for (int r = 0; r < 256; r += 5)
            for (int g = 0; g < 256; g += 5)
                for (int b = 0; b < 256; b += 5)
                {
                    uint c = Pixel.Rgb((byte)r, (byte)g, (byte)b);
                    var (h, s, v) = Hsv.FromRgb(c);
                    Assert.Equal(c, Hsv.ToRgb(h, s, v));
                }
    }

    [Theory]
    [InlineData(0, 255, 0, 0)]
    [InlineData(60, 255, 255, 0)]
    [InlineData(120, 0, 255, 0)]
    [InlineData(180, 0, 255, 255)]
    [InlineData(240, 0, 0, 255)]
    [InlineData(300, 255, 0, 255)]
    public void PureHues_MapToPrimaryAndSecondaryColors(double hue, int r, int g, int b)
    {
        Assert.Equal(Pixel.Rgb((byte)r, (byte)g, (byte)b), Hsv.ToRgb(hue, 1, 1));
        Assert.Equal(hue, Hsv.FromRgb(Pixel.Rgb((byte)r, (byte)g, (byte)b)).H, 6);
    }

    [Fact]
    public void Greys_HaveNoSaturationAndBlackHasNoValue()
    {
        var (_, s, v) = Hsv.FromRgb(Pixel.Rgb(128, 128, 128));
        Assert.Equal(0, s);
        Assert.Equal(128 / 255.0, v, 6);
        Assert.Equal(Pixel.Rgb(128, 128, 128), Hsv.ToRgb(200, 0, 128 / 255.0));
        Assert.Equal((0.0, 0.0, 0.0), Hsv.FromRgb(Pixel.Rgb(0, 0, 0)));
        Assert.Equal(Pixel.Rgb(0, 0, 0), Hsv.ToRgb(123, 1, 0));
        Assert.Equal(Pixel.Rgb(255, 255, 255), Hsv.ToRgb(77, 0, 1));
    }

    [Fact]
    public void ToRgb_WrapsHueAndClampsTheRest()
    {
        Assert.Equal(Hsv.ToRgb(0, 1, 1), Hsv.ToRgb(360, 1, 1));
        Assert.Equal(Hsv.ToRgb(240, 1, 1), Hsv.ToRgb(-120, 1, 1));
        Assert.Equal(Hsv.ToRgb(10, 1, 1), Hsv.ToRgb(10, 5, 9));
    }
}

public class ShadeRampTests
{
    private static readonly uint[] Ordinary =
    [
        Pixel.Rgb(200, 60, 60), Pixel.Rgb(60, 160, 80), Pixel.Rgb(70, 90, 200), Pixel.Rgb(220, 180, 60),
        Pixel.Rgb(128, 64, 160), Pixel.Rgb(90, 90, 90),
    ];

    [Fact]
    public void Ordinary_GivesFiveDistinctColorsOrderedByBrightnessWithTheBaseInTheMiddle()
    {
        foreach (var c in Ordinary)
        {
            var ramp = PaletteTools.ShadeRamp(c);
            Assert.Equal(5, ramp.Count);
            Assert.Equal(5, ramp.Distinct().Count());
            Assert.Equal(c, ramp[2]);
            for (int i = 1; i < ramp.Count; i++) Assert.True(PaletteTools.Luma(ramp[i]) > PaletteTools.Luma(ramp[i - 1]));
            Assert.All(ramp, p => Assert.Equal(255, Pixel.A(p)));
        }
    }

    [Fact]
    public void Shadows_ShiftTowardBlueAndHighlightsTowardYellow()
    {
        var ramp = PaletteTools.ShadeRamp(Pixel.Rgb(200, 60, 60)); // red: hue 0
        double dark = PaletteTools.HueSat(ramp[0]).Hue, light = PaletteTools.HueSat(ramp[4]).Hue;
        Assert.True(dark > 300, $"dark hue {dark} should move from red toward purple (below 360)");
        Assert.InRange(light, 1, 60);
        // saturation: more in the shadows, less in the highlights
        Assert.True(PaletteTools.HueSat(ramp[0]).Sat > PaletteTools.HueSat(ramp[2]).Sat);
        Assert.True(PaletteTools.HueSat(ramp[4]).Sat < PaletteTools.HueSat(ramp[2]).Sat);
    }

    [Fact]
    public void Grey_StaysNeutral()
    {
        var ramp = PaletteTools.ShadeRamp(Pixel.Rgb(100, 100, 100));
        Assert.Equal(5, ramp.Count);
        Assert.All(ramp, p => Assert.True(Pixel.R(p) == Pixel.G(p) && Pixel.G(p) == Pixel.B(p)));
        Assert.Contains(Pixel.Rgb(100, 100, 100), ramp);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(255u)]
    public void BlackAndWhite_GiveAValidOrderedRampThatIncludesTheBase(uint level)
    {
        uint c = Pixel.Rgb((byte)level, (byte)level, (byte)level);
        var ramp = PaletteTools.ShadeRamp(c);
        Assert.Contains(c, ramp);
        Assert.InRange(ramp.Count, 3, 5);
        Assert.Equal(ramp.Count, ramp.Distinct().Count());
        for (int i = 1; i < ramp.Count; i++) Assert.True(PaletteTools.Luma(ramp[i]) > PaletteTools.Luma(ramp[i - 1]));
    }
}
