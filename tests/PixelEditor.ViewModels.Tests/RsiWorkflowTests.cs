// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using PixelEditor.Core;
using PixelEditor.ViewModels;

namespace PixelEditor.ViewModels.Tests;

public class RsiWorkflowTests
{
    private static string Folder(Rig rig) => Path.Combine(rig.Dir, "mob.rsi");

    private static void CreateProject(Rig rig, int w = 16, int h = 16, string license = "CC0-1.0")
    {
        rig.Dialogs.RsiProjectConfigure = d =>
        {
            d.ParentFolder = rig.Dir;
            d.Name = "mob";
            d.License = license;
            d.Copyright = "тест";
            d.FrameWidthText = w.ToString();
            d.FrameHeightText = h.ToString();
        };
        rig.Vm.Rsi.NewProjectCommand.Execute(null);
    }

    private static void CreateState(Rig rig, string name, int dirs, int frames, string delay = "0.2")
    {
        rig.Dialogs.RsiStateConfigure = d =>
        {
            d.Name = name;
            d.SelectedDirections = d.DirectionChoices.First(c => c.Key == dirs.ToString());
            d.FramesText = frames.ToString();
            d.DelayText = delay;
        };
        rig.Vm.Rsi.NewStateCommand.Execute(null);
    }

    [Fact]
    public void NewProject_CreatesFolderWithMeta_AndRemembersIt()
    {
        using var rig = new Rig();
        CreateProject(rig);
        Assert.True(File.Exists(Path.Combine(Folder(rig), "meta.json")));
        var meta = RsiMeta.Parse(File.ReadAllText(Path.Combine(Folder(rig), "meta.json")));
        Assert.Equal(("CC0-1.0", "тест", 16, 16), (meta.License, meta.Copyright, meta.FrameWidth, meta.FrameHeight));
        Assert.Empty(meta.States);
        Assert.Equal(Folder(rig), rig.Vm.Rsi.CurrentFolder);
        Assert.Equal(rig.Dir, rig.Settings.LastRsiParent);

        // a second time into the same folder: an error, the existing RSI is not overwritten
        CreateProject(rig);
        Assert.Single(rig.Dialogs.Errors);
    }

    [Fact]
    public void NewProject_ValidatesFields()
    {
        var vm = new RsiProjectViewModel(new FakeDialogs(), true, "", "", "CC0-1.0", "", 32, 32);
        vm.OkCommand.Execute(null);
        Assert.Contains("название", vm.Error);
        vm.Name = "a";
        vm.OkCommand.Execute(null);
        Assert.Contains("папку", vm.Error);
        vm.ParentFolder = "C:/x";
        vm.FrameWidthText = "0";
        vm.OkCommand.Execute(null);
        Assert.Contains("кадра", vm.Error);
        vm.FrameWidthText = "32";
        vm.License = " ";                                // the license is optional now: an empty one is accepted and saved as ""
        vm.OkCommand.Execute(null);
        Assert.Equal("", vm.Error);
        Assert.EndsWith("a.rsi", vm.Result!.Folder);
        Assert.Equal("", vm.Result.License);
    }

    [Fact]
    public void NewState_BuildsSheetWithZonesAndBinding()
    {
        using var rig = new Rig();
        CreateProject(rig);
        CreateState(rig, "walk", 4, 3);

        Assert.Equal((48, 64), (rig.Doc.Width, rig.Doc.Height));
        Assert.Equal((16, 16), (rig.Doc.SheetCellWidth, rig.Doc.SheetCellHeight));
        var b = rig.Doc.Rsi!;
        Assert.Equal(("walk", 4, 3), (b.State, b.Directions, b.Frames));
        Assert.Equal(0.2, b.Delays[3][2], 3);
        Assert.Contains("walk", rig.Vm.Rsi.StatusLine);
        Assert.True(rig.Vm.Rsi.SaveStateCommand.CanExecute(null));
    }

    [Fact]
    public void NewState_WithoutProject_ExplainsWhatToDo_AndDuplicateNameIsRejected()
    {
        using var rig = new Rig();
        rig.Vm.Rsi.NewStateCommand.Execute(null);
        Assert.Single(rig.Dialogs.Infos);

        CreateProject(rig);
        CreateState(rig, "a", 1, 1);
        rig.Vm.Rsi.SaveStateCommand.Execute(null);
        var vm = new RsiStateViewModel(16, 16, ["a"]);
        vm.Name = "A";
        vm.OkCommand.Execute(null);
        Assert.Contains("уже есть", vm.Error);
    }

    [Fact]
    public void SaveState_WritesPng_UpdatesMeta_AndKeepsOtherStates_ThenReopens()
    {
        using var rig = new Rig();
        CreateProject(rig);
        CreateState(rig, "walk", 4, 2);
        rig.Doc.Layers[0].Pixels[16 * 32 + 20] = Rig.Red; // zone N frame 1
        rig.Vm.Rsi.SaveStateCommand.Execute(null);

        var png = Path.Combine(Folder(rig), "walk.png");
        Assert.True(rig.Images.Files.ContainsKey(png));
        Assert.Equal(Rig.Red, rig.Images.Files[png].Pixels[16 * 32 + 20]);

        CreateState(rig, "open", 1, 1);
        rig.Doc.Layers[0].Pixels[3] = Rig.Blue;
        rig.Vm.Rsi.SaveStateCommand.Execute(null);
        var meta = RsiMeta.Parse(File.ReadAllText(Path.Combine(Folder(rig), "meta.json")));
        Assert.Equal(["walk", "open"], meta.States.Select(s => s.Name));
        Assert.Equal((4, 2), (meta.Find("walk")!.Directions, meta.Find("walk")!.Frames));

        // open walk back: the pixel is in place, the binding is restored
        rig.Dialogs.RsiOpenConfigure = d => { d.Folder = Folder(rig); d.SelectedState = "walk"; };
        rig.Vm.Rsi.OpenStateCommand.Execute(null);
        Assert.Equal("walk", rig.Doc.Rsi!.State);
        Assert.Equal((32, 64), (rig.Doc.Width, rig.Doc.Height));
        Assert.Equal(Rig.Red, rig.Layer.Get(20, 16));
        Assert.False(rig.S.Dirty);
    }

    [Fact]
    public void SaveState_RejectsSheetWhoseSizeWasChanged()
    {
        using var rig = new Rig();
        CreateProject(rig);
        CreateState(rig, "x", 4, 2);
        rig.Doc.ResizeCanvas(40, 40);
        rig.Vm.Rsi.SaveStateCommand.Execute(null);
        Assert.Single(rig.Dialogs.Errors);
        Assert.Contains("не совпадает", rig.Dialogs.Errors[0]);
        Assert.False(File.Exists(Path.Combine(Folder(rig), "x.png")));
    }

    [Fact]
    public void OpenDialog_ListsStatesOfMeta_AndReportsNotRsi()
    {
        using var rig = new Rig();
        CreateProject(rig);
        CreateState(rig, "one", 1, 1);
        rig.Vm.Rsi.SaveStateCommand.Execute(null);

        var dlg = new RsiOpenViewModel(rig.Dialogs, Folder(rig));
        Assert.Equal(["one"], dlg.States);
        Assert.Equal("one", dlg.SelectedState);
        var bad = new RsiOpenViewModel(rig.Dialogs, rig.Dir);
        Assert.Contains("не RSI", bad.Error);
        Assert.False(bad.OkCommand.CanExecute(null));
    }

    [Fact]
    public void EditMeta_ChangesLicense_ButNotSizeWhenStatesExist()
    {
        using var rig = new Rig();
        CreateProject(rig);
        CreateState(rig, "a", 1, 1);
        rig.Vm.Rsi.SaveStateCommand.Execute(null);

        rig.Dialogs.RsiProjectConfigure = d => { d.License = "MIT"; d.FrameWidthText = "32"; };
        rig.Vm.Rsi.EditMetaCommand.Execute(null);
        Assert.Single(rig.Dialogs.Warnings);
        Assert.Equal("CC0-1.0", RsiMeta.Parse(File.ReadAllText(Path.Combine(Folder(rig), "meta.json"))).License);

        rig.Dialogs.RsiProjectConfigure = d => d.License = "MIT";
        rig.Vm.Rsi.EditMetaCommand.Execute(null);
        Assert.Equal("MIT", RsiMeta.Parse(File.ReadAllText(Path.Combine(Folder(rig), "meta.json"))).License);
    }

    [Fact]
    public void ExportAnimation_WritesSheetAndSelectedFormats()
    {
        using var rig = new Rig();
        CreateProject(rig);
        CreateState(rig, "walk", 4, 3);
        var outDir = Path.Combine(rig.Dir, "export");
        rig.Dialogs.ExportConfigure = d =>
        {
            d.Folder = outDir;
            d.BaseName = "hero";
            d.Simple = true;
            d.GodotTexturePath = "res://hero.png";
        };
        rig.Vm.Rsi.ExportAnimationCommand.Execute(null);

        Assert.True(rig.Images.Files.ContainsKey(Path.Combine(outDir, "hero.png")));
        Assert.True(File.Exists(Path.Combine(outDir, "hero.aseprite.json")));
        Assert.Contains("res://hero.png", File.ReadAllText(Path.Combine(outDir, "hero.tres")));
        Assert.True(File.Exists(Path.Combine(outDir, "hero.json")));
        Assert.Equal(4, Directory.GetFiles(outDir, "*.gif").Length); // a GIF per direction
        Assert.Equal([outDir], rig.Shell.Opened);
        Assert.Equal(outDir, rig.Settings.LastExportFolder);
    }

    [Fact]
    public void ExportAnimation_WorksFromPlainSheetGrid_AndComplainsWithoutOne()
    {
        using var rig = new Rig();
        rig.Vm.Rsi.ExportAnimationCommand.Execute(null);
        Assert.Single(rig.Dialogs.Infos);

        rig.Doc.SheetCellWidth = rig.Doc.SheetCellHeight = 16; // a 32×32 canvas: 2 frames × 2 rows
        var outDir = Path.Combine(rig.Dir, "plain");
        rig.Dialogs.ExportConfigure = d => { d.Folder = outDir; d.Gif = false; d.Godot = false; };
        rig.Vm.Rsi.ExportAnimationCommand.Execute(null);
        var json = File.ReadAllText(Directory.GetFiles(outDir, "*.aseprite.json").Single());
        Assert.Contains("frameTags", json);
        Assert.Empty(Directory.GetFiles(outDir, "*.gif"));
    }

    [Fact]
    public void RsiMeta_IsBackedByFiles_SoNewRigStartsWithoutProject()
    {
        using var rig = new Rig();
        Assert.Null(rig.Vm.Rsi.CurrentFolder);
        Assert.Equal("RSI не выбран", rig.Vm.Rsi.StatusLine);
        Assert.False(rig.Vm.Rsi.SaveStateCommand.CanExecute(null));
        Assert.False(rig.Vm.Rsi.EditMetaCommand.CanExecute(null));
    }
}
