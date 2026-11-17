// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using PixelEditor.Core;
using PixelEditor.ViewModels;

namespace PixelEditor.ViewModels.Tests;

/// <summary>
/// The frame delay typed in the new animation sheet / RSI state dialog must reach the created sheet, whatever order the user works in
/// (template first or delay first), and the dialog must show exactly what will be created.
/// </summary>
public class SheetDelayTests
{
    private static RsiStateViewModel Dialog(Rig rig, bool custom = true) =>
        new(16, 16, [], allowCustomRows: custom, askSize: custom, templates: rig.Settings.SheetTemplates, dialogs: rig.Dialogs) { };

    private static SheetTemplate T(RsiStateViewModel d, string id) => d.Templates.First(t => t.Id == id);

    [Fact]
    public void DelayTypedAfterChoosingATemplate_AppliesToEveryFrame_AndUpdatesTheLines()
    {
        using var rig = new Rig();
        var d = Dialog(rig);
        d.SelectedTemplate = T(d, "system:attack-4");      // замах 0.12, удар 0.08, возврат 0.15
        d.DelayText = "0.3";
        Assert.Equal("замах, 0.3\nудар, 0.3\nвозврат, 0.3", d.FrameLinesText);
        d.Name = "a";
        d.OkCommand.Execute(null);
        Assert.Equal([0.3, 0.3, 0.3], d.Result!.FrameDelays);
        Assert.Equal(["замах", "удар", "возврат"], d.Result.FrameNames);
        Assert.Equal(0.3, d.Result.Delay);
    }

    [Fact]
    public void DelayTypedBeforeChoosingATemplate_SurvivesTheChoice_NamesComeFromTheTemplate()
    {
        using var rig = new Rig();
        var d = Dialog(rig);
        d.DelayText = "0.25";
        d.SelectedTemplate = T(d, "system:attack-4");
        Assert.Equal("0.25", d.DelayText);
        Assert.Equal("замах, 0.25\nудар, 0.25\nвозврат, 0.25", d.FrameLinesText);
        d.SelectedTemplate = T(d, "system:walk-4");        // another template: names change, the typed delay stays
        Assert.Equal("стоит, 0.25\nшаг 1, 0.25\nшаг 2, 0.25", d.FrameLinesText);
        d.Name = "a";
        d.OkCommand.Execute(null);
        Assert.Equal([0.25, 0.25, 0.25], d.Result!.FrameDelays);
    }

    [Fact]
    public void ATemplateWithoutATypedDelay_KeepsItsOwnDelays_AndTheFieldAdmitsTheyDiffer()
    {
        using var rig = new Rig();
        var d = Dialog(rig);
        d.SelectedTemplate = T(d, "system:attack-4");
        Assert.Equal("", d.DelayText);                      // not one value: the list below is the truth
        Assert.False(string.IsNullOrEmpty(d.DelayInfo));
        d.Name = "a";
        d.OkCommand.Execute(null);
        Assert.Equal([0.12, 0.08, 0.15], d.Result!.FrameDelays);

        var uniform = Dialog(rig);
        uniform.SelectedTemplate = T(uniform, "system:walk-4");   // 0.2 everywhere
        Assert.Equal("0.2", uniform.DelayText);
        Assert.True(string.IsNullOrEmpty(uniform.DelayInfo));
    }

    [Fact]
    public void ALineEditWinsForThatFrame_AndTheFieldFollowsTheLines()
    {
        using var rig = new Rig();
        var d = Dialog(rig);
        d.SelectedTemplate = T(d, "system:walk-4");
        d.FrameLinesText = "стоит, 0.5\nшаг 1, 0.2\nшаг 2, 0.2";
        Assert.Equal("", d.DelayText);
        d.Name = "a";
        d.OkCommand.Execute(null);
        Assert.Equal([0.5, 0.2, 0.2], d.Result!.FrameDelays);

        var e = Dialog(rig);
        e.SelectedTemplate = T(e, "system:walk-4");
        e.FrameLinesText = "стоит, 0.4\nшаг 1, 0.4\nшаг 2, 0.4";
        Assert.Equal("0.4", e.DelayText);                   // all equal again: the field shows it
    }

    [Fact]
    public void FrameCountAfterATemplate_ResizesTheList_InsteadOfBeingIgnored()
    {
        using var rig = new Rig();
        var d = Dialog(rig);
        d.SelectedTemplate = T(d, "system:walk-4");
        d.DelayText = "0.3";
        d.FramesText = "5";
        d.Name = "a";
        d.OkCommand.Execute(null);
        Assert.Equal(5, d.Result!.Frames);
        Assert.Equal(["стоит", "шаг 1", "шаг 2", "4", "5"], d.Result.FrameNames);
        Assert.Equal([0.3, 0.3, 0.3, 0.3, 0.3], d.Result.FrameDelays);

        var s = Dialog(rig);
        s.SelectedTemplate = T(s, "system:walk-4");
        s.FramesText = "2";
        Assert.Equal(2, s.FrameLinesText.Split('\n').Length);
        Assert.Equal("2", s.FramesText);
    }

    [Fact]
    public void ListEdits_KeepTheFrameCountFieldInSync()
    {
        using var rig = new Rig();
        var d = Dialog(rig);
        d.FrameLinesText = "a, 0.1\nb, 0.1\nc, 0.1\nd, 0.1";
        Assert.Equal("4", d.FramesText);
        Assert.Equal("0.1", d.DelayText);
    }

    [Theory]
    [InlineData("0,15", 0.15)]
    [InlineData(" 0.15 ", 0.15)]
    [InlineData("1,5", 1.5)]
    public void DelayAcceptsCommaAndDot(string typed, double expected)
    {
        using var rig = new Rig();
        var d = Dialog(rig);
        d.SelectedTemplate = T(d, "system:walk-4");
        d.DelayText = typed;
        d.Name = "a";
        d.OkCommand.Execute(null);
        Assert.Equal(expected, d.Result!.FrameDelays![0], 6);
        Assert.Equal(expected, d.Result.FrameDelays[2], 6);
    }

    [Fact]
    public void ALineWithACommaDelay_IsRead()
    {
        var d = new RsiStateViewModel(16, 16, []);
        d.FrameLinesText = "a, 0,5\nb, 0.25";
        d.Name = "a";
        d.OkCommand.Execute(null);
        Assert.Equal([0.5, 0.25], d.Result!.FrameDelays);
    }

    [Fact]
    public void AnInvalidDelayInTheField_IsReported_NotSilentlyReplacedBy01()
    {
        using var rig = new Rig();
        var d = Dialog(rig);
        d.SelectedTemplate = T(d, "system:walk-4");
        d.DelayText = "abc";
        d.Name = "a";
        d.OkCommand.Execute(null);
        Assert.NotEqual("", d.Error);
        Assert.Null(d.Result);
        d.DelayText = "0.5";
        d.OkCommand.Execute(null);
        Assert.Equal("", d.Error);
        Assert.Equal([0.5, 0.5, 0.5], d.Result!.FrameDelays);
    }

    [Fact]
    public void WithoutATemplate_TheFieldIsTheUniformDelay_AsBefore()
    {
        var d = new RsiStateViewModel(16, 16, []);
        d.FramesText = "3";
        d.DelayText = "0.45";
        d.Name = "a";
        d.OkCommand.Execute(null);
        Assert.Equal([0.45, 0.45, 0.45], d.Result!.FrameDelays);
        Assert.Null(d.Result.FrameNames);
    }

    [Fact]
    public void RememberedDelayAndTemplate_ComeBackWhenTheDialogReopens()
    {
        using var rig = new Rig();
        var d = new RsiStateViewModel(16, 16, [], allowCustomRows: true, askSize: true, templates: rig.Settings.SheetTemplates, dialogs: rig.Dialogs,
            lastTemplateId: "system:attack-4", lastDelay: 0.3);
        Assert.Equal("system:attack-4", d.SelectedTemplate!.Id);
        Assert.Equal("0.3", d.DelayText);
        Assert.Equal("замах, 0.3\nудар, 0.3\nвозврат, 0.3", d.FrameLinesText);
        Assert.Equal(0.3, d.UserDelay);
    }

    [Fact]
    public void NewAnimationSheet_GetsExactlyTheDelaysTheDialogShowed_InTheBinding_AndTheExport()
    {
        using var rig = new Rig();
        rig.Dialogs.RsiStateConfigure = d =>
        {
            d.SelectedTemplate = d.Templates.First(t => t.Id == "system:attack-4");
            d.DelayText = "0.3";
            d.Name = "hit";
        };
        rig.Vm.Rsi.NewSheetCommand.Execute(null);
        var b = rig.Doc.Rsi!;
        Assert.All(b.Delays, row => Assert.Equal([0.3, 0.3, 0.3], row));
        Assert.Equal(["замах", "удар", "возврат"], b.FrameNames);
        Assert.Equal(0.3, rig.Settings.LastSheetDelay);                 // remembered for the next dialog
        Assert.Equal("system:attack-4", rig.Settings.LastSheetTemplateId);

        // the animation preview and the export read the same binding
        rig.Vm.Sheet.AnimationCommand.Execute(null);
        Assert.Equal(0.3, rig.Dialogs.AnimationWindows[^1].Delays![0][1]);
    }

    [Fact]
    public void NewRsiState_WritesTheTypedDelaysToMetaJson()
    {
        using var rig = new Rig();
        rig.Dialogs.RsiProjectConfigure = d => { d.ParentFolder = rig.Dir; d.Name = "mob"; d.License = "CC0-1.0"; d.FrameWidthText = d.FrameHeightText = "16"; };
        rig.Vm.Rsi.NewProjectCommand.Execute(null);
        rig.Dialogs.RsiStateConfigure = d =>
        {
            d.SelectedTemplate = d.Templates.First(t => t.Id == "system:walk-4");
            d.DelayText = "0.35";
            d.Name = "walk";
        };
        rig.Vm.Rsi.NewStateCommand.Execute(null);
        rig.Vm.Rsi.SaveStateCommand.Execute(null);
        var meta = RsiMeta.Parse(File.ReadAllText(Path.Combine(rig.Dir, "mob.rsi", "meta.json")));
        var s = meta.Find("walk")!;
        Assert.Equal(3, s.Frames);
        Assert.All(s.Delays, row => Assert.Equal([0.35, 0.35, 0.35], row));
    }
}
