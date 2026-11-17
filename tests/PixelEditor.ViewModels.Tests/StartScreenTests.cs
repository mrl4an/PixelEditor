// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using PixelEditor.Core;
using PixelEditor.ViewModels;

namespace PixelEditor.ViewModels.Tests;

public class StartScreenTests
{
    private static string MakeProject(Rig rig, string name, int size = 9)
    {
        var path = Path.Combine(rig.Dir, name);
        ProjectIO.Save(new Document(size, size), path);
        return path;
    }

    // ------------------------------------------------------------ startup decision

    [Fact]
    public void Startup_NoFile_ShowsStartScreenOnce()
    {
        using var rig = new Rig();
        rig.Vm.Startup(null);
        Assert.Single(rig.Dialogs.StartScreens);
    }

    [Fact]
    public void Startup_WithFile_DoesNotShowStartScreen_AndOpensTheFile()
    {
        using var rig = new Rig();
        var proj = MakeProject(rig, "a.pixproj", 7);
        rig.Vm.Startup(proj);
        Assert.Empty(rig.Dialogs.StartScreens);
        Assert.Equal(7, rig.Doc.Width);
    }

    [Fact]
    public void Startup_SettingOff_DoesNotShowStartScreen()
    {
        using var rig = new Rig();
        rig.Settings.ShowStartScreen = false;
        rig.Vm.Startup(null);
        Assert.Empty(rig.Dialogs.StartScreens);
    }

    [Fact]
    public void Startup_RestoredAutosave_DoesNotShowStartScreen()
    {
        using var rig = new Rig();
        rig.Stroke(0, 0, 0, 0);
        rig.Vm.Autosave();
        var second = new MainViewModel(rig.Services, rig.Settings);
        rig.Dialogs.ConfirmFn = _ => true;   // "restore"
        second.Startup(null);
        Assert.Empty(rig.Dialogs.StartScreens);
        Assert.True(second.Session.Dirty);
    }

    [Fact]
    public void Startup_DeclinedAutosave_StillShowsStartScreen()
    {
        using var rig = new Rig();
        rig.Stroke(0, 0, 0, 0);
        rig.Vm.Autosave();
        rig.Dialogs.ConfirmFn = _ => false;
        rig.Vm.Startup(null);
        Assert.Single(rig.Dialogs.StartScreens);
    }

    [Fact]
    public void StartScreenCommand_ShowsTheScreen_EvenWhenDisabledAtStartup()
    {
        using var rig = new Rig();
        rig.Settings.ShowStartScreen = false;
        rig.Vm.StartScreenCommand.Execute(null);
        Assert.Single(rig.Dialogs.StartScreens);
    }

    [Fact]
    public void Closing_JustLeavesTheCurrentDocument()
    {
        using var rig = new Rig();
        var doc = rig.Doc;
        rig.Dialogs.StartScreenConfigure = s => s.CancelCommand.Execute(null);
        rig.Vm.Startup(null);
        Assert.Same(doc, rig.Doc);
        Assert.Empty(rig.Dialogs.Errors);
    }

    // ------------------------------------------------------------ create

    [Fact]
    public void Sizes_ArePresets_AndTheLastUsedSizeIsAddedWhenDifferent()
    {
        using var rig = new Rig();   // the rig remembers 32x32, which is a preset
        var plain = new StartScreenViewModel(rig.Vm);
        Assert.Equal(["16×16", "32×32", "48×48", "64×64"], plain.Sizes.Select(s => s.Label));

        rig.Settings.DefaultWidth = 24;
        rig.Settings.DefaultHeight = 40;
        var withLast = new StartScreenViewModel(rig.Vm);
        Assert.Equal(["16×16", "32×32", "48×48", "64×64", "24×40"], withLast.Sizes.Select(s => s.Label));
    }

    [Fact]
    public void SizeButton_CreatesBlankDocument_ClosesAndRemembersSize()
    {
        using var rig = new Rig();
        rig.Dialogs.StartScreenConfigure = s => s.NewSizeCommand.Execute(s.Sizes.First(z => z.Width == 16));
        rig.Vm.Startup(null);
        Assert.Equal((16, 16), (rig.Doc.Width, rig.Doc.Height));
        Assert.Equal((16, 16), (rig.Settings.DefaultWidth, rig.Settings.DefaultHeight));
        Assert.Single(rig.Dialogs.StartScreens);
    }

    [Fact]
    public void OtherSize_OpensNewDocumentDialog_AndCancelDoesNotReopenStartScreen()
    {
        using var rig = new Rig();
        int asked = 0;
        rig.Dialogs.NewDocFn = (_, _) => { asked++; return null; };   // the user cancels
        rig.Dialogs.StartScreenConfigure = s => s.OtherSizeCommand.Execute(null);
        rig.Vm.Startup(null);
        Assert.Equal(1, asked);
        Assert.Single(rig.Dialogs.StartScreens);
        Assert.Equal(32, rig.Doc.Width);
    }

    [Fact]
    public void OtherSize_CreatesTheChosenDocument()
    {
        using var rig = new Rig();
        rig.Dialogs.NewDocFn = (_, _) => new NewDocumentResult(20, 12);
        rig.Dialogs.StartScreenConfigure = s => s.OtherSizeCommand.Execute(null);
        rig.Vm.Startup(null);
        Assert.Equal((20, 12), (rig.Doc.Width, rig.Doc.Height));
    }

    [Fact]
    public void AnimationSheet_RunsTheExistingNewSheetFlow()
    {
        using var rig = new Rig();
        bool asked = false;
        rig.Dialogs.RsiStateConfigure = _ => asked = true;
        rig.Dialogs.StartScreenConfigure = s => s.AnimationSheetCommand.Execute(null);
        rig.Vm.Startup(null);
        Assert.True(asked);
        Assert.NotNull(rig.Doc.Rsi);
        Assert.False(rig.Doc.Rsi!.IsRsi);
        Assert.Single(rig.Dialogs.StartScreens);
    }

    [Fact]
    public void RsiState_WithoutRsiFolder_StartsTheNewRsiFlow()
    {
        using var rig = new Rig();
        bool projectAsked = false;
        rig.Dialogs.RsiProjectConfigure = d =>
        {
            projectAsked = true;
            d.ParentFolder = rig.Dir;
            d.Name = "mob";
            d.License = "CC0-1.0";
            d.Copyright = "test";
        };
        rig.Dialogs.RsiStateConfigure = d => { d.Name = "idle"; };   // the flow offers the first state right away
        rig.Dialogs.StartScreenConfigure = s => s.RsiStateCommand.Execute(null);
        rig.Vm.Startup(null);
        Assert.True(projectAsked);
        Assert.Empty(rig.Dialogs.Errors);
        Assert.True(rig.Doc.Rsi is { IsRsi: true });
    }

    [Fact]
    public void RsiState_WithRsiFolder_StartsTheNewStateFlow()
    {
        using var rig = new Rig();
        rig.Dialogs.RsiProjectConfigure = d => { d.ParentFolder = rig.Dir; d.Name = "mob"; d.License = "CC0-1.0"; d.Copyright = "test"; };
        rig.Vm.Rsi.NewProjectCommand.Execute(null);
        Assert.NotNull(rig.Vm.Rsi.CurrentFolder);

        int projectAsked = 0;
        rig.Dialogs.RsiProjectConfigure = _ => projectAsked++;
        rig.Dialogs.RsiStateConfigure = d => { d.Name = "walk"; };
        rig.Dialogs.StartScreenConfigure = s => s.RsiStateCommand.Execute(null);
        rig.Vm.StartScreenCommand.Execute(null);
        Assert.Equal(0, projectAsked);
        Assert.Equal("walk", rig.Doc.Rsi?.State);
    }

    // ------------------------------------------------------------ open

    [Fact]
    public void Open_UsesTheExistingOpenCommand_AfterClosing()
    {
        using var rig = new Rig();
        var proj = MakeProject(rig, "o.pixproj", 11);
        rig.Dialogs.OpenFileFn = () => proj;
        rig.Dialogs.StartScreenConfigure = s => s.OpenCommand.Execute(null);
        rig.Vm.Startup(null);
        Assert.Equal(11, rig.Doc.Width);
        Assert.Single(rig.Dialogs.StartScreens);
    }

    [Fact]
    public void Import_UsesTheExistingImportFlow()
    {
        using var rig = new Rig();
        var img = new PixelImage(5, 5);
        img.Set(1, 1, Rig.Red);
        rig.Images.Files["pic.png"] = img;
        rig.Dialogs.OpenFileFn = () => "pic.png";
        bool importDialog = false;
        rig.Dialogs.ImportConfigure = _ => importDialog = true;
        rig.Dialogs.StartScreenConfigure = s => s.ImportCommand.Execute(null);
        rig.Vm.Startup(null);
        Assert.True(importDialog);
        Assert.Single(rig.Dialogs.StartScreens);
    }

    // ------------------------------------------------------------ recent

    [Fact]
    public void Recent_ListsExistingFiles_WithNameAndFolder_AndClickOpens()
    {
        using var rig = new Rig();
        var a = MakeProject(rig, "first.pixproj", 6);
        var b = MakeProject(rig, "second.pixproj", 8);
        rig.Settings.AddRecent(a);
        rig.Settings.AddRecent(b);
        rig.Settings.AddRecent(Path.Combine(rig.Dir, "gone.pixproj"));   // a missing file is skipped as in the Recent menu

        var screen = new StartScreenViewModel(rig.Vm);
        Assert.Equal(["second.pixproj", "first.pixproj"], screen.Recent.Select(r => r.Name));
        Assert.All(screen.Recent, r => Assert.Equal(rig.Dir, r.Folder));
        Assert.True(screen.HasRecent);

        rig.Dialogs.StartScreenConfigure = s => s.OpenRecentCommand.Execute(s.Recent.First(r => r.Name == "first.pixproj"));
        rig.Vm.StartScreenCommand.Execute(null);
        Assert.Equal(6, rig.Doc.Width);
    }

    [Fact]
    public void Recent_EmptyList_IsReported()
    {
        using var rig = new Rig();
        var screen = new StartScreenViewModel(rig.Vm);
        Assert.Empty(screen.Recent);
        Assert.True(screen.HasNoRecent);
    }

    // ------------------------------------------------------------ setting

    [Fact]
    public void ShowAtStartup_PersistsInSettings()
    {
        using var rig = new Rig();
        var screen = new StartScreenViewModel(rig.Vm);
        Assert.True(screen.ShowAtStartup);
        screen.ShowAtStartup = false;
        Assert.False(AppSettings.Load(rig.Settings.Directory).ShowStartScreen);
        screen.ShowAtStartup = true;
        Assert.True(AppSettings.Load(rig.Settings.Directory).ShowStartScreen);
    }

    [Fact]
    public void OldSettingsWithoutTheField_LoadWithShowStartScreenTrue()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pixed-vm-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "settings.json"), "{\"Theme\":\"Light\",\"DefaultWidth\":48}");
            var s = AppSettings.Load(dir);
            Assert.True(s.ShowStartScreen);
            Assert.Equal("Light", s.Theme);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
