// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using PixelEditor.Core;
using PixelEditor.Core.Ops;
using PixelEditor.ViewModels;

namespace PixelEditor.ViewModels.Tests;

public class AnimationSheetTests
{
    [Fact]
    public void NewAnimationSheet_UsesCustomRowNamesAndFrameSize()
    {
        using var rig = new Rig();
        rig.Dialogs.RsiStateConfigure = d =>
        {
            d.Name = "hero";
            d.SelectedDirections = d.DirectionChoices.First(c => c.Key == "custom");
            d.CustomRowsText = "idle, walk_down, attack";
            d.FramesText = "4";
            d.FrameWidthText = "24";
            d.FrameHeightText = "40";
        };
        rig.Vm.Rsi.NewSheetCommand.Execute(null);

        Assert.Equal((96, 120), (rig.Doc.Width, rig.Doc.Height));
        Assert.Equal((24, 40), (rig.Doc.SheetCellWidth, rig.Doc.SheetCellHeight));
        var b = rig.Doc.Rsi!;
        Assert.False(b.IsRsi);
        Assert.Equal(["idle", "walk_down", "attack"], b.RowLabels);
        Assert.Equal("walk_down · 2", RsiLayout.Zones(b).First(z => z.Direction == 1 && z.Frame == 1).Label);
        Assert.Contains("Лист анимации «hero»", rig.Vm.Rsi.StatusLine);
        Assert.False(rig.Vm.Rsi.SaveStateCommand.CanExecute(null)); // this is not RSI: there is nothing to save to a folder
    }

    [Fact]
    public void NewAnimationSheet_ExportsClipsNamedByRows()
    {
        using var rig = new Rig();
        rig.Dialogs.RsiStateConfigure = d =>
        {
            d.Name = "hero";
            d.SelectedDirections = d.DirectionChoices.First(c => c.Key == "custom");
            d.CustomRowsText = "idle, run";
            d.FramesText = "2";
            d.FrameWidthText = d.FrameHeightText = "16";
        };
        rig.Vm.Rsi.NewSheetCommand.Execute(null);
        var outDir = Path.Combine(rig.Dir, "unity");
        rig.Dialogs.ExportConfigure = d => { d.Folder = outDir; d.BaseName = "hero"; };
        rig.Vm.Rsi.ExportAnimationCommand.Execute(null);

        var tres = File.ReadAllText(Path.Combine(outDir, "hero.tres"));
        Assert.Contains("&\"hero_idle\"", tres);
        Assert.Contains("&\"hero_run\"", tres);
        Assert.Contains("hero_run", File.ReadAllText(Path.Combine(outDir, "hero.aseprite.json")));
        Assert.True(File.Exists(Path.Combine(outDir, "hero_hero_run.gif".Replace("hero_hero", "hero_hero"))) || Directory.GetFiles(outDir, "*run*.gif").Length == 1);
    }

    [Fact]
    public void StateDialog_ValidatesCustomRows_AndRsiModeHasNoCustomChoice()
    {
        var vm = new RsiStateViewModel(32, 32, [], allowCustomRows: true, askSize: true);
        vm.SelectedDirections = vm.DirectionChoices.First(c => c.Key == "custom");
        vm.CustomRowsText = "a, A";
        vm.OkCommand.Execute(null);
        Assert.Contains("повторяться", vm.Error);
        vm.CustomRowsText = "a/b";
        vm.OkCommand.Execute(null);
        Assert.Contains("нельзя", vm.Error);
        vm.CustomRowsText = " , ";
        vm.OkCommand.Execute(null);
        Assert.Contains("хотя бы одна", vm.Error);
        vm.CustomRowsText = "idle, walk";
        vm.OkCommand.Execute(null);
        Assert.Equal(["idle", "walk"], vm.Result!.Rows);
        Assert.Equal(2, vm.Result.Directions);

        var rsi = new RsiStateViewModel(32, 32, []);
        Assert.DoesNotContain(rsi.DirectionChoices, c => c.Key == "custom");
        Assert.Contains("S, N, E, W", rsi.Summary);
    }

    [Fact]
    public void Rows_AreStoredInProject()
    {
        var doc = new Document(32, 32);
        doc.Rsi = new RsiBinding("", "hero", 2, 2, 16, 16, RsiState.UniformDelays(2, 2, 0.1), ["idle", "run"]);
        using var ms = new MemoryStream();
        ProjectIO.Save(doc, ms);
        ms.Position = 0;
        var back = ProjectIO.Load(ms);
        Assert.Equal(["idle", "run"], back.Rsi!.RowLabels);
        Assert.False(back.Rsi.IsRsi);
    }
}

public class SelectionDragAndLightTests
{
    private static void Fill(Layer l, int x0, int y0, int w, int h, uint c)
    {
        for (int y = y0; y < y0 + h; y++)
            for (int x = x0; x < x0 + w; x++) l.Pixels[y * l.Width + x] = c;
    }

    [Fact]
    public void WandSelection_CanBeDraggedWithoutSwitchingTool()
    {
        using var rig = new Rig();
        Fill(rig.Layer, 2, 2, 4, 4, Rig.Red);
        var c = rig.Vm.Canvas;
        c.Tool = Tool.Wand;
        c.PointerDown(3, 3, false, false);
        c.PointerUp();
        Assert.True(c.HasSelection);

        rig.Stroke(3, 3, 13, 13); // the same wand: a click inside the selection drags it
        Assert.Equal(Tool.Wand, c.Tool); // the selection tool stays active
        Assert.Equal(Rig.Red, rig.Layer.Get(12, 12));
        Assert.True(Pixel.IsEmpty(rig.Layer.Get(3, 3)));

        c.PointerDown(25, 25, false, false); // a click outside makes a new selection (of an empty place)
        c.PointerUp();
    }

    [Fact]
    public void LassoAndPolygonSelections_CanBeDraggedToo()
    {
        using var rig = new Rig();
        Fill(rig.Layer, 4, 4, 6, 6, Rig.Blue);
        var c = rig.Vm.Canvas;
        c.Tool = Tool.Polygon;
        foreach (var (x, y) in new[] { (3, 3), (11, 3), (11, 11), (3, 11) }) c.PointerDown(x, y, false, false);
        c.FinishPolygon();
        Assert.NotNull(c.SelectionShape);
        rig.Stroke(6, 6, 16, 6);
        Assert.Equal(Rig.Blue, rig.Layer.Get(16, 6));
        Assert.Equal(Tool.Polygon, c.Tool);
    }

    [Fact]
    public void RightClickWand_StillAddsToSelection_InsteadOfDragging()
    {
        using var rig = new Rig();
        Fill(rig.Layer, 2, 2, 3, 3, Rig.Red);
        Fill(rig.Layer, 10, 2, 3, 3, Rig.Red);
        var c = rig.Vm.Canvas;
        c.Tool = Tool.Wand;
        c.PointerDown(3, 3, false, false);
        c.PointerUp();
        c.Tool = Tool.Wand; // after selecting the tool became "Move"; to add an area take the wand again
        c.PointerDown(11, 3, true, false);
        c.PointerUp();
        Assert.Equal(18, c.SelectionShape!.Count);
    }

    [Fact]
    public void EdgeShade_LightensLitEdges_DarkensOppositeEdges_AndKeepsInside()
    {
        var img = new PixelImage(8, 8);
        for (int y = 1; y < 7; y++)
            for (int x = 1; x < 7; x++) img.Set(x, y, Pixel.Rgb(120, 120, 120));
        var r = Lighting.EdgeShade(img, Lighting.ToLight("NW"), 1, 0.2, 0.25);
        Assert.True(Pixel.R(r.Pixels[1 * 8 + 1]) > 120);   // the corner toward the light
        Assert.True(Pixel.R(r.Pixels[6 * 8 + 6]) < 120);   // the opposite corner
        Assert.Equal(Pixel.Rgb(120, 120, 120), r.Pixels[3 * 8 + 3]); // the middle is not touched
        Assert.True(Pixel.R(r.Pixels[1 * 8 + 4]) > 120);   // the top edge is a highlight
        Assert.True(Pixel.R(r.Pixels[4 * 8 + 6]) < 120);   // the right edge is a shadow

        var thin = new PixelImage(5, 5);
        thin.Set(2, 2, Pixel.Rgb(100, 100, 100));
        Assert.Equal(thin.Pixels, Lighting.EdgeShade(thin, (-1, -1), 1, 0.3, 0.3).Pixels); // empty on both sides: no change
        Assert.Throws<ArgumentException>(() => Lighting.ToLight("up"));
    }

    [Fact]
    public void EdgeShadeOp_RunsFromMenuArguments()
    {
        using var rig = new Rig();
        Fill(rig.Layer, 5, 5, 6, 6, Pixel.Rgb(100, 140, 100));
        rig.Vm.Automation.ApplyOperation("edgeShade", """{"from":"NW","depth":1,"highlight":0.2,"shadow":0.25}""");
        Assert.True(Pixel.G(rig.Layer.Get(5, 5)) > 140);
        Assert.True(Pixel.G(rig.Layer.Get(10, 10)) < 140);
    }
}
