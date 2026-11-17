// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using PixelEditor.Core;
using PixelEditor.Core.Ops;

namespace PixelEditor.Tests;

public class SpriteStyleTests
{
    private const uint Ink = 0xFF252030, Wood = 0xFFB78146;

    [Fact]
    public void ProfileRoundTripsAndFitsWithinFinalCanvasAndPalette()
    {
        var reference = new PixelImage(10, 9);
        Drawing.Rectangle(1, 2, 8, 7, false, (x, y) => reference.Set(x, y, Ink));
        Drawing.Rectangle(2, 3, 7, 6, true, (x, y) => reference.Set(x, y, Wood));
        var extracted = SpriteStyle.Extract(reference, 2);
        var profile = SpriteStyle.Parse(SpriteStyle.ToJson(extracted));
        Assert.Equal(new PixelRect(1, 2, 8, 6), profile.Content);
        Assert.Equal(1, profile.OutlineThickness);
        Assert.Equal("#252030", profile.OutlineColor);

        var source = new PixelImage(4, 7);
        Array.Fill(source.Pixels, Wood);
        var result = SpriteStyle.Apply(source, profile);
        Assert.Equal((10, 9), (result.Width, result.Height));
        Assert.All(result.Pixels, p => Assert.Contains(p, new[] { 0u, Ink, Wood }));
        Assert.Contains(Ink, result.Pixels.Skip(7 * result.Width).Take(result.Width));
        for (int y = 0; y < result.Height; y++)
            for (int x = 0; x < result.Width; x++)
                if (!profile.Content.Contains(x, y)) Assert.Equal(0u, result.Get(x, y));
    }

    [Fact]
    public void FlatReferenceDoesNotSuggestAnOutline()
    {
        var reference = new PixelImage(8, 8);
        Array.Fill(reference.Pixels, Wood);
        Assert.Equal(0, SpriteStyle.Extract(reference).OutlineThickness);
    }

    [Fact]
    public void GroupsApplyLocalOffsetsAndRoundCorners()
    {
        var image = Scene.Render("""
            {"size":[12,10],"items":[
              {"offset":[5,2],"group":[{"rect":[0,0,5,4],"radius":1,"color":"#B78146"}]},
              {"offset":[1,7],"group":[{"offset":[2,0],"group":[{"pixels":[[0,0]],"color":"#252030"}]}]}
            ]}
            """);
        Assert.Equal(0u, image.Get(5, 2));
        Assert.Equal(Wood, image.Get(6, 2));
        Assert.Equal(Wood, image.Get(5, 3));
        Assert.Equal(Ink, image.Get(3, 7));
    }

    [Fact]
    public void SimplifyUsesLocalNeighborsAndRetainsContrastAndSilhouette()
    {
        const uint speck = 0xFFB88045;
        var source = new PixelImage(5, 2, [Wood, Wood, speck, 0, speck, Wood, Wood, Ink, 0, 0]);
        var result = ColorRegions.Simplify(source, maxArea: 1, tolerance: 10);
        Assert.Equal(Wood, result.Get(2, 0));
        Assert.Equal(speck, result.Get(4, 0));
        Assert.Equal(Ink, result.Get(2, 1));
        Assert.Equal(source.OpaqueCount(), result.OpaqueCount());
        Assert.Equal(speck, source.Get(2, 0));
    }
}
