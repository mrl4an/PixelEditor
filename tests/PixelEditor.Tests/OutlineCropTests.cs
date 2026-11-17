// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using PixelEditor.Core;
using PixelEditor.Core.Ops;

namespace PixelEditor.Tests;

public class OutlineAndCropTests
{
    private static readonly uint Red = Pixel.Rgb(255, 0, 0), Black = Pixel.Rgb(0, 0, 0), Dark = Pixel.Rgb(30, 20, 20);

    private static PixelImage Outlined(int thickness)
    {
        var img = new PixelImage(16, 16);
        for (int y = 3; y < 13; y++)
            for (int x = 3; x < 13; x++)
            {
                int edge = Math.Min(Math.Min(x - 3, 12 - x), Math.Min(y - 3, 12 - y));
                img.Set(x, y, edge < thickness ? Black : Red);
            }
        return img;
    }

    [Fact]
    public void RemoveOutline_AutoDetectsColor_AndSupportsThickOutlines()
    {
        var one = OutlineRemoval.Remove(Outlined(1), null, 30, false, 70, 3, false);
        Assert.True(Pixel.IsEmpty(one.Pixels[3 * 16 + 3]));
        Assert.Equal(Red, one.Pixels[5 * 16 + 5]);
        Assert.Equal(64, one.Pixels.Count(p => p == Red)); // red 8×8 whole

        var two = OutlineRemoval.Remove(Outlined(2), null, 30, false, 70, 3, false);
        Assert.Equal(36, two.Pixels.Count(p => p == Red));
        Assert.DoesNotContain(Black, two.Pixels);
    }

    [Fact]
    public void RemoveOutline_Ragged_DarkMode_RemovesDifferentShades_ButKeepsInnerDark()
    {
        var img = Outlined(1);
        img.Set(3, 5, Dark);                  // another dark shade at the edge
        img.Set(4, 3, Pixel.Rgb(60, 60, 60)); // one more
        img.Set(8, 8, Black);                 // a dark pixel inside (an eye): stays
        var r = OutlineRemoval.Remove(img, null, 5, dark: true, maxLuma: 70, passes: 3, diagonal: false);
        Assert.True(Pixel.IsEmpty(r.Pixels[5 * 16 + 3]));
        Assert.True(Pixel.IsEmpty(r.Pixels[3 * 16 + 4]));
        Assert.Equal(Black, r.Pixels[8 * 16 + 8]);
        Assert.Equal(Red, r.Pixels[6 * 16 + 6]);
    }

    [Fact]
    public void RemoveOutline_Passes_StopWhenNothingMatches()
    {
        var r = OutlineRemoval.Remove(Outlined(1), Black, 10, false, 70, 16, false);
        Assert.Equal(64, r.Pixels.Count(p => p == Red)); // the red is not worn down by extra passes
    }

    [Fact]
    public void Crop_CutsAllLayers_AndUndoRestores()
    {
        var doc = new Document(10, 8);
        doc.Layers[0].Pixels[2 * 10 + 3] = Red;
        doc.AddLayer("b");
        doc.Layers[1].Pixels[5 * 10 + 6] = Black;
        doc.Crop(new PixelRect(3, 2, 4, 4));
        Assert.Equal((4, 4), (doc.Width, doc.Height));
        Assert.All(doc.Layers, l => Assert.Equal(16, l.Pixels.Length));
        Assert.Equal(Red, doc.Layers[0].Pixels[0]);
        Assert.Equal(Black, doc.Layers[1].Pixels[3 * 4 + 3]);

        doc.History.Undo();
        Assert.Equal((10, 8), (doc.Width, doc.Height));
        Assert.Equal(Red, doc.Layers[0].Pixels[2 * 10 + 3]);
        Assert.Throws<ArgumentException>(() => doc.Crop(new PixelRect(50, 50, 3, 3)));
    }

    [Fact]
    public void ContentBounds_FindsDrawing_AcrossLayers()
    {
        var doc = new Document(20, 20);
        Assert.Null(doc.ContentBounds());
        doc.Layers[0].Pixels[4 * 20 + 6] = Red;
        doc.AddLayer("x");
        doc.Layers[1].Pixels[9 * 20 + 12] = Red;
        Assert.Equal(new PixelRect(6, 4, 7, 6), doc.ContentBounds());
    }
}

public class AdjustAndWebTemplateTests
{
    [Fact]
    public void Adjust_ChangesContrastBrightnessSaturation_AndKeepsTransparent()
    {
        var img = new PixelImage(3, 1);
        img.Pixels[0] = Pixel.Rgb(100, 100, 100);
        img.Pixels[1] = Pixel.Rgb(200, 100, 100);
        var bright = Adjust.Apply(img, 20, 0, 0);
        Assert.True(Pixel.R(bright.Pixels[0]) > 100);
        var contrast = Adjust.Apply(img, 0, 50, 0);
        Assert.True(Pixel.R(contrast.Pixels[0]) < 100); // dark gets darker
        Assert.True(Pixel.R(contrast.Pixels[1]) > 200); // light gets lighter
        var gray = Adjust.Apply(img, 0, 0, -100);
        Assert.Equal(Pixel.R(gray.Pixels[1]), Pixel.G(gray.Pixels[1]));
        Assert.True(Pixel.IsEmpty(bright.Pixels[2]));
        Assert.Equal(img.Pixels, Adjust.Apply(img, 0, 0, 0).Pixels);
    }

    [Fact]
    public void WebTemplate_TurnsSmoothPhotoOnFlatBackgroundIntoSmallSprite()
    {
        // a "photo": a smooth circle on a plain background
        var photo = new PixelImage(200, 160);
        for (int y = 0; y < 160; y++)
            for (int x = 0; x < 200; x++)
            {
                double d = Math.Sqrt((x - 100) * (x - 100) + (y - 80) * (y - 80));
                photo.Set(x, y, d < 60 ? Pixel.Rgb((byte)(120 + d), (byte)(60 + x / 3), (byte)(200 - d)) : Pixel.Rgb(235, 240, 245));
            }
        // the old template is now an alias of "photo to pixel art" with background=complex, outline=colored
        var (t, preset) = new PipelineLibrary(Path.Combine(Path.GetTempPath(), "pixed-none")).FindWithPreset("web-image-to-pixel-art")!.Value;
        var res = Pipeline.Parse(t.Json).WithPreset(preset).Run(photo);
        Assert.True(res.Ok, string.Join("; ", res.Log.Where(l => l.Error != null).Select(l => l.Error)));
        Assert.True(res.Image.Width <= 66 && res.Image.Height <= 66);
        Assert.True(res.Image.DistinctColors() <= 32);
        Assert.True(res.ChecksPassed, string.Join("; ", res.Checks.Where(c => !c.Passed).Select(c => c.Check + ": " + c.Message)));
        Assert.True(Pixel.IsEmpty(res.Image.Pixels[0])); // the background is gone
    }
}
