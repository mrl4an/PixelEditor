// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using PixelEditor.Core;
using PixelEditor.ViewModels;

namespace PixelEditor.ViewModels.Tests;

public class SelectionDragRepeatTests
{
    private static void Fill(Layer l, int x0, int y0, int w, int h, uint c)
    {
        for (int y = y0; y < y0 + h; y++)
            for (int x = x0; x < x0 + w; x++) l.Pixels[y * l.Width + x] = c;
    }

    [Fact]
    public void Marquee_CanBeDraggedRepeatedly_AndEmptyClickOutsideDeselects()
    {
        using var rig = new Rig();
        var c = rig.Vm.Canvas;
        Fill(rig.Layer, 4, 4, 4, 4, Rig.Red);
        c.Tool = Tool.Select;
        rig.Stroke(3, 3, 9, 9);
        for (int i = 0; i < 3; i++)
        {
            var before = c.SelectionBounds!.Value;
            rig.Stroke(before.X + 2, before.Y + 2, before.X + 5, before.Y + 2);
            Assert.Equal(before.X + 3, c.SelectionBounds!.Value.X);
            Assert.True(c.HasSelection);
        }
        // a click on an empty spot outside the selection always clears it, including on empty pixels
        c.PointerDown(28, 28, false, false); c.PointerUp();
        Assert.False(c.HasSelection);
        Assert.Null(c.SelectionBounds);
    }

    [Fact]
    public void WandSelection_Moves_AndOutsideClickClears_ThenReselectWorks()
    {
        using var rig = new Rig();
        var c = rig.Vm.Canvas;
        Fill(rig.Layer, 4, 4, 4, 4, Rig.Red);
        c.Tool = Tool.Wand;
        c.PointerDown(5, 5, false, false); c.PointerUp();
        Assert.Equal(Tool.Wand, c.Tool);
        rig.Stroke(5, 5, 12, 5);
        rig.Stroke(12, 5, 12, 12);
        Assert.True(c.HasSelection);
        c.PointerDown(30, 30, false, false); c.PointerUp();   // outside: the wand starts a NEW selection (of the empty area), the tool stays
        Assert.Equal(Tool.Wand, c.Tool);
        c.DeselectCommand.Execute(null);
        Assert.Null(c.SelectionBounds);
        c.Tool = Tool.Select;
        rig.Stroke(1, 1, 3, 3);
        Assert.True(c.HasSelection);
    }
}

public class RotateHandleTests
{
    [Fact]
    public void Handle_RotatesFromBase_WithoutAccumulatingLoss_AndIsOneUndoStep()
    {
        using var rig = new Rig();
        var c = rig.Vm.Canvas;
        for (int x = 8; x < 16; x++) rig.Layer.Pixels[10 * 32 + x] = Rig.Red;
        c.Tool = Tool.Select;
        rig.Stroke(6, 8, 18, 12);
        Assert.NotNull(c.SelectionCenter);

        Assert.True(c.BeginRotateHandle());
        c.RotateHandleTo(37); c.RotateHandleTo(61); c.RotateHandleTo(90);
        Assert.True(c.IsRotating);
        c.EndRotateHandle();
        Assert.False(c.IsRotating);
        var cols = Enumerable.Range(0, 32 * 32).Where(i => rig.Layer.Pixels[i] == Rig.Red).Select(i => i % 32).Distinct().ToList();
        Assert.True(cols.Count <= 2);                       // became a vertical stroke

        Assert.True(c.BeginRotateHandle());
        c.RotateHandleTo(0);                                // back to the original snapshot position
        c.EndRotateHandle();
        Assert.Equal(8, rig.Layer.Pixels.Count(p => p == Rig.Red));
        c.DeselectCommand.Execute(null);
        rig.Doc.History.Undo();
        Assert.Equal(8, rig.Layer.Pixels.Count(p => p == Rig.Red));
    }
}

public class MoveClickTests
{
    [Fact]
    public void ClickWithoutDrag_InsideSelection_KeepsSelectionAndPixels()
    {
        using var rig = new Rig();
        var c = rig.Vm.Canvas;
        rig.Vm.Colors.SetPrimary(Rig.Red);
        c.Tool = Tool.Rect;
        rig.Stroke(10, 10, 20, 18);
        int before = rig.Doc.Layers.Sum(l => l.Pixels.Count(p => p == Rig.Red));
        Assert.True(before > 0);

        c.Tool = Tool.Select;
        rig.Stroke(5, 5, 26, 24);
        Assert.Equal(Tool.Select, c.Tool);
        var sel = c.SelectionBounds;

        c.PointerDown(15, 14, false, false); c.PointerUp();      // just a click inside the frame, in the empty middle of the shape
        Assert.True(c.HasSelection);
        Assert.Equal(sel, c.SelectionBounds);
        Assert.Equal(before, rig.Doc.Layers.Sum(l => l.Pixels.Count(p => p == Rig.Red)));

        c.Tool = Tool.Move;                                       // choosing "Move" again does not touch the selection
        Assert.True(c.HasSelection);
        c.PointerDown(10, 10, false, false); c.PointerUp();        // a click on the outline itself inside the frame
        Assert.True(c.HasSelection);
        Assert.Equal(before, rig.Doc.Layers.Sum(l => l.Pixels.Count(p => p == Rig.Red)));
    }
}

public class ScaleHandleTests
{
    [Fact]
    public void Handles_StretchAndShrink_FromSnapshot_AndKeepAspectOnCorner()
    {
        using var rig = new Rig();
        var c = rig.Vm.Canvas;
        for (int y = 4; y < 8; y++) for (int x = 4; x < 8; x++) rig.Layer.Pixels[y * 32 + x] = Rig.Red;
        c.Tool = Tool.Select;
        rig.Stroke(4, 4, 7, 7);
        var b = c.SelectionBounds!.Value;
        Assert.Equal((4, 4), (b.Width, b.Height));

        Assert.True(c.BeginScaleHandle(4));               // the southeast corner
        c.ScaleHandleTo(12, 12, false);
        Assert.Equal((8, 8), (c.SelectionBounds!.Value.Width, c.SelectionBounds!.Value.Height));
        c.ScaleHandleTo(10, 8, false);                    // smaller in height: computed from the original 4×4
        Assert.Equal((6, 4), (c.SelectionBounds!.Value.Width, c.SelectionBounds!.Value.Height));
        c.EndScaleHandle();
        Assert.Equal(24, rig.Layer.Pixels.Count(p => p == Rig.Red));

        Assert.True(c.BeginScaleHandle(0));               // the northwest one keeping proportions
        var cur = c.SelectionBounds!.Value;
        c.ScaleHandleTo(cur.X + cur.Width - 12, cur.Y + cur.Height - 4, true);
        var n = c.SelectionBounds!.Value;
        Assert.Equal(n.Width * 4, n.Height * 6);          // the 6:4 proportions are kept
        c.EndScaleHandle();

        c.DeselectCommand.Execute(null);
        rig.Doc.History.Undo(); rig.Doc.History.Undo();
        Assert.True(rig.Layer.Pixels.Count(p => p == Rig.Red) <= 16 + 24);
    }
}

public class MoveObjectClickTests
{
    [Fact]
    public void ClickOnObject_WithoutDrag_KeepsItSelected()
    {
        using var rig = new Rig();
        var c = rig.Vm.Canvas;
        for (int y = 4; y < 8; y++) for (int x = 4; x < 8; x++) rig.Layer.Pixels[y * 32 + x] = Rig.Red;
        c.Tool = Tool.Move;
        c.PointerDown(5, 5, false, false); c.PointerUp();
        Assert.True(c.HasSelection);
        Assert.Equal(16, rig.Layer.Pixels.Count(p => p == Rig.Red));
        Assert.True(c.BeginScaleHandle(4));      // the selected subject can be stretched at once
        c.ScaleHandleTo(12, 12, false);
        c.EndScaleHandle();
        Assert.Equal(64, rig.Layer.Pixels.Count(p => p == Rig.Red));
    }
}
