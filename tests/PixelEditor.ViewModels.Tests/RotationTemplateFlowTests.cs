// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using PixelEditor.Core;
using PixelEditor.ViewModels;

namespace PixelEditor.ViewModels.Tests;

public class RotationAndToolSwitchTests
{
    private static void Fill(Layer l, int x0, int y0, int w, int h, uint c)
    {
        for (int y = y0; y < y0 + h; y++)
            for (int x = x0; x < x0 + w; x++) l.Pixels[y * l.Width + x] = c;
    }

    [Fact]
    public void AnySelection_KeepsTheSelectionTool()
    {
        using var rig = new Rig();
        var c = rig.Vm.Canvas;
        Fill(rig.Layer, 4, 4, 5, 5, Rig.Red);

        c.Tool = Tool.Select; rig.Stroke(2, 2, 12, 12);
        Assert.Equal(Tool.Select, c.Tool);
        c.DeselectCommand.Execute(null);

        c.Tool = Tool.Wand; c.PointerDown(5, 5, false, false); c.PointerUp();
        Assert.Equal(Tool.Wand, c.Tool);
        c.DeselectCommand.Execute(null);

        c.Tool = Tool.Lasso;
        c.PointerDown(2, 2, false, false); c.PointerMove(14, 2); c.PointerMove(14, 14); c.PointerMove(2, 14); c.PointerUp();
        Assert.Equal(Tool.Lasso, c.Tool);
        c.DeselectCommand.Execute(null);

        c.Tool = Tool.Polygon;
        foreach (var (x, y) in new[] { (2, 2), (14, 2), (14, 14), (2, 14) }) c.PointerDown(x, y, false, false);
        c.FinishPolygon();
        Assert.Equal(Tool.Polygon, c.Tool);
        c.DeselectCommand.Execute(null);

        c.Tool = Tool.Pencil;
        c.SelectAllCommand.Execute(null);
        Assert.Equal(Tool.Polygon, c.Tool);
    }

    [Fact]
    public void MoveTool_DragsSelectionAndKeepsIt_ClickOutsideDeselects_AndLeavingToolClearsIt()
    {
        using var rig = new Rig();
        var c = rig.Vm.Canvas;
        Fill(rig.Layer, 4, 4, 4, 4, Rig.Red);
        c.Tool = Tool.Select;
        rig.Stroke(3, 3, 9, 9); // a frame around the square
        Assert.Equal(Tool.Select, c.Tool);

        rig.Stroke(5, 5, 15, 5);   // drag the selection inside the frame
        Assert.Equal(Rig.Red, rig.Layer.Get(14, 4));
        Assert.True(c.HasSelection);               // the selection remained (it can be dragged further)
        Assert.Equal(Tool.Select, c.Tool);

        c.PointerDown(28, 28, false, false);       // a click outside the selection
        c.PointerUp();
        Assert.False(c.HasSelection);

        c.Tool = Tool.Select;
        rig.Stroke(12, 3, 20, 9);
        Assert.True(c.HasSelection);
        c.Tool = Tool.Pencil;                      // leaving for a drawing tool clears the selection
        Assert.False(c.HasSelection);
    }

    [Fact]
    public void RotateSelection_ByAngle_KeepsPixelsAndOnlyOriginalColors()
    {
        using var rig = new Rig();
        var c = rig.Vm.Canvas;
        for (int x = 8; x < 16; x++) rig.Layer.Pixels[10 * 32 + x] = Rig.Red; // a horizontal stroke
        c.Tool = Tool.Select;
        rig.Stroke(6, 8, 18, 12);
        c.RotateByCommand.Execute(90);
        c.DeselectCommand.Execute(null);

        Assert.True(Pixel.IsEmpty(rig.Layer.Get(8, 10)) || rig.Layer.Get(8, 10) == Rig.Red);
        int reds = rig.Layer.Pixels.Count(p => p == Rig.Red);
        Assert.InRange(reds, 6, 10);
        Assert.All(rig.Layer.Pixels, p => Assert.True(Pixel.IsEmpty(p) || p == Rig.Red));
        // the stroke became vertical: pixels in one column
        var cols = Enumerable.Range(0, 32 * 32).Where(i => rig.Layer.Pixels[i] == Rig.Red).Select(i => i % 32).Distinct().ToList();
        Assert.True(cols.Count <= 2);
    }

    [Fact]
    public void RotateWithoutSelection_TurnsWholeLayer_AndAskCommandParsesAngle()
    {
        using var rig = new Rig();
        for (int x = 16; x < 28; x++) rig.Layer.Pixels[16 * 32 + x] = Rig.Red; // from the center to the right
        rig.Dialogs.PromptFn = (_, _, _) => "90";
        rig.Vm.Canvas.RotateAskCommand.Execute(null);
        Assert.Equal(Rig.Red, rig.Layer.Get(15, 26)); // became down (the rotation center is between pixels)
        Assert.True(Pixel.IsEmpty(rig.Layer.Get(26, 16)));
        Assert.True(rig.Doc.History.CanUndo);

        rig.Dialogs.PromptFn = (_, _, _) => "abc";
        rig.Vm.Canvas.RotateAskCommand.Execute(null);
        Assert.Contains("угол", rig.S.StatusText);
        rig.Vm.Canvas.ApplyRotation(0);
        Assert.Contains("0°", rig.S.StatusText);
    }
}

public class SheetTemplateFlowTests
{
    private static void CreateProject(Rig rig)
    {
        rig.Dialogs.RsiProjectConfigure = d => { d.ParentFolder = rig.Dir; d.Name = "mob"; d.License = "CC0-1.0"; d.FrameWidthText = d.FrameHeightText = "16"; };
        rig.Vm.Rsi.NewProjectCommand.Execute(null);
    }

    [Fact]
    public void NewRsi_OffersToCreateFirstState_AndLaterDoesNot()
    {
        using var rig = new Rig();
        rig.Dialogs.RsiStateConfigure = d => { d.Name = "first"; d.SelectedDirections = d.DirectionChoices.First(c => c.Key == "4"); d.FramesText = "2"; };
        CreateProject(rig);
        Assert.Contains(rig.Dialogs.Confirms, c => c.Contains("первое состояние"));
        Assert.Equal("first", rig.Doc.Rsi!.State);   // the consent opened the state dialog at once and created the sheet
        Assert.Equal((32, 64), (rig.Doc.Width, rig.Doc.Height));

        using var rig2 = new Rig();
        rig2.Dialogs.ConfirmFn = _ => false;           // "Later"
        rig2.Dialogs.RsiStateConfigure = d => d.Name = "never";
        CreateProject(rig2);
        Assert.Null(rig2.Doc.Rsi);
    }

    [Fact]
    public void Template_FillsRowsFramesAndDelays_AndCreatesNamedZones()
    {
        using var rig = new Rig();
        rig.Dialogs.RsiStateConfigure = d =>
        {
            d.Name = "hero";
            d.SelectedTemplate = d.Templates.First(t => t.Id == "system:walk-4");
            Assert.Equal("4", d.SelectedDirections.Key);
            Assert.Equal("3", d.FramesText);
            Assert.Contains("шаг 1, 0.2", d.FrameLinesText);
        };
        rig.Vm.Rsi.NewSheetCommand.Execute(null);

        var b = rig.Doc.Rsi!;
        Assert.Equal(3, b.Frames);
        Assert.Equal(["стоит", "шаг 1", "шаг 2"], b.FrameNames);
        Assert.Equal("S · шаг 1", RsiLayout.Zones(b).First(z => z.Direction == 0 && z.Frame == 1).Label);
    }

    [Fact]
    public void FrameLines_OverrideCountAndDelays_AndAreValidated()
    {
        var vm = new RsiStateViewModel(16, 16, [], allowCustomRows: true, askSize: true);
        vm.Name = "x";
        vm.FramesText = "5";
        vm.DelayText = "0.3";                               // typing the delay sets all frames, so it goes first: a line without a delay takes it
        vm.FrameLinesText = "стоит, 0.5\nшаг 1, 0.15\nшаг 2";
        Assert.Contains("кадров в строке 3", vm.Summary);
        vm.OkCommand.Execute(null);
        var r = vm.Result!;
        Assert.Equal(3, r.Frames);
        Assert.Equal(["стоит", "шаг 1", "шаг 2"], r.FrameNames);
        Assert.Equal([0.5, 0.15, 0.3], r.FrameDelays);   // the third frame took the common delay

        var bad = new RsiStateViewModel(16, 16, []);
        bad.FrameLinesText = "a, быстро";
        bad.OkCommand.Execute(null);
        Assert.Contains("задержка", bad.Error);
    }

    [Fact]
    public void SaveOwnTemplate_ThenUseIt_AndDeleteIt()
    {
        using var rig = new Rig();
        rig.Dialogs.NameDescriptionConfigure = d => { d.Name = "Мои шаги"; d.Description = "тест"; };
        rig.Dialogs.RsiStateConfigure = d =>
        {
            d.SelectedDirections = d.DirectionChoices.First(c => c.Key == "custom");
            d.CustomRowsText = "вперёд, назад";
            d.FrameLinesText = "стоит, 0.4\nшаг, 0.1";
            d.SaveTemplateCommand.Execute(null);
            Assert.Equal("Мои шаги", d.SelectedTemplate!.Name);   // the new template is selected at once
            Assert.True(d.DeleteTemplateCommand.CanExecute(null));
            d.Name = "a";
        };
        rig.Vm.Rsi.NewSheetCommand.Execute(null);
        var saved = rig.Settings.SheetTemplates.Find("Мои шаги")!;
        Assert.Equal(["вперёд", "назад"], saved.Rows);
        Assert.Equal(0.4, saved.Frames[0].Delay, 3);

        // a new dialog: the template is in the list, fills in rows and frames, is deleted with confirmation
        var dlg = new RsiStateViewModel(16, 16, [], allowCustomRows: true, askSize: true,
            templates: rig.Settings.SheetTemplates, dialogs: rig.Dialogs);
        dlg.SelectedTemplate = dlg.Templates.First(t => t.Name == "Мои шаги");
        Assert.True(dlg.IsCustomLayout);
        Assert.Equal("вперёд, назад", dlg.CustomRowsText);
        rig.Dialogs.ConfirmFn = _ => false;
        dlg.DeleteTemplateCommand.Execute(null);
        Assert.NotNull(rig.Settings.SheetTemplates.Find("Мои шаги"));
        rig.Dialogs.ConfirmFn = _ => true;
        dlg.DeleteTemplateCommand.Execute(null);
        Assert.Null(rig.Settings.SheetTemplates.Find("Мои шаги"));
    }

    [Fact]
    public void RsiDialog_ListsOnlyRsiCompatibleTemplates_AndSystemOnesCannotBeDeleted()
    {
        using var rig = new Rig();
        var rsi = new RsiStateViewModel(16, 16, [], templates: rig.Settings.SheetTemplates, dialogs: rig.Dialogs);
        Assert.DoesNotContain(rsi.Templates, t => t.Id == "system:platformer");
        Assert.Contains(rsi.Templates, t => t.Id == "system:walk-8");
        rsi.SelectedTemplate = rsi.Templates.First(t => t.Id == "system:walk-8");
        Assert.Equal("8", rsi.SelectedDirections.Key);
        Assert.False(rsi.DeleteTemplateCommand.CanExecute(null));

        var sheet = new RsiStateViewModel(16, 16, [], allowCustomRows: true, askSize: true, templates: rig.Settings.SheetTemplates, dialogs: rig.Dialogs);
        Assert.Contains(sheet.Templates, t => t.Id == "system:platformer");
        sheet.SelectedTemplate = sheet.Templates.First(t => t.Id == "system:platformer");
        Assert.True(sheet.IsCustomLayout);
        Assert.Equal("idle, run, jump, fall", sheet.CustomRowsText);
    }
}
