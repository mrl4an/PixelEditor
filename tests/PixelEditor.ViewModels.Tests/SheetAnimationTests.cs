// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using PixelEditor.Core;
using PixelEditor.ViewModels;

namespace PixelEditor.ViewModels.Tests;

public class SheetAnimationTests
{
    private static PixelImage Frame(uint c) { var i = new PixelImage(2, 2); i.Pixels[0] = c; return i; }

    private static (AnimationViewModel Vm, FakeTimers Timers, List<double[][]> Saved) Make()
    {
        var timers = new FakeTimers();
        var saved = new List<double[][]>();
        var rows = new List<AnimationRow>
        {
            new("S", [Frame(Rig.Red), Frame(Rig.Blue), Frame(Rig.Red)]),
            new("N", [Frame(Rig.Blue), Frame(Rig.Blue), Frame(Rig.Blue)]),
        };
        double[][] delays = [[0.1, 0.5, 0.2], [0.3, 0.3, 0.3]];
        var vm = new AnimationViewModel(new FakeDialogs(), timers, rows, delays, "лист", saved.Add);
        return (vm, timers, saved);
    }

    [Fact]
    public void SheetMode_PlaysWithPerFrameDelays_AndSwitchesRows()
    {
        var (vm, timers, _) = Make();
        var timer = timers.Created[0];
        Assert.True(vm.UsesSheetDelays);
        Assert.False(vm.UsesFps);
        Assert.True(vm.HasRows);
        Assert.Equal(["S", "N"], vm.RowNames);
        Assert.Equal(100, timer.Interval.TotalMilliseconds);

        timer.Fire(); // frame 1: a delay of 0.5 s
        Assert.Equal(500, timer.Interval.TotalMilliseconds);
        Assert.Equal("0.5", vm.FrameDelayText);
        timer.Fire();
        Assert.Equal(200, timer.Interval.TotalMilliseconds);

        vm.SelectedRowIndex = 1;
        Assert.Equal("1 / 3", vm.FrameLabel);
        Assert.Equal(Rig.Blue, vm.CurrentFrame.Pixels[0]);
        Assert.Equal("0.3", vm.FrameDelayText);
        Assert.Contains("0.9", vm.RowSummary);
    }

    [Fact]
    public void EditingDelays_ChangesOneFrame_RowOrAll_AndSaveReturnsThemToTheSheet()
    {
        var (vm, _, saved) = Make();
        vm.FrameDelayText = "0.25"; // frame 0 of row S
        Assert.Equal(0.25, vm.Delays![0][0], 3);
        Assert.Equal(0.5, vm.Delays![0][1], 3);

        vm.ApplyToRowCommand.Execute(null);
        Assert.All(vm.Delays![0], d => Assert.Equal(0.25, d, 3));
        Assert.Equal(0.3, vm.Delays![1][0], 3);
        vm.ApplyToAllCommand.Execute(null);
        Assert.All(vm.Delays!.SelectMany(r => r), d => Assert.Equal(0.25, d, 3));

        vm.FrameDelayText = "abc"; // an invalid value is ignored
        Assert.Equal(0.25, vm.Delays![0][0], 3);
        vm.FrameDelayText = "-1";
        Assert.Equal(0.25, vm.Delays![0][0], 3);

        vm.SaveDelaysCommand.Execute(null);
        Assert.Single(saved);
        Assert.Equal(0.25, saved[0][1][2], 3);
    }

    [Fact]
    public void PlainMode_StillUsesFps()
    {
        var vm = new AnimationViewModel(new FakeDialogs(), new FakeTimers(), [Frame(Rig.Red), Frame(Rig.Blue)], "x");
        Assert.True(vm.UsesFps);
        Assert.False(vm.UsesSheetDelays);
        Assert.False(vm.HasRows);
        Assert.False(vm.SaveDelaysCommand.CanExecute(null));
    }

    [Fact]
    public void SheetViewModel_OnRsiSheet_OpensRowsAnimation_AndSavedDelaysLandInBinding()
    {
        using var rig = new Rig();
        rig.Doc.ResizeCanvas(32, 16);
        rig.Doc.Rsi = new RsiBinding("", "hero", 2, 2, 16, 8, RsiState.UniformDelays(2, 2, 0.1), ["idle", "run"]);
        rig.Doc.ResizeCanvas(32, 16);
        rig.Vm.Sheet.AnimationCommand.Execute(null);

        var anim = Assert.Single(rig.Dialogs.AnimationWindows);
        Assert.True(anim.HasRows);
        Assert.Equal(["idle", "run"], anim.RowNames);
        Assert.Equal(2, anim.Frames.Count);

        anim.FrameDelayText = "0.4";
        anim.ApplyToAllCommand.Execute(null);
        anim.SaveDelaysCommand.Execute(null);
        Assert.Equal(0.4, rig.Doc.Rsi!.Delays[1][1], 3);
        Assert.True(rig.S.Dirty);
    }
}
