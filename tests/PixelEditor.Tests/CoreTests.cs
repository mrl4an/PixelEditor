// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using PixelEditor.Core;
using PixelEditor.Core.Ops;

namespace PixelEditor.Tests;

public class CoreTests
{
    private static readonly uint Red = Pixel.Rgb(255, 0, 0);
    private static readonly uint Blue = Pixel.Rgb(0, 0, 255);

    [Fact]
    public void Batch_UndoRedo_RestoresPixels()
    {
        var doc = new Document(4, 4);
        var layer = doc.Layers[0];
        var batch = new PixelBatch(layer);
        batch.Set(1, 1, Red);
        batch.Set(2, 2, Blue);
        doc.History.Commit(batch);

        Assert.Equal(Red, layer.Get(1, 1));
        doc.History.Undo();
        Assert.Equal(Pixel.Empty, layer.Get(1, 1));
        Assert.Equal(Pixel.Empty, layer.Get(2, 2));
        doc.History.Redo();
        Assert.Equal(Blue, layer.Get(2, 2));
    }

    [Fact]
    public void Batch_Revert_ForShapePreview()
    {
        var doc = new Document(4, 4);
        var batch = new PixelBatch(doc.Layers[0]);
        batch.Set(0, 0, Red);
        batch.Revert();
        Assert.False(batch.HasChanges);
        Assert.Equal(Pixel.Empty, doc.Layers[0].Get(0, 0));
    }

    [Fact]
    public void Layers_AddRemoveMergeUndo()
    {
        var doc = new Document(2, 2);
        var bottom = doc.Layers[0];
        var top = doc.AddLayer("top");
        top.Pixels[0] = Red;
        Assert.Equal(2, doc.Layers.Count);

        Assert.True(doc.MergeDown(top));
        Assert.Single(doc.Layers);
        Assert.Equal(Red, bottom.Pixels[0]);

        doc.History.Undo();
        Assert.Equal(2, doc.Layers.Count);
        Assert.Equal(Pixel.Empty, bottom.Pixels[0]);
    }

    [Fact]
    public void Flatten_UpperLayerWins_HiddenSkipped()
    {
        var doc = new Document(1, 1);
        doc.Layers[0].Pixels[0] = Red;
        var top = doc.AddLayer("top");
        top.Pixels[0] = Blue;
        Assert.Equal(Blue, doc.Flatten().Pixels[0]);
        top.Visible = false;
        Assert.Equal(Red, doc.Flatten().Pixels[0]);
    }

    [Fact]
    public void FloodFill_FillsOnlyConnectedRegion()
    {
        var doc = new Document(5, 1);
        var layer = doc.Layers[0];
        layer.Pixels[2] = Blue; // wall
        var batch = new PixelBatch(layer);
        Drawing.FloodFill(batch, 0, 0, Red);
        Assert.Equal(Red, layer.Get(0, 0));
        Assert.Equal(Red, layer.Get(1, 0));
        Assert.Equal(Blue, layer.Get(2, 0));
        Assert.Equal(Pixel.Empty, layer.Get(3, 0));
    }

    [Fact]
    public void ColorStats_CountsAndReplace()
    {
        var doc = new Document(3, 1);
        doc.Layers[0].Pixels[0] = Red;
        doc.Layers[0].Pixels[1] = Red;
        doc.Layers[0].Pixels[2] = Blue;
        var stats = ColorStats.Count(doc.Flatten());
        Assert.Equal(new ColorCount(Red, 2), stats[0]);
        Assert.Equal(new ColorCount(Blue, 1), stats[1]);

        Assert.Equal(2, ColorStats.ReplaceColor(doc, Red, Blue));
        Assert.Single(ColorStats.Count(doc.Flatten()));
        doc.History.Undo();
        Assert.Equal(2, ColorStats.Count(doc.Flatten()).Count);
    }

    [Fact]
    public void ProjectIO_RoundTrip()
    {
        var doc = new Document(3, 2);
        doc.Layers[0].Pixels[4] = Red;
        var l2 = doc.AddLayer("второй");
        l2.Visible = false;
        l2.Pixels[0] = Blue;

        using var ms = new MemoryStream();
        ProjectIO.Save(doc, ms);
        ms.Position = 0;
        var back = ProjectIO.Load(ms);

        Assert.Equal(2, back.Layers.Count);
        Assert.Equal("второй", back.Layers[1].Name);
        Assert.False(back.Layers[1].Visible);
        Assert.Equal(Red, back.Layers[0].Pixels[4]);
        Assert.Equal(Blue, back.Layers[1].Pixels[0]);
    }

    [Fact]
    public void Resize_Mode_KeepsOnlyExistingColors()
    {
        var img = new PixelImage(4, 4);
        for (int i = 0; i < 16; i++) img.Pixels[i] = i % 5 == 0 ? Blue : Red;
        var small = Resampler.Resize(img, 2, 2, ResizeMethod.Mode);
        Assert.All(small.Pixels, p => Assert.True(p == Red || p == Blue));
    }

    [Fact]
    public void Resize_Box_AveragesColors()
    {
        var img = new PixelImage(2, 1, [Pixel.Rgb(0, 0, 0), Pixel.Rgb(200, 100, 50)]);
        var one = Resampler.Resize(img, 1, 1, ResizeMethod.Box);
        Assert.Equal(Pixel.Rgb(100, 50, 25), one.Pixels[0]);
    }

    [Fact]
    public void Resize_Box_IgnoresTransparentPixelsInColor()
    {
        var img = new PixelImage(2, 1, [Pixel.Empty, Red]);
        var one = Resampler.Resize(img, 1, 1, ResizeMethod.Box);
        Assert.Equal(255, Pixel.R(one.Pixels[0]));
        Assert.Equal(0, Pixel.G(one.Pixels[0]));
        Assert.Equal(128, Pixel.A(one.Pixels[0]));
    }

    [Fact]
    public void Quantize_ReducesColorCount()
    {
        var img = new PixelImage(16, 16);
        for (int i = 0; i < img.Pixels.Length; i++) img.Pixels[i] = Pixel.Rgb((byte)i, (byte)(255 - i), (byte)(i / 2));
        var pal = Quantizer.BuildPalette(img, 4);
        Assert.True(pal.Count <= 4);
        var q = Quantizer.Apply(img, pal, dither: false);
        Assert.True(q.DistinctColors() <= 4);
    }

    [Fact]
    public void RemoveIslands_RemovesSinglePixelsKeepsBigShapes()
    {
        var img = new PixelImage(6, 6);
        img.Set(0, 0, Red); // junk
        for (int y = 2; y < 5; y++) for (int x = 2; x < 5; x++) img.Set(x, y, Blue);
        var clean = Cleanup.RemoveIslands(img, 1);
        Assert.Equal(Pixel.Empty, clean.Get(0, 0));
        Assert.Equal(9, clean.OpaqueCount());
    }

    [Fact]
    public void FillHoles_FillsEnclosedOnly()
    {
        var img = new PixelImage(5, 5);
        for (int y = 0; y < 5; y++) for (int x = 0; x < 5; x++) img.Set(x, y, Red);
        img.Set(2, 2, Pixel.Empty);   // a hole inside
        img.Set(0, 0, Pixel.Empty);   // at the edge: not a hole
        var filled = Cleanup.FillHoles(img);
        Assert.Equal(Red, filled.Get(2, 2));
        Assert.Equal(Pixel.Empty, filled.Get(0, 0));
    }

    [Fact]
    public void Outline_Outside_AddsRingAroundShape()
    {
        var img = new PixelImage(3, 3);
        img.Set(1, 1, Red);
        var padded = Transform.Pad(img, 1, 1, 1, 1);
        var o = Outline.Apply(padded, Blue, OutlineMode.Outside, diagonal: false);
        Assert.Equal(Red, o.Get(2, 2));
        Assert.Equal(Blue, o.Get(1, 2));
        Assert.Equal(Blue, o.Get(2, 1));
        Assert.Equal(Pixel.Empty, o.Get(1, 1)); // a diagonal is not outlined with 4-connectivity
        Assert.Equal(5, o.OpaqueCount());
    }

    [Fact]
    public void Pipeline_RunsStepsAndReportsErrors()
    {
        var img = new PixelImage(8, 8);
        for (int i = 0; i < 64; i++) img.Pixels[i] = Red;

        var ok = Pipeline.Parse("""
            { "name": "t", "steps": [
              { "op": "resize", "width": 4, "method": "mode" },
              { "op": "outline", "color": "#000000" }
            ] }
            """).Run(img);
        Assert.True(ok.Ok);
        Assert.Equal(6, ok.Image.Width);
        Assert.Equal(2, ok.Log.Count);

        var bad = Pipeline.Parse("""[ { "op": "resize", "width": 4 }, { "op": "nope" } ]""").Run(img);
        Assert.False(bad.Ok);
        Assert.Equal(4, bad.Image.Width);
        Assert.Contains("Неизвестная операция", bad.Log[^1].Error);
    }

    [Fact]
    public void Rotate90_RotatesClockwise()
    {
        var img = new PixelImage(2, 1, [Red, Blue]);
        var r = Transform.Rotate90(img, 1);
        Assert.Equal(1, r.Width);
        Assert.Equal(2, r.Height);
        Assert.Equal(Red, r.Get(0, 0));
        Assert.Equal(Blue, r.Get(0, 1));
    }
}

public class MirrorBrushTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void MirroredStamp_IsAnExactReflection_ForOddAndEvenBrushSizes(int size)
    {
        var layer = new Layer(16, 16, "t");
        var batch = new PixelBatch(layer);
        var ctx = new PaintContext(batch) { Color = 0xFFFF0000, BrushSize = size, MirrorX = true, MirrorY = true };
        ctx.Plot(3, 5);
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
            {
                Assert.Equal(layer.Get(x, y), layer.Get(15 - x, y));      // left to right
                Assert.Equal(layer.Get(x, y), layer.Get(x, 15 - y));      // top to bottom
            }
        Assert.Equal(4 * size * size, layer.Pixels.Count(p => p == 0xFFFF0000u));
    }
}
