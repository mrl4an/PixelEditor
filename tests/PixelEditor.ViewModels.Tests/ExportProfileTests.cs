// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using PixelEditor.Core;
using PixelEditor.ViewModels;

namespace PixelEditor.ViewModels.Tests;

/// <summary>Per-row loop flags, the Unity script option and the remembered export profile ("Export again").</summary>
public class ExportProfileTests
{
    private static void SetUpSheet(Rig rig)
    {
        rig.Doc.SheetCellWidth = rig.Doc.SheetCellHeight = 16;   // a 32x32 canvas: 2 frames x 2 rows
    }

    [Fact]
    public void Dialog_ListsRowsWithLoopChecked_AndReportsUnchecked()
    {
        var dlg = new AnimationExportViewModel(new FakeDialogs(), "C:/o", "hero", "", ["idle", "attack"]);
        Assert.All(dlg.Loops, l => Assert.True(l.Loop));
        dlg.Loops[1].Loop = false;
        dlg.Unity = true;
        dlg.OkCommand.Execute(null);
        Assert.Equal(["attack"], dlg.Result!.NoLoopRows);
        Assert.True(dlg.Result.Unity);
    }

    [Fact]
    public void Export_WritesLoopFlagsAndUnityScript_AndRemembersProfile()
    {
        using var rig = new Rig();
        SetUpSheet(rig);
        Assert.False(rig.Vm.Rsi.ExportAgainCommand.CanExecute(null));   // nothing exported yet
        var outDir = Path.Combine(rig.Dir, "out");
        rig.Dialogs.ExportConfigure = d =>
        {
            d.Folder = outDir;
            d.BaseName = "hero";
            d.Unity = true;
            d.Gif = false;
            d.Loops[0].Loop = false;
        };
        rig.Vm.Rsi.ExportAnimationCommand.Execute(null);

        var script = Path.Combine(outDir, AnimationExport.UnityScriptFileName);
        Assert.Equal(AnimationExport.UnityImporterScript, File.ReadAllText(script));
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(outDir, "hero.json")))!;   // written because Unity needs it
        Assert.False(json["animations"]![0]!["loop"]!.GetValue<bool>());
        Assert.True(json["animations"]![1]!["loop"]!.GetValue<bool>());

        var profile = rig.Doc.ExportProfile!;
        Assert.Equal((outDir, "hero", true, false), (profile.Folder, profile.BaseName, profile.Unity, profile.Gif));
        Assert.Single(profile.NoLoop);
        Assert.True(rig.Vm.Rsi.ExportAgainCommand.CanExecute(null));
    }

    [Fact]
    public void Dialog_IsPrefilledFromProfile()
    {
        using var rig = new Rig();
        SetUpSheet(rig);
        var outDir = Path.Combine(rig.Dir, "out");
        rig.Doc.ExportProfile = new AnimationExportProfile(outDir, "mine", Aseprite: false, Godot: true, Gif: false, Simple: true, Unity: false, "res://x/mine.png", ["N"]);
        AnimationExportViewModel? seen = null;
        rig.Dialogs.ExportConfigure = d => seen = d;
        rig.Vm.Rsi.ExportAnimationCommand.Execute(null);

        Assert.Equal((outDir, "mine", false, true), (seen!.Folder, seen.BaseName, seen.Aseprite, seen.Simple));
        Assert.Equal("res://x/mine.png", seen.GodotTexturePath);
        Assert.Equal([("S", true), ("N", false)], seen.Loops.Select(l => (l.Name, l.Loop)));
    }

    [Fact]
    public void ExportAgain_RepeatsWithoutDialog_AndReportsInStatusBar()
    {
        using var rig = new Rig();
        SetUpSheet(rig);
        var outDir = Path.Combine(rig.Dir, "again");
        rig.Doc.ExportProfile = new AnimationExportProfile(outDir, "hero", Aseprite: false, Godot: false, Gif: false, Simple: true, Unity: false, "res://hero.png", ["N"]);
        rig.Vm.Rsi.ExportAgainCommand.RaiseCanExecuteChanged();
        Assert.True(rig.Vm.Rsi.ExportAgainCommand.CanExecute(null));

        rig.Vm.Rsi.ExportAgainCommand.Execute(null);

        Assert.True(rig.Images.Files.ContainsKey(Path.Combine(outDir, "hero.png")));
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(outDir, "hero.json")))!;
        Assert.True(json["animations"]![0]!["loop"]!.GetValue<bool>());
        Assert.False(json["animations"]![1]!["loop"]!.GetValue<bool>());
        Assert.Contains("hero", rig.S.StatusText);
        Assert.Empty(rig.Dialogs.Errors);
    }
}
