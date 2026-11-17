// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Text.Json;
using PixelEditor.Core;
using PixelEditor.ViewModels;

namespace PixelEditor.ViewModels.Tests;

/// <summary>UI cleanup: color tokens of operation arguments, the cleanup template item, layer renaming, the theme setting.</summary>
public class UiCleanupTests
{
    private static readonly uint Green = Pixel.Rgb(0, 255, 0);

    // ---------------------------------------------------------------- replace color prefill

    private static (string From, string To) OpenReplaceColorTuner(Rig rig)
    {
        string from = "", to = "";
        rig.Dialogs.TunerConfigure = t =>
        {
            from = t.Parameters.First(p => p.Name == "from").Value;
            to = t.Parameters.First(p => p.Name == "to").Value;
        };
        rig.Vm.Automation.TuneOperationCommand.Execute("replaceColor|{\"from\":\"$selectedUsed\",\"to\":\"$primary\"}");
        return (from, to);
    }

    [Fact]
    public void ReplaceColorTuner_FromIsTheSecondaryColor_WhenNothingIsSelected_AndToIsThePrimary()
    {
        using var rig = new Rig();
        rig.Vm.Colors.SetPrimary(Rig.Blue);
        rig.Vm.Colors.SetSecondary(Rig.Red);
        var (from, to) = OpenReplaceColorTuner(rig);
        Assert.Equal("#FF0000", from.ToUpperInvariant());
        Assert.Equal("#0000FF", to.ToUpperInvariant());
    }

    [Fact]
    public void ReplaceColorTuner_FromIsTheColorSelectedInTheUsedColorsList()
    {
        using var rig = new Rig();
        rig.Layer.Pixels[0] = Green;
        rig.Layer.Pixels[1] = Green;
        rig.Layer.Pixels[2] = Rig.Red;
        rig.Vm.Colors.SetPrimary(Rig.Blue);
        rig.Vm.Colors.SetSecondary(Rig.Red);
        rig.Vm.Colors.RefreshStats();
        rig.Vm.Colors.SelectedStat = rig.Vm.Colors.Stats.First(c => c.Color == Green);
        var (from, to) = OpenReplaceColorTuner(rig);
        Assert.Equal("#00FF00", from.ToUpperInvariant());
        Assert.Equal("#0000FF", to.ToUpperInvariant());
    }

    [Fact]
    public void ApplyOperation_ResolvesSecondaryAndSelectedUsedTokens()
    {
        using var rig = new Rig();
        rig.Layer.Pixels[0] = Rig.Red;
        rig.Vm.Colors.SetPrimary(Rig.Blue);
        rig.Vm.Colors.SetSecondary(Rig.Red);
        rig.Vm.Automation.ApplyOperation("replaceColor|{\"from\":\"$secondary\",\"to\":\"$primary\"}");
        Assert.Equal(Rig.Blue, rig.Layer.Get(0, 0));

        rig.Layer.Pixels[1] = Green;
        rig.Vm.Colors.RefreshStats();
        rig.Vm.Colors.SelectedStat = rig.Vm.Colors.Stats.First(c => c.Color == Green);
        rig.Vm.Automation.ApplyOperation("replaceColor|{\"from\":\"$selectedUsed\",\"to\":\"$primary\"}");
        Assert.Equal(Rig.Blue, rig.Layer.Get(1, 0));
    }

    // ---------------------------------------------------------------- hue shift and outline items

    [Fact]
    public void HueShiftItem_OpensTheTunerOnHueShift()
    {
        using var rig = new Rig();
        string? seen = null;
        rig.Dialogs.TunerConfigure = t => seen = t.OperationName;
        rig.Vm.Automation.TuneOperationCommand.Execute("hueShift|{\"degrees\":30}");
        Assert.Equal("hueShift", seen);
    }

    [Fact]
    public void RemoveOutlineItems_RunTheOperationWithDefaultAndDarkArguments()
    {
        using var rig = new Rig();
        // a red 4x4 square with a black one-pixel outline
        for (int y = 4; y < 10; y++)
            for (int x = 4; x < 10; x++)
                rig.Layer.Pixels[y * rig.Layer.Width + x] = x is 4 or 9 || y is 4 or 9 ? Pixel.Rgb(0, 0, 0) : Rig.Red;
        rig.Vm.Automation.ApplyOperationCommand.Execute("removeOutline|{}");
        Assert.True(Pixel.IsEmpty(rig.Layer.Get(4, 4)));
        Assert.Equal(Rig.Red, rig.Layer.Get(5, 5));

        rig.Vm.UndoCommand.Execute(null);
        rig.Vm.Automation.ApplyOperationCommand.Execute("removeOutline|{\"dark\":true}");
        Assert.True(Pixel.IsEmpty(rig.Layer.Get(4, 4)));
    }

    [Fact]
    public void SmallIslandsItem_OpensTheTunerOnRemoveIslands_WithAChoosableSize()
    {
        using var rig = new Rig();
        string? op = null, size = null;
        rig.Dialogs.TunerConfigure = t => { op = t.OperationName; size = t.Parameters.First(p => p.Name == "maxSize").Value; };
        rig.Vm.Automation.TuneOperationCommand.Execute("removeIslands|{\"maxSize\":3}");
        Assert.Equal("removeIslands", op);
        Assert.Equal("3", size);

        rig.Dialogs.TunerConfigure = t => op = t.OperationName;
        rig.Vm.Automation.TuneOperationCommand.Execute("despeckle|{\"maxArea\":3,\"diagonal\":false}");
        Assert.Equal("despeckle", op);
    }

    [Fact]
    public void RemoveDebrisItem_ErasesGroupsUpToThreePixels()
    {
        using var rig = new Rig();
        rig.Layer.Pixels[0] = Rig.Red; rig.Layer.Pixels[1] = Rig.Red; rig.Layer.Pixels[2] = Rig.Red;   // a group of 3
        for (int x = 10; x < 14; x++) rig.Layer.Pixels[10 * rig.Layer.Width + x] = Rig.Red;            // a group of 4 stays
        rig.Vm.Automation.ApplyOperationCommand.Execute("removeIslands|{\"maxSize\":3}");
        Assert.True(Pixel.IsEmpty(rig.Layer.Get(0, 0)));
        Assert.Equal(Rig.Red, rig.Layer.Get(10, 10));
    }

    // ---------------------------------------------------------------- "Clean up sprite"

    [Fact]
    public void CleanSpriteItem_RunsTheBuiltInTemplateThroughThePreview()
    {
        using var rig = new Rig();
        Assert.True(rig.Vm.Automation.HasCleanSpriteTemplate);
        Assert.Contains(rig.Vm.Automation.Templates, t => t.Id == AutomationViewModel.CleanSpriteTemplateId);
        rig.Layer.Pixels[0] = Rig.Red;
        rig.Dialogs.PreviewAccept = false;
        rig.Vm.Automation.CleanSpriteCommand.Execute(null);
        Assert.Single(rig.Dialogs.PreviewsShown);
        Assert.True(Pixel.IsEmpty(rig.Layer.Get(0, 0)) is false);      // cancelled in the preview: nothing applied
    }

    // ---------------------------------------------------------------- layer rename

    [Fact]
    public void Layer_ClickingAndSelectingDoesNotStartEditing()
    {
        using var rig = new Rig();
        rig.Vm.Layers.AddCommand.Execute(null);
        rig.Vm.Layers.SelectedItem = rig.Vm.Layers.Items[1];
        Assert.All(rig.Vm.Layers.Items, i => Assert.False(i.IsEditing));
    }

    [Fact]
    public void Layer_BeginEdit_SelectsTheRow_AndCommitRenamesWithUndo()
    {
        using var rig = new Rig();
        rig.Vm.Layers.AddCommand.Execute(null);
        var bottom = rig.Vm.Layers.Items[1];
        var oldName = bottom.Name;
        bottom.BeginEditCommand.Execute(null);
        Assert.True(bottom.IsEditing);
        Assert.Same(bottom, rig.Vm.Layers.SelectedItem);          // editing a row selects it
        Assert.Equal(oldName, bottom.EditName);

        bottom.EditName = "  Body  ";
        bottom.CommitEditCommand.Execute(null);
        Assert.False(bottom.IsEditing);
        Assert.Equal("Body", rig.Doc.Layers[0].Name);

        rig.Vm.UndoCommand.Execute(null);
        Assert.Equal(oldName, rig.Doc.Layers[0].Name);
        rig.Vm.RedoCommand.Execute(null);
        Assert.Equal("Body", rig.Doc.Layers[0].Name);
    }

    [Fact]
    public void Layer_EmptyNameIsRejected_AndEscapeCancels()
    {
        using var rig = new Rig();
        var item = rig.Vm.Layers.Items[0];
        var oldName = item.Name;
        item.BeginEdit();
        item.EditName = "   ";
        item.CommitEditCommand.Execute(null);
        Assert.Equal(oldName, rig.Doc.Layers[0].Name);
        Assert.False(item.IsEditing);

        item.BeginEdit();
        item.EditName = "Something";
        item.CancelEditCommand.Execute(null);
        Assert.False(item.IsEditing);
        Assert.Equal(oldName, rig.Doc.Layers[0].Name);
        // a cancelled edit leaves nothing to undo besides what was there before
        Assert.False(rig.Doc.History.CanUndo);
    }

    [Fact]
    public void Layer_F2Command_StartsEditingTheSelectedLayer_AndAnotherEditorIsCommitted()
    {
        using var rig = new Rig();
        rig.Vm.Layers.AddCommand.Execute(null);
        var top = rig.Vm.Layers.SelectedItem!;
        rig.Vm.Layers.RenameSelectedCommand.Execute(null);
        Assert.True(top.IsEditing);
        top.EditName = "First";

        var other = rig.Vm.Layers.Items.First(i => !ReferenceEquals(i, top));
        other.BeginEdit();                                         // the second editor commits the first one
        Assert.False(top.IsEditing);
        Assert.True(other.IsEditing);
        Assert.Contains(rig.Doc.Layers, l => l.Name == "First");
    }

    // ---------------------------------------------------------------- theme setting and rotation setting

    [Theory]
    [InlineData("System")]
    [InlineData("")]
    [InlineData("Pink")]
    public void SavedSystemTheme_LoadsAsDark(string saved)
    {
        var dir = Path.Combine(Path.GetTempPath(), "pixed-theme-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "settings.json"), JsonSerializer.Serialize(new { Theme = saved, RotationSmooth = false }));
            var settings = AppSettings.Load(dir);
            Assert.Equal("Dark", settings.Theme);
            Assert.False(settings.RotationSmooth);                  // an old value still reads without error (and is ignored)
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void SavedLightTheme_StaysLight()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pixed-theme-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "settings.json"), """{"Theme":"Light"}""");
            Assert.Equal("Light", AppSettings.Load(dir).Theme);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Rotation_IsAlwaysCrisp_EvenWithAnOldSettingToTheContrary()
    {
        using var rig = new Rig();
        rig.Settings.RotationSmooth = false;
        // a rotation by 45 degrees creates no new colors in the crisp mode
        for (int y = 10; y < 18; y++)
            for (int x = 10; x < 18; x++)
                rig.Layer.Pixels[y * rig.Layer.Width + x] = Rig.Red;
        rig.Vm.Canvas.RotateByCommand.Execute("45");
        var colors = rig.Layer.Pixels.Where(p => !Pixel.IsEmpty(p)).Distinct().ToList();
        Assert.Equal([Rig.Red], colors);
    }
}
