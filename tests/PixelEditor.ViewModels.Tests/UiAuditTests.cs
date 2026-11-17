// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using PixelEditor.Core;
using PixelEditor.Core.Ops;
using PixelEditor.ViewModels;

namespace PixelEditor.ViewModels.Tests;

/// <summary>Menu, wording and shortcut changes of the UI audit.</summary>
public class UiAuditTests
{
    private static uint At(Rig rig, int x, int y) => rig.Layer.Get(x, y);

    // ---------------------------------------------------------------- A: wording and the optional license

    [Fact]
    public void InterfaceTexts_NeverMentionSs14()
    {
        var loc = new Loc(Path.Combine(Path.GetTempPath(), "pixed-no-user-locales-" + Guid.NewGuid().ToString("N")));
        foreach (var lang in new[] { "ru", "en" })
        {
            Assert.True(loc.SetLanguage(lang));
            foreach (var key in loc.Keys(lang))
            {
                var text = loc[key];
                Assert.False(text.Contains("SS14", StringComparison.OrdinalIgnoreCase) || text.Contains("Space Station", StringComparison.OrdinalIgnoreCase),
                    $"{lang}:{key} mentions SS14: {text}");
            }
        }
    }

    [Fact]
    public void NewRsi_WithoutLicense_IsSavedWithAnEmptyLicense()
    {
        using var rig = new Rig();
        rig.Dialogs.RsiProjectConfigure = d => { d.ParentFolder = rig.Dir; d.Name = "nolic"; d.License = ""; d.Copyright = ""; d.FrameWidthText = "16"; d.FrameHeightText = "16"; };
        rig.Vm.Rsi.NewProjectCommand.Execute(null);

        var meta = RsiMeta.Parse(File.ReadAllText(Path.Combine(rig.Dir, "nolic.rsi", "meta.json")));
        Assert.Equal("", meta.License);
        Assert.Equal("", meta.Copyright);
    }

    // ---------------------------------------------------------------- 3: fit and 100%

    [Fact]
    public void FitAndZoom100Commands_GoThroughTheViewModel()
    {
        using var rig = new Rig();
        var c = rig.Vm.Canvas;
        int fits = 0;
        c.FitRequested += () => fits++;
        c.FitCommand.Execute(null);
        Assert.Equal(1, fits);

        c.SetZoom(10);
        Assert.NotEqual(1.0, c.Zoom);
        c.ZoomActualCommand.Execute(null);
        Assert.Equal(1.0, c.Zoom);
        Assert.Equal("100%", c.ZoomText);
    }

    // ---------------------------------------------------------------- 4: turning and flipping the whole layer

    [Fact]
    public void FlipsAndHalfTurn_WorkOnTheWholeLayerWithoutSelection_InOneUndoStep()
    {
        using var rig = new Rig();
        rig.Layer.Pixels[3 * 32 + 2] = Rig.Red;                     // (2,3)
        var c = rig.Vm.Canvas;

        c.FlipHorizontalCommand.Execute(null);
        Assert.Equal(Rig.Red, At(rig, 29, 3));
        Assert.Equal(0u, At(rig, 2, 3));
        rig.Doc.History.Undo();
        Assert.Equal(Rig.Red, At(rig, 2, 3));

        c.FlipVerticalCommand.Execute(null);
        Assert.Equal(Rig.Red, At(rig, 2, 28));
        rig.Doc.History.Undo();

        c.Rotate180Command.Execute(null);
        Assert.Equal(Rig.Red, At(rig, 29, 28));
        rig.Doc.History.Undo();
        Assert.Equal(Rig.Red, At(rig, 2, 3));
        Assert.False(rig.Doc.History.CanUndo && At(rig, 29, 28) != 0);
    }

    [Fact]
    public void QuarterTurns_TurnTheWholeSquareLayer()
    {
        using var rig = new Rig();
        rig.Layer.Pixels[0] = Rig.Red;                              // top left
        rig.Vm.Canvas.RotateRightCommand.Execute(null);
        Assert.Equal(Rig.Red, At(rig, 31, 0));                      // clockwise: top right
        Assert.Equal(0u, At(rig, 0, 0));
        rig.Doc.History.Undo();
        Assert.Equal(Rig.Red, At(rig, 0, 0));

        rig.Vm.Canvas.RotateLeftCommand.Execute(null);
        Assert.Equal(Rig.Red, At(rig, 0, 31));                      // counterclockwise: bottom left
    }

    [Fact]
    public void QuarterTurn_OfANonSquareLayer_DoesWhatRotateByAngleDoes()
    {
        using var rig = new Rig();
        rig.Doc.ResizeCanvas(32, 16);
        for (int y = 2; y < 10; y++) for (int x = 20; x < 30; x++) rig.Layer.Pixels[y * 32 + x] = Rig.Red;
        var expected = Rotation.Rotate(rig.Layer.ToImage(), 90, smooth: true, expand: false);

        rig.Vm.Canvas.RotateRightCommand.Execute(null);

        Assert.Equal((32, 16), (rig.Layer.Width, rig.Layer.Height));
        Assert.Equal(expected.Pixels, rig.Layer.ToImage().Pixels);
    }

    [Fact]
    public void ScaleUpAndDown_StayAvailableOnlyWithASelection()
    {
        using var rig = new Rig();
        Assert.False(rig.Vm.Canvas.ScaleUpCommand.CanExecute(null));
        Assert.False(rig.Vm.Canvas.ScaleDownCommand.CanExecute(null));
        Assert.True(rig.Vm.Canvas.FlipHorizontalCommand.CanExecute(null));
        Assert.True(rig.Vm.Canvas.Rotate180Command.CanExecute(null));
        rig.Vm.Canvas.Tool = Tool.Select;
        rig.Stroke(0, 0, 3, 3);
        Assert.True(rig.Vm.Canvas.ScaleUpCommand.CanExecute(null));
    }

    [Fact]
    public void Flip_WithASelection_StillTransformsOnlyTheSelection()
    {
        using var rig = new Rig();
        rig.Layer.Pixels[0] = Rig.Red;                              // (0,0)
        rig.Layer.Pixels[20 * 32 + 20] = Rig.Blue;
        rig.Vm.Canvas.Tool = Tool.Select;
        rig.Stroke(0, 0, 3, 3);                                     // 4x4 area
        rig.Vm.Canvas.FlipHorizontalCommand.Execute(null);
        rig.Vm.Canvas.DeselectCommand.Execute(null);
        Assert.Equal(Rig.Red, At(rig, 3, 0));
        Assert.Equal(Rig.Blue, At(rig, 20, 20));                    // the rest of the layer is untouched
    }

    // ---------------------------------------------------------------- 5, 13: operations from the menu

    [Fact]
    public void OutlineFromTheMenu_UsesThePrimaryColor_AndBlackWhenItIsEmpty()
    {
        using var rig = new Rig();
        rig.Layer.Pixels[10 * 32 + 10] = Rig.Red;
        rig.Vm.Colors.SetPrimary(Rig.Blue);
        rig.Vm.Automation.ApplyOperationCommand.Execute("""outline|{"mode":"outside","autoPad":false,"color":"$primary"}""");
        Assert.Equal(Rig.Blue, At(rig, 9, 10));
        Assert.Equal(Rig.Red, At(rig, 10, 10));
        rig.Doc.History.Undo();

        rig.Vm.Colors.SetPrimary(0);
        rig.Vm.Automation.ApplyOperationCommand.Execute("""outline|{"mode":"outside","autoPad":false,"color":"$primary"}""");
        Assert.Equal(Pixel.Rgb(0, 0, 0), At(rig, 9, 10));
    }

    [Fact]
    public void OutlineInside_UsesThePrimaryColor()
    {
        using var rig = new Rig();
        for (int y = 5; y < 10; y++) for (int x = 5; x < 10; x++) rig.Layer.Pixels[y * 32 + x] = Rig.Red;
        rig.Vm.Colors.SetPrimary(Rig.Blue);
        rig.Vm.Automation.ApplyOperationCommand.Execute("""outline|{"mode":"inside","color":"$primary"}""");
        Assert.Equal(Rig.Blue, At(rig, 5, 5));
        Assert.Equal(Rig.Red, At(rig, 7, 7));
    }

    [Fact]
    public void SnapToPalette_UsesTheProjectPalette_AndExplainsAnEmptyOne()
    {
        using var rig = new Rig();
        rig.Layer.Pixels[5 * 32 + 5] = Pixel.Rgb(250, 20, 20);
        rig.Vm.Colors.Palette.Clear();
        rig.Vm.Automation.SnapToPaletteCommand.Execute(null);
        Assert.Equal(Loc.T("auto.palette_empty"), rig.S.StatusText);
        Assert.Equal(Pixel.Rgb(250, 20, 20), At(rig, 5, 5));

        rig.Vm.Colors.Palette.Add(Rig.Red);
        rig.Vm.Colors.Palette.Add(Rig.Blue);
        rig.Vm.Automation.SnapToPaletteCommand.Execute(null);
        Assert.Equal(Rig.Red, At(rig, 5, 5));
        rig.Doc.History.Undo();
        Assert.Equal(Pixel.Rgb(250, 20, 20), At(rig, 5, 5));
    }

    [Fact]
    public void TuningFromTheMenu_CanStartOnAnyOperation_AndRemembersTheLastOne()
    {
        using var rig = new Rig();
        string? seen = null;
        rig.Dialogs.TunerConfigure = t => { seen = t.OperationName; t.SelectedChoice = t.Choices.First(c => c.Name == "quantize"); };
        rig.Layer.Pixels[0] = Pixel.Rgb(10, 20, 30);
        rig.Layer.Pixels[1] = Pixel.Rgb(200, 20, 30);
        rig.Layer.Pixels[2] = Pixel.Rgb(100, 120, 30);
        rig.Vm.Automation.TuneOperationCommand.Execute(null);
        Assert.Equal(new OperationTunerViewModel(() => rig.Layer.ToImage(), null, null).Catalog[0].Name, seen);   // nothing is forced to removeBackground

        string? second = null;
        rig.Dialogs.TunerConfigure = t => { second = t.OperationName; };
        rig.Vm.Automation.TuneOperationCommand.Execute(null);
        Assert.Equal("quantize", second);                           // the generic item starts with the last tuned operation
    }

    // ---------------------------------------------------------------- 6: titles in the operation list

    [Theory]
    [InlineData("Привести все цвета к ближайшим из палитры.", "x", "Привести все цвета к ближайшим из палитры")]
    [InlineData("Цветокоррекция: яркость, контраст (-100..100). Делай перед quantize.", "x", "Цветокоррекция")]
    [InlineData("Убрать фон (сделать прозрачным). Фон моделируется.", "x", "Убрать фон")]
    [InlineData("(только скобки). Текст", "x", "(только скобки)")]
    [InlineData("Remove noise. More text", "x", "Remove noise")]
    [InlineData("Version 1.5 of things. Next", "x", "Version 1.5 of things")]
    [InlineData("", "plug", "plug")]
    [InlineData("   ", "plug", "plug")]
    [InlineData(": only a tail", "plug", "plug")]
    public void OperationTitle_IsTheFirstSentence(string description, string name, string expected) =>
        Assert.Equal(expected, OperationChoice.TitleOf(description, name));

    [Fact]
    public void OperationTitle_OfALongDescription_IsTrimmed()
    {
        var title = OperationChoice.TitleOf(string.Join(' ', Enumerable.Repeat("слово", 30)), "x");
        Assert.True(title.Length <= 50, title);
        Assert.EndsWith("…", title);
    }

    [Fact]
    public void Tuner_ListsTitlesWithTechnicalNames_AndSelectionByNameStillWorks()
    {
        var t = new OperationTunerViewModel(() => new PixelImage(8, 8), "quantize", null);
        Assert.Equal(t.Catalog.Count, t.Choices.Count);
        Assert.All(t.Choices, c => Assert.Equal($"{c.Title} ({c.Name})", c.Display));
        var bg = t.Choices.First(c => c.Name == "removeBackground");
        Assert.EndsWith("(removeBackground)", bg.Display);
        Assert.NotEqual("removeBackground", bg.Title);               // a real title, not the raw name

        Assert.Equal("quantize", t.SelectedChoice!.Name);
        t.SelectedChoice = bg;
        Assert.Equal("removeBackground", t.OperationName);
        t.SelectedOperation = t.Catalog.First(o => o.Name == "adjust");
        Assert.Equal("adjust", t.SelectedChoice!.Name);
    }

    // ---------------------------------------------------------------- 12: merge visible layers

    [Fact]
    public void MergeVisible_MergesVisibleUnlockedLayers_LeavesHiddenOnes_AndIsOneUndoStep()
    {
        using var rig = new Rig();
        var bottom = rig.Layer;
        bottom.Pixels[0] = Rig.Red;
        var hidden = rig.Doc.AddLayer("hidden");
        hidden.Pixels[1] = Rig.Blue;
        hidden.Visible = false;
        var top = rig.Doc.AddLayer("top");
        top.Pixels[0] = Rig.Blue;
        top.Pixels[2] = Rig.Red;
        Assert.Equal(3, rig.Doc.Layers.Count);
        Assert.True(rig.Vm.Layers.MergeVisibleCommand.CanExecute(null));

        rig.Vm.Layers.MergeVisibleCommand.Execute(null);

        Assert.Equal(2, rig.Doc.Layers.Count);
        Assert.Same(bottom, rig.Doc.Layers[0]);
        Assert.Same(hidden, rig.Doc.Layers[1]);
        Assert.False(hidden.Visible);
        Assert.Equal(Rig.Blue, bottom.Pixels[0]);                    // the upper layer wins
        Assert.Equal(Rig.Red, bottom.Pixels[2]);
        Assert.Equal(0u, bottom.Pixels[1]);                          // the hidden layer did not leak into the result
        Assert.Equal(Rig.Blue, hidden.Pixels[1]);
        Assert.Same(bottom, rig.S.ActiveLayer);

        rig.Doc.History.Undo();
        Assert.Equal(3, rig.Doc.Layers.Count);
        Assert.Same(top, rig.Doc.Layers[2]);
        Assert.Equal(Rig.Red, bottom.Pixels[0]);
        Assert.Equal(0u, bottom.Pixels[2]);
        Assert.Equal(Rig.Blue, top.Pixels[0]);
    }

    [Fact]
    public void MergeVisible_NeedsTwoVisibleUnlockedLayers()
    {
        using var rig = new Rig();
        Assert.False(rig.Vm.Layers.MergeVisibleCommand.CanExecute(null));       // one layer
        var second = rig.Doc.AddLayer("two");
        Assert.True(rig.Vm.Layers.MergeVisibleCommand.CanExecute(null));
        second.Locked = true;                                                    // a locked layer is not merged
        rig.S.NotifyStateChanged();
        Assert.False(rig.Vm.Layers.MergeVisibleCommand.CanExecute(null));
        second.Locked = false;
        second.Visible = false;
        rig.S.NotifyStateChanged();
        Assert.False(rig.Vm.Layers.MergeVisibleCommand.CanExecute(null));
    }

    // ---------------------------------------------------------------- 14: operations honor "only in selection"

    [Fact]
    public void Operation_WithSelectionOnly_ChangesOnlyTheSelection()
    {
        using var rig = new Rig();
        rig.Layer.Pixels[0] = Rig.Red;
        rig.Layer.Pixels[6] = Rig.Red;
        rig.Vm.Canvas.Tool = Tool.Select;
        rig.Stroke(0, 0, 2, 2);
        rig.Vm.Automation.SelectionOnly = true;

        rig.Vm.Automation.ApplyOperation("replaceColor", """{"from":"#FF0000","to":"#0000FF"}""");

        Assert.Equal(Rig.Blue, At(rig, 0, 0));
        Assert.Equal(Rig.Red, At(rig, 6, 0));
        rig.Doc.History.Undo();
        Assert.Equal(Rig.Red, At(rig, 0, 0));
    }

    [Fact]
    public void Operation_WithSelectionOnlyButNoSelection_DoesNothingAndSaysSo()
    {
        using var rig = new Rig();
        rig.Layer.Pixels[0] = Rig.Red;
        rig.Vm.Automation.SelectionOnly = true;
        rig.Vm.Automation.ApplyOperation("replaceColor", """{"from":"#FF0000","to":"#0000FF"}""");
        Assert.Equal(Rig.Red, At(rig, 0, 0));
        Assert.Equal(Loc.T("canvas.no_selection"), rig.S.StatusText);
    }

    [Fact]
    public void Operation_WithoutTheFlag_StillChangesTheWholeLayerEvenWithASelection()
    {
        using var rig = new Rig();
        rig.Layer.Pixels[0] = Rig.Red;
        rig.Layer.Pixels[6] = Rig.Red;
        rig.Vm.Canvas.Tool = Tool.Select;
        rig.Stroke(0, 0, 2, 2);
        rig.Vm.Automation.ApplyOperation("replaceColor", """{"from":"#FF0000","to":"#0000FF"}""");
        Assert.Equal(Rig.Blue, At(rig, 0, 0));
        Assert.Equal(Rig.Blue, At(rig, 6, 0));
    }

    [Fact]
    public void Tuner_WithSelectionOnly_AppliesOnlyInsideTheSelection()
    {
        using var rig = new Rig();
        rig.Layer.Pixels[0] = Rig.Red;
        rig.Layer.Pixels[6] = Rig.Red;
        rig.Vm.Canvas.Tool = Tool.Select;
        rig.Stroke(0, 0, 2, 2);
        rig.Vm.Automation.SelectionOnly = true;
        rig.Dialogs.TunerConfigure = t =>
        {
            t.Parameters.First(p => p.Name == "from").Value = "#FF0000";
            t.Parameters.First(p => p.Name == "to").Value = "#0000FF";
            t.RunPreview();
        };
        rig.Vm.Automation.TuneOperationCommand.Execute("replaceColor|{}");
        Assert.Equal(Rig.Blue, At(rig, 0, 0));
        Assert.Equal(Rig.Red, At(rig, 6, 0));
    }
}
