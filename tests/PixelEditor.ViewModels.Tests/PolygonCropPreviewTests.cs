// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using PixelEditor.Core;
using PixelEditor.ViewModels;

namespace PixelEditor.ViewModels.Tests;

public class PolygonCropPreviewTests
{
    [Fact]
    public void PolygonTool_BuildsSelectionFromNodes_AndFinishesByNearFirstNodeOrRightClick()
    {
        using var rig = new Rig();
        var c = rig.Vm.Canvas;
        c.Tool = Tool.Polygon;
        c.PointerDown(4, 4, false, false);
        Assert.True(c.IsBuildingPolygon);
        c.PointerDown(20, 4, false, false);
        c.PointerDown(20, 20, false, false);
        c.PointerMove(10, 22);
        Assert.NotNull(c.LassoPath);
        Assert.Equal((10, 22), c.LassoPath![^2]); // the cursor point comes before closing

        c.PointerDown(4, 20, false, false);
        c.PointerDown(5, 5, false, false); // near the first node: closing
        Assert.False(c.IsBuildingPolygon);
        Assert.NotNull(c.SelectionShape);
        Assert.True(c.SelectionShape!.Contains(10, 10));
        Assert.False(c.SelectionShape.Contains(25, 25));

        // the right button also closes, Backspace removes a node, Esc cancels
        c.DeselectCommand.Execute(null);
        c.Tool = Tool.Polygon; // after closing the tool became "Move"
        c.PointerDown(2, 2, false, false);
        c.PointerDown(9, 2, false, false);
        c.PointerDown(9, 9, false, false);
        Assert.True(c.UndoPolygonNode());
        c.PointerDown(5, 9, false, false);
        c.PointerDown(0, 0, true, false);
        Assert.NotNull(c.SelectionShape);

        c.DeselectCommand.Execute(null);
        c.Tool = Tool.Polygon;
        c.PointerDown(1, 1, false, false);
        c.DeselectCommand.Execute(null);
        Assert.False(c.IsBuildingPolygon);
    }

    [Fact]
    public void PolygonWithTwoNodes_SelectsNothing_AndLeavingToolCancels()
    {
        using var rig = new Rig();
        var c = rig.Vm.Canvas;
        c.Tool = Tool.Polygon;
        c.PointerDown(3, 3, false, false);
        c.PointerDown(8, 8, false, false);
        c.FinishPolygon();
        Assert.False(c.HasSelection);

        c.PointerDown(3, 3, false, false);
        c.Tool = Tool.Pencil;
        Assert.False(c.IsBuildingPolygon);
    }

    [Fact]
    public void CropToSelection_AndToContent_ResizeCanvas_WithUndo()
    {
        using var rig = new Rig();
        rig.Vm.Colors.SetPrimary(Rig.Red);
        rig.Stroke(10, 10, 14, 10);
        rig.Vm.Canvas.CropToContentCommand.Execute(null);
        Assert.Equal((5, 1), (rig.Doc.Width, rig.Doc.Height));
        rig.Vm.UndoCommand.Execute(null);
        Assert.Equal((32, 32), (rig.Doc.Width, rig.Doc.Height));

        rig.Vm.Canvas.Tool = Tool.Select;
        rig.Stroke(8, 8, 17, 12);
        Assert.True(rig.Vm.Canvas.CropToSelectionCommand.CanExecute(null));
        rig.Vm.Canvas.CropToSelectionCommand.Execute(null);
        Assert.Equal((10, 5), (rig.Doc.Width, rig.Doc.Height));
        Assert.Equal(Rig.Red, rig.Layer.Get(2, 2));
    }

    [Fact]
    public void BrushPreview_ShowsAffectedArea_WithMirrorAndOnlyForBrushLikeTools()
    {
        using var rig = new Rig();
        var c = rig.Vm.Canvas;
        Assert.Empty(c.BrushPreview); // the cursor is not on the canvas yet
        c.UpdatePosition(10, 10);
        Assert.Equal([new PixelRect(10, 10, 1, 1)], c.BrushPreview);

        c.BrushSize = 3;
        Assert.Equal([new PixelRect(9, 9, 3, 3)], c.BrushPreview);
        c.MirrorX = true;
        Assert.Equal(2, c.BrushPreview.Count);
        Assert.Equal(20, c.BrushPreview[1].X); // mirror: 31 - 10 - 1

        c.Tool = Tool.Fill;
        Assert.Empty(c.BrushPreview);
        c.Tool = Tool.Rect;
        c.ShapeThickness = 2;
        Assert.Equal(2, c.BrushPreview[0].Width);
        c.Tool = Tool.Eraser;
        c.ClearPosition();
        Assert.Empty(c.BrushPreview); // the cursor left the canvas
    }

    [Fact]
    public void Import_NoResizeMethod_KeepsOriginalSize_AndSwitchesOnManualSize()
    {
        var img = new PixelImage(40, 20);
        for (int i = 0; i < img.Pixels.Length; i++) img.Pixels[i] = Rig.Red;
        var vm = new ImportViewModel(new FakeDialogs(), null, img, "t.png", [], canAddLayer: false);
        Assert.Equal("none", vm.SelectedMethod.Key);
        Assert.DoesNotContain("resize", vm.PipelineJson);

        vm.SetLongestSideCommand.Execute(10);
        vm.Recompute();
        Assert.Equal("box", vm.SelectedMethod.Key); // the size is set: the method was replaced
        Assert.Contains("resize", vm.PipelineJson);
        Assert.Equal(10, vm.Preview!.Width);

        vm.SelectedMethod = vm.Methods[0]; // returned to "no resize"
        Assert.Equal("40", vm.WidthText);
        vm.Recompute();
        Assert.Equal((40, 20), (vm.Preview!.Width, vm.Preview.Height));
        Assert.DoesNotContain("resize", vm.PipelineJson);
    }
}
