// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using PixelEditor.Core;
using PixelEditor.ViewModels;

namespace PixelEditor.ViewModels.Tests;

public class FillAndPixelPerfectTests
{
    [Fact]
    public void Fill_Tolerance_CoversNearColors_AndWholeLayerSkipsConnectivity()
    {
        using var rig = new Rig();
        var c = rig.Vm.Canvas; rig.Vm.Colors.SetPrimary(Rig.Red);
        uint a = Pixel.Rgb(100, 100, 100), near = Pixel.Rgb(110, 105, 100), far = Pixel.Rgb(200, 0, 0);
        rig.Layer.Pixels[5 * 32 + 5] = a;
        rig.Layer.Pixels[5 * 32 + 6] = near;
        rig.Layer.Pixels[5 * 32 + 7] = far;
        rig.Layer.Pixels[5 * 32 + 8] = a;               // behind a "far" pixel: not connected

        c.Tool = Tool.Fill;
        c.FillTolerance = 0;
        c.PointerDown(5, 5, false, false); c.PointerUp();
        Assert.Equal(near, rig.Layer.Get(6, 5));          // without tolerance the "close" one is not touched
        rig.Doc.History.Undo();

        c.FillTolerance = 20;
        c.PointerDown(5, 5, false, false); c.PointerUp();
        Assert.Equal(Rig.Red, rig.Layer.Get(6, 5));
        Assert.Equal(far, rig.Layer.Get(7, 5));
        Assert.Equal(a, rig.Layer.Get(8, 5));             // the connected area: to the right of the barrier it is not painted
        rig.Doc.History.Undo();

        c.FillWholeLayer = true;
        c.PointerDown(5, 5, false, false); c.PointerUp();
        Assert.Equal(Rig.Red, rig.Layer.Get(8, 5));       // the whole layer: the unconnected one is painted too
        Assert.Equal(far, rig.Layer.Get(7, 5));
    }
}
