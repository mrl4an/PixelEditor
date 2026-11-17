// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using PixelEditor.Core;
using PixelEditor.ViewModels;

namespace PixelEditor.ViewModels.Tests;

/// <summary>Regression tests for the owner's report on a painted illustration: wand tolerance, eyedropper highlight, pixel grid toggle.</summary>
public class OwnerReportTests
{
    /// <summary>A painted flat sky: every pixel differs slightly from its neighbours (noise of a few RGB levels).</summary>
    private static void PaintNoisySky(Layer l, int seed = 1)
    {
        var rnd = new Random(seed);
        for (int i = 0; i < l.Pixels.Length; i++)
            l.Pixels[i] = Pixel.Rgb((byte)(90 + rnd.Next(-4, 5)), (byte)(140 + rnd.Next(-4, 5)), (byte)(220 + rnd.Next(-4, 5)));
    }

    [Fact]
    public void Wand_OnFreshSettings_SelectsNoisySky_NotJustOwnColor()
    {
        using var rig = new Rig();
        PaintNoisySky(rig.Layer);
        var c = rig.Vm.Canvas;
        c.Tool = Tool.Wand;
        c.PointerDown(5, 5, false, false); c.PointerUp();
        Assert.True(c.SelectionShape!.Count > 32 * 32 * 9 / 10, $"selected only {c.SelectionShape.Count}");
    }

    [Fact]
    public void OldSettingsFile_WithZeroWandTolerance_IsMigratedToTheUsefulDefault()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pixed-old-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // what the previous version wrote on the first preference change: the tolerance saved as 0
            File.WriteAllText(Path.Combine(dir, "settings.json"), "{ \"BrushSize\": 2, \"WandTolerance\": 0, \"WandContiguous\": true, \"WandFollow\": false }");
            var s = AppSettings.Load(dir);
            Assert.True(s.WandTolerance > 0);
            s.WandTolerance = 0;           // a deliberate 0 chosen afterwards is kept
            s.Save();
            Assert.Equal(0, AppSettings.Load(dir).WandTolerance);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void Eyedropper_AfterWandSelection_LeavesNoSelectionAndNoHighlight()
    {
        using var rig = new Rig();
        PaintNoisySky(rig.Layer);
        var c = rig.Vm.Canvas;
        c.Tool = Tool.Wand;
        c.PointerDown(5, 5, false, false); c.PointerUp();
        Assert.True(c.HasSelection);
        c.Tool = Tool.Picker;
        c.PointerDown(6, 6, false, false); c.PointerUp();
        Assert.False(c.HasSelection);
        Assert.Null(c.SelectionShape);
        Assert.Null(rig.S.Highlight);
    }

    [Fact]
    public void Eyedropper_ClearsUsedColorsHighlight()
    {
        using var rig = new Rig();
        rig.Layer.Pixels[0] = Rig.Red;
        rig.Layer.Pixels[1] = Rig.Blue;
        rig.Vm.Colors.RefreshStats();
        rig.Vm.Colors.SelectedStat = rig.Vm.Colors.Stats[0];
        Assert.NotNull(rig.S.Highlight);
        rig.Vm.Canvas.Tool = Tool.Picker;
        rig.Vm.Canvas.PointerDown(1, 0, false, false);
        Assert.Equal(Rig.Blue, rig.Vm.Colors.Primary);
        Assert.Null(rig.S.Highlight);
        Assert.Null(rig.Vm.Colors.SelectedStat);
    }

    [Fact]
    public void BgEraser_ErasesNoisyRegionInOneUndoStep_AndRespectsAdjacentOnly()
    {
        using var rig = new Rig();
        PaintNoisySky(rig.Layer);
        for (int y = 0; y < 32; y++) rig.Layer.Pixels[y * 32 + 16] = Rig.Red;   // a red wall splits the sky in two halves
        var c = rig.Vm.Canvas;
        c.Tool = Tool.BgEraser;
        c.PointerDown(2, 2, false, false);
        Assert.True(Pixel.IsEmpty(rig.Layer.Get(3, 20)));
        Assert.Equal(Rig.Red, rig.Layer.Get(16, 5));
        Assert.False(Pixel.IsEmpty(rig.Layer.Get(25, 5)));      // the other half is not adjacent
        Assert.Contains("Ластик фона", rig.S.StatusText);
        rig.Vm.UndoCommand.Execute(null);
        Assert.False(Pixel.IsEmpty(rig.Layer.Get(3, 20)));
        c.EraserContiguous = false;
        c.PointerDown(2, 2, false, false);
        Assert.True(Pixel.IsEmpty(rig.Layer.Get(25, 5)));       // adjacent-only off: the whole layer
    }

    [Fact]
    public void Selection_ToolStays_DragInsideMovesOutsideStartsNew()
    {
        using var rig = new Rig();
        for (int y = 4; y < 8; y++) for (int x = 4; x < 8; x++) rig.Layer.Pixels[y * 32 + x] = Rig.Red;
        var c = rig.Vm.Canvas;
        c.Tool = Tool.Select;
        rig.Stroke(3, 3, 9, 9);
        Assert.Equal(Tool.Select, c.Tool);
        Assert.True(c.HandlesActive);
        rig.Stroke(5, 5, 15, 5);                                  // inside: drags
        Assert.Equal(Rig.Red, rig.Layer.Get(14, 4));
        rig.Stroke(25, 25, 28, 28);                               // outside: a new frame
        Assert.Equal(Tool.Select, c.Tool);
        Assert.Equal(new PixelRect(25, 25, 4, 4), c.SelectionBounds);
    }

    [Fact]
    public void PixelGrid_TurnsOff_RaisesChange_AndIsRemembered()
    {
        using var rig = new Rig();
        var c = rig.Vm.Canvas;
        Assert.True(c.ShowPixelGrid);
        var raised = new List<string?>();
        c.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        c.ShowPixelGrid = false;
        Assert.Contains(nameof(CanvasViewModel.ShowPixelGrid), raised);
        Assert.False(AppSettings.Load(rig.Settings.Directory).ShowPixelGrid);
        c.ShowPixelGrid = true;
        Assert.True(AppSettings.Load(rig.Settings.Directory).ShowPixelGrid);
    }
}
