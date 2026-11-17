// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using PixelEditor.Core;
using PixelEditor.ViewModels;

namespace PixelEditor.ViewModels.Tests;

public class HsvSyncTests
{
    [Fact]
    public void RgbHexAndPaletteChanges_UpdateHsv()
    {
        using var rig = new Rig();
        var c = rig.Vm.Colors;
        c.Red = 255; c.Green = 0; c.Blue = 0;
        Assert.Equal(0, c.Hue, 3);
        Assert.Equal(1, c.Saturation, 3);
        Assert.Equal(1, c.Value, 3);

        c.PrimaryHex = "#0000FF";
        Assert.Equal(240, c.Hue, 3);
        c.PaletteClick(Pixel.Rgb(0, 255, 0), right: false);
        Assert.Equal(120, c.Hue, 3);
    }

    [Fact]
    public void HsvChanges_UpdateRgbAndHex_AndRaiseNotifications()
    {
        using var rig = new Rig();
        var c = rig.Vm.Colors;
        var changed = new List<string?>();
        c.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        c.Hue = 240; c.Saturation = 1; c.Value = 1;
        Assert.Equal(Pixel.Rgb(0, 0, 255), c.Primary);
        Assert.Equal("#0000FF", c.PrimaryHex);
        Assert.Equal(255, c.Blue);
        Assert.Contains(nameof(ColorsViewModel.Primary), changed);
        Assert.Contains(nameof(ColorsViewModel.Blue), changed);
        Assert.Contains(nameof(ColorsViewModel.PrimaryHex), changed);
    }

    [Fact]
    public void DraggingHsv_DoesNotDriftThroughRgbRounding()
    {
        using var rig = new Rig();
        var c = rig.Vm.Colors;
        c.SetHsv(200.4, 0.333, 0.777);
        Assert.Equal(200.4, c.Hue, 6);
        Assert.Equal(0.333, c.Saturation, 6);
        Assert.Equal(0.777, c.Value, 6);
        // an RGB edit moves HSV to the exact values of that color, with no feedback loop
        c.Red = 10;
        var (h, s, v) = Hsv.FromRgb(c.Primary);
        Assert.Equal((h, s, v), (c.Hue, c.Saturation, c.Value));
    }

    [Fact]
    public void Hue_IsRememberedForGreysAndBlack()
    {
        using var rig = new Rig();
        var c = rig.Vm.Colors;
        c.SetHsv(210, 0.6, 0.8);

        c.SetPrimary(Pixel.Rgb(128, 128, 128));   // a grey: hue stays, saturation is 0
        Assert.Equal(210, c.Hue, 6);
        Assert.Equal(0, c.Saturation, 6);

        c.SetHsv(210, 0.6, 0.8);
        c.SetPrimary(Pixel.Rgb(0, 0, 0));          // black: hue and saturation stay, value is 0
        Assert.Equal(210, c.Hue, 6);
        Assert.Equal(0.6, c.Saturation, 6);
        Assert.Equal(0, c.Value, 6);

        c.Value = 0.8;                             // lifting value brings the old color back
        Assert.Equal(Hsv.ToRgb(210, 0.6, 0.8), c.Primary);
    }

    [Fact]
    public void PickerMode_IsRememberedInSettings_DefaultIsHsv()
    {
        Assert.True(new AppSettings().ColorPickerHsv);
        using var rig = new Rig();
        Assert.True(rig.Vm.Colors.ShowHsv);
        rig.Vm.Colors.ShowRgb = true;
        Assert.False(rig.Vm.Colors.ShowHsv);

        var next = new MainViewModel(rig.Services, AppSettings.Load(Path.Combine(rig.Dir, "settings")));
        Assert.False(next.Colors.ShowHsv);
        Assert.True(next.Colors.ShowRgb);
    }
}

public class ShadesTests
{
    [Fact]
    public void Shades_AreAddedNextToEachOtherAndReportedAndMarkDirty()
    {
        using var rig = new Rig();
        var c = rig.Vm.Colors;
        c.SetPrimary(Pixel.Rgb(200, 60, 60));
        int before = c.Palette.Count;
        rig.S.Dirty = false;

        c.ShadesCommand.Execute(null);

        var ramp = PaletteTools.ShadeRamp(c.Primary);
        int expected = ramp.Count(x => !c.Palette.Take(before).Contains(x));
        Assert.Equal(before + expected, c.Palette.Count);
        var added = c.Palette.Skip(before).ToList();
        Assert.Equal(ramp.Where(x => added.Contains(x)).ToList(), added);   // in ramp order, contiguous at the end
        Assert.True(rig.S.Dirty);
        Assert.Contains(expected.ToString(), rig.S.StatusText);
        Assert.Equal(c.Palette.ToList(), rig.Doc.Palette);
    }

    [Fact]
    public void Shades_SkipColorsThatAreAlreadyThere_AndRepeatAddsNothing()
    {
        using var rig = new Rig();
        var c = rig.Vm.Colors;
        c.SetPrimary(Pixel.Rgb(200, 60, 60));
        c.ShadesCommand.Execute(null);
        int n = c.Palette.Count;
        c.ShadesCommand.Execute(null);
        Assert.Equal(n, c.Palette.Count);
        Assert.Contains("0", rig.S.StatusText);
    }

    [Fact]
    public void Shades_AreSavedIntoTheOwnLibraryPaletteLikeAnyPaletteEdit()
    {
        using var rig = new Rig();
        var c = rig.Vm.Colors;
        var info = c.Library.Save("shade-test", "", [Pixel.Rgb(1, 2, 3)]);
        c.ApplyPalette(info);
        c.SetPrimary(Pixel.Rgb(60, 160, 80));
        c.ShadesCommand.Execute(null);
        var saved = c.Library.Find(info.Id)!;
        Assert.Equal(c.Palette.ToList(), saved.Colors.ToList());
        Assert.False(c.IsPaletteModified);
    }

    [Fact]
    public void Shades_OfTheTransparentColor_AddNothing()
    {
        using var rig = new Rig();
        var c = rig.Vm.Colors;
        c.SetPrimary(Pixel.Empty);
        int n = c.Palette.Count;
        c.ShadesCommand.Execute(null);
        Assert.Equal(n, c.Palette.Count);
    }
}

public class RecentColorsTests
{
    private static readonly uint A = Pixel.Rgb(10, 0, 0), B = Pixel.Rgb(0, 10, 0), C = Pixel.Rgb(0, 0, 10);

    [Fact]
    public void SelectingColors_DoesNotCount()
    {
        using var rig = new Rig();
        rig.Vm.Colors.SetPrimary(A);
        rig.Vm.Colors.SetSecondary(B);
        rig.Vm.Colors.PaletteClick(C, false);
        Assert.Empty(rig.Vm.Colors.Recent);
    }

    [Fact]
    public void Pencil_RecordsTheColorsItPaintedWith_MostRecentFirstWithoutDuplicates()
    {
        using var rig = new Rig();
        var c = rig.Vm.Colors;
        rig.Vm.Canvas.Tool = Tool.Pencil;
        c.SetPrimary(A); c.SetSecondary(B);
        rig.Stroke(1, 1, 3, 1);
        rig.Stroke(1, 3, 3, 3, right: true);
        Assert.Equal([B, A], c.Recent.ToList());
        rig.Stroke(1, 5, 3, 5);             // A again: moves to the front, no duplicate
        Assert.Equal([A, B], c.Recent.ToList());
    }

    [Fact]
    public void Fill_Shapes_Gradient_AndCommittedText_AreRecorded()
    {
        using var rig = new Rig();
        var c = rig.Vm.Colors;
        c.SetPrimary(A); c.SetSecondary(B);

        rig.Vm.Canvas.Tool = Tool.Fill;
        rig.Stroke(0, 0, 0, 0);
        Assert.Equal([A], c.Recent.ToList());

        c.SetPrimary(C);
        rig.Vm.Canvas.Tool = Tool.Rect;
        rig.Stroke(2, 2, 6, 6);
        Assert.Equal(C, c.Recent[0]);

        var d = Pixel.Rgb(9, 9, 0);
        c.SetPrimary(d);
        rig.Vm.Canvas.Tool = Tool.Line;
        rig.Stroke(0, 10, 8, 10);
        Assert.Equal(d, c.Recent[0]);

        var e = Pixel.Rgb(0, 9, 9);
        c.SetPrimary(e);
        rig.Vm.Canvas.Tool = Tool.Text;
        rig.Vm.Canvas.PointerDown(1, 12, false, false);
        rig.Vm.Canvas.PointerUp();
        rig.Vm.Canvas.TextInsert("A");
        Assert.DoesNotContain(e, c.Recent);   // still a preview: not painted yet
        rig.Vm.Canvas.TextCommit();
        Assert.Equal(e, c.Recent[0]);
    }

    [Fact]
    public void Eraser_AndEmptyColor_AreNeverRecorded_AndTheRowIsLimitedToEight()
    {
        using var rig = new Rig();
        var c = rig.Vm.Colors;
        rig.Vm.Canvas.Tool = Tool.Pencil;
        c.SetPrimary(A);
        rig.Stroke(1, 1, 3, 1);
        rig.Vm.Canvas.Tool = Tool.Eraser;
        rig.Stroke(1, 1, 3, 1);
        Assert.Equal([A], c.Recent.ToList());

        c.NoteUsed(Pixel.Empty);
        Assert.Equal(1, c.Recent.Count);

        for (int i = 1; i <= 12; i++) c.NoteUsed(Pixel.Rgb((byte)i, 50, 50));
        Assert.Equal(ColorsViewModel.RecentLimit, c.Recent.Count);
        Assert.Equal(Pixel.Rgb(12, 50, 50), c.Recent[0]);
        Assert.DoesNotContain(A, c.Recent);
    }

    [Fact]
    public void ClickingARecentColor_UsesTheSameRulesAsThePalette()
    {
        using var rig = new Rig();
        var c = rig.Vm.Colors;
        c.NoteUsed(A);
        c.PaletteClick(c.Recent[0], right: true);
        Assert.Equal(A, c.Secondary);
        c.PaletteClick(c.Recent[0], right: false);
        Assert.Equal(A, c.Primary);
        Assert.Equal([A], c.Recent.ToList());   // picking from the row does not reorder it
    }
}
