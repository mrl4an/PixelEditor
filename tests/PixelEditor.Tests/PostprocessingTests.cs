// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using PixelEditor.Core;
using PixelEditor.Core.Ops;

namespace PixelEditor.Tests;

public class PostprocessingTests
{
    [Fact]
    public void PatchAndProtectionAreAppliedBeforeChecksAndPreserveInput()
    {
        var source = new PixelImage(4, 1, [0xFFFF0000, 0xFFFF0000, 0, 0xFFFF0000]);
        var mask = new PixelImage(4, 1, [0, 0xFFFFFFFF, 0, 0]);
        var pipeline = Pipeline.Parse("""
            {"steps":[{"op":"patchPixels","edits":[[0,0,"#00FF00"],[1,0,"transparent"],[2,0,"#00FF00"],[3,0,"transparent"]]}],
             "checks":[{"check":"opaquePixels","min":3,"max":3}]}
            """);

        var result = pipeline.Run(source, mask);
        Assert.True(result.Success);
        Assert.Equal(0xFFFF0000u, result.Image.Get(1, 0));
        Assert.Equal(0u, source.Get(2, 0));
        var diff = ImageComparison.Compare(source, result.Image);
        Assert.Equal((1, 1, 1), (diff.Recolored, diff.Added, diff.Removed));
    }

    [Fact]
    public void MaskRejectsResizeAndBadPatchReportsStepError()
    {
        var source = new PixelImage(2, 2);
        var mask = new PixelImage(2, 2);
        Assert.False(Pipeline.Parse("""[{"op":"resize","width":1}]""").Run(source, mask).Ok);
        Assert.False(Pipeline.Parse("""[{"op":"patchPixels","edits":[[3,0,"#FFF"]]}]""").Run(source).Ok);
    }

    [Fact]
    public void SharedPaletteUsesMultipleFramesAndReservesAccents()
    {
        const uint red = 0xFFFF0000, blue = 0xFF0000FF, green = 0xFF00FF00;
        var first = new PixelImage(1, 1, [red]);
        var second = new PixelImage(1, 1, [blue]);
        var palette = Quantizer.BuildSharedPalette([first, second], 3, [green]);
        Assert.Equal(new[] { blue, green, red }, palette);
        Assert.Equal(new[] { green }, Quantizer.BuildSharedPalette([first, second], 1, [green]));
    }

    [Fact]
    public void GlobalColorReplacementSkipsLockedLayers()
    {
        const uint red = 0xFFFF0000, blue = 0xFF0000FF;
        var doc = Document.FromImage(new PixelImage(1, 1, [red]));
        var locked = doc.AddLayer("Locked", pixels: [red]);
        locked.Locked = true;
        Assert.Equal(1, ColorStats.ReplaceColor(doc, red, blue));
        Assert.Equal(red, locked.Get(0, 0));
        Assert.Equal(blue, doc.Layers[0].Get(0, 0));
        doc.History.Undo();
        Assert.Equal(red, doc.Layers[0].Get(0, 0));
    }
}
