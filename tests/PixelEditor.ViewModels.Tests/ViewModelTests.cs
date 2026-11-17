// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using PixelEditor.Core;
using PixelEditor.ViewModels;
using PixelEditor.Core.Ops;

namespace PixelEditor.ViewModels.Tests;

public class CanvasViewModelTests
{
    private static readonly uint Red = Rig.Red;
    private static readonly uint Blue = Rig.Blue;

    [Fact]
    public void PencilStroke_IsOneUndoStep()
    {
        using var rig = new Rig();
        rig.Vm.Colors.SetPrimary(Red);
        rig.Stroke(1, 1, 4, 1);
        Assert.Equal(Red, rig.Layer.Get(1, 1));
        Assert.Equal(Red, rig.Layer.Get(4, 1));
        Assert.True(rig.S.Dirty);

        rig.Vm.UndoCommand.Execute(null);
        Assert.Equal(0u, rig.Layer.Get(1, 1));
        Assert.Equal(0u, rig.Layer.Get(4, 1));
        Assert.False(rig.Vm.Canvas.HasFloat);
    }

    [Fact]
    public void RightButton_DrawsSecondaryColor_WhichIsTransparentByDefault()
    {
        using var rig = new Rig();
        rig.Layer.Pixels[2 * 32 + 2] = Red;
        rig.Stroke(2, 2, 2, 2, right: true);
        Assert.Equal(0u, rig.Layer.Get(2, 2));

        rig.Vm.Colors.SetSecondary(Blue);
        rig.Stroke(5, 5, 5, 5, right: true);
        Assert.Equal(Blue, rig.Layer.Get(5, 5));
    }

    [Fact]
    public void EraserAndFill_Work()
    {
        using var rig = new Rig();
        rig.Vm.Colors.SetPrimary(Red);
        rig.Vm.Canvas.Tool = Tool.Fill;
        rig.Stroke(0, 0, 0, 0);
        Assert.Equal(1024, rig.Layer.Pixels.Count(p => p == Red));

        rig.Vm.Canvas.Tool = Tool.Eraser;
        rig.Stroke(3, 3, 3, 3);
        Assert.Equal(0u, rig.Layer.Get(3, 3));
    }

    [Fact]
    public void ShapeTools_PreviewRevertsBeforeCommit()
    {
        using var rig = new Rig();
        rig.Vm.Colors.SetPrimary(Red);
        rig.Vm.Canvas.Tool = Tool.Rect;
        rig.Vm.Canvas.FilledShapes = false;
        rig.Vm.Canvas.PointerDown(2, 2, false, false);
        rig.Vm.Canvas.PointerMove(10, 10);
        rig.Vm.Canvas.PointerMove(5, 5); // the preview is redrawn, not accumulated
        rig.Vm.Canvas.PointerUp();
        Assert.Equal(Red, rig.Layer.Get(5, 5));
        Assert.Equal(0u, rig.Layer.Get(10, 10));
        Assert.Equal(0u, rig.Layer.Get(3, 3)); // an outline without a fill
    }

    [Fact]
    public void LockedOrHiddenLayer_BlocksDrawing()
    {
        using var rig = new Rig();
        rig.Layer.Locked = true;
        rig.Stroke(1, 1, 3, 1);
        Assert.Equal(0u, rig.Layer.Get(1, 1));
        Assert.Contains("заблокирован", rig.S.StatusText);

        rig.Layer.Locked = false;
        rig.Layer.Visible = false;
        rig.Stroke(1, 1, 3, 1);
        Assert.Equal(0u, rig.Layer.Get(1, 1));
        Assert.Contains("скрыт", rig.S.StatusText);
    }

    [Fact]
    public void Eyedropper_PicksIntoPrimaryOrSecondary()
    {
        using var rig = new Rig();
        rig.Layer.Pixels[1 * 32 + 1] = Blue;
        rig.Vm.Canvas.Tool = Tool.Picker;
        Assert.False(rig.Vm.Canvas.PointerDown(1, 1, right: false, alt: false)); // no mouse capture is needed
        Assert.Equal(Blue, rig.Vm.Colors.Primary);

        rig.Vm.Canvas.Tool = Tool.Pencil;
        rig.Vm.Canvas.PointerDown(1, 1, right: true, alt: true); // Alt+click from any tool
        Assert.Equal(Blue, rig.Vm.Colors.Secondary);
    }

    [Fact]
    public void Marquee_Select_Move_Commit_Undo()
    {
        using var rig = new Rig();
        rig.Layer.Pixels[1 * 32 + 1] = Red;
        rig.Layer.Pixels[1 * 32 + 2] = Blue;
        var canvas = rig.Vm.Canvas;
        canvas.Tool = Tool.Select;

        Assert.True(canvas.PointerDown(1, 1, false, false)); // the frame
        canvas.PointerMove(2, 1);
        canvas.PointerUp();
        Assert.Equal(new PixelRect(1, 1, 2, 1), canvas.SelectionBounds);
        Assert.True(canvas.CopyCommand.CanExecute(null));

        Assert.True(canvas.PointerDown(1, 1, false, false)); // drag the selected area
        Assert.True(canvas.HasFloat);
        canvas.PointerMove(4, 1);
        canvas.PointerUp();
        Assert.Equal(Red, rig.Layer.Get(4, 1));
        Assert.Equal(0u, rig.Layer.Get(1, 1));
        Assert.Equal(new PixelRect(4, 1, 2, 1), canvas.SelectionBounds);

        canvas.CommitFloat();
        Assert.False(canvas.HasFloat);
        rig.Vm.UndoCommand.Execute(null);
        Assert.Equal(Red, rig.Layer.Get(1, 1));
        Assert.Equal(0u, rig.Layer.Get(4, 1));
    }

    [Fact]
    public void ClickWithoutDrag_ClearsSelection()
    {
        using var rig = new Rig();
        var canvas = rig.Vm.Canvas;
        canvas.Tool = Tool.Select;
        canvas.PointerDown(2, 2, false, false);
        canvas.PointerUp();
        Assert.Null(canvas.SelectionBounds);
    }

    [Fact]
    public void Undo_WhileFloating_CancelsTheMoveFirst()
    {
        using var rig = new Rig();
        rig.Layer.Pixels[5 * 32 + 5] = Red;
        rig.Vm.Canvas.SelectAllCommand.Execute(null);
        Assert.True(rig.Vm.Canvas.Nudge(2, 0, big: false));
        Assert.Equal(Red, rig.Layer.Get(7, 5));
        Assert.True(rig.Vm.UndoCommand.CanExecute(null));

        rig.Vm.UndoCommand.Execute(null);
        Assert.False(rig.Vm.Canvas.HasFloat);
        Assert.Equal(Red, rig.Layer.Get(5, 5));
        Assert.False(rig.Doc.History.CanUndo); // the move itself did not get into history
    }

    [Fact]
    public void Nudge_WithoutSelection_ReturnsFalse_AndBigStepIsEight()
    {
        using var rig = new Rig();
        Assert.False(rig.Vm.Canvas.Nudge(1, 0, false));
        rig.Layer.Pixels[0] = Red;
        rig.Vm.Canvas.SelectAllCommand.Execute(null);
        rig.Vm.Canvas.Nudge(1, 0, big: true);
        Assert.Equal(Red, rig.Layer.Get(8, 0));
    }

    [Fact]
    public void CopyPaste_UsesOwnCopyToKeepTransparency()
    {
        using var rig = new Rig();
        rig.Layer.Pixels[3 * 32 + 3] = Red;
        rig.Vm.Canvas.SelectAllCommand.Execute(null);
        rig.Vm.Canvas.CopyCommand.Execute(null);
        Assert.Equal("Скопировано 32×32", rig.S.StatusText);

        rig.Vm.Canvas.DeselectCommand.Execute(null);
        rig.Vm.Canvas.PasteOrigin = () => (2, 2);
        rig.Vm.Canvas.PasteCommand.Execute(null);
        Assert.True(rig.Vm.Canvas.HasFloat);
        Assert.Equal(Tool.Select, rig.Vm.Canvas.Tool);
        // the source pixel (3,3) moved by (5,5), the empty pixels of the paste erased nothing
        Assert.Equal(Red, rig.Layer.Get(5, 5));
        Assert.Equal(Red, rig.Layer.Get(3, 3));
    }

    [Fact]
    public void Paste_FromExternalClipboard_AndEmptyClipboard()
    {
        using var rig = new Rig();
        rig.Vm.Canvas.PasteCommand.Execute(null);
        Assert.Contains("нет изображения", rig.S.StatusText);

        rig.Clipboard.SetExternal(new PixelImage(2, 1, [Red, Blue]));
        rig.Vm.Canvas.PasteCommand.Execute(null);
        Assert.True(rig.Vm.Canvas.HasFloat);
        Assert.Equal(Blue, rig.Layer.Get(1, 0));
    }

    [Fact]
    public void Cut_And_Delete_ClearRegionWithUndo()
    {
        using var rig = new Rig();
        rig.Layer.Pixels[1 * 32 + 1] = Red;
        rig.Vm.Canvas.SelectAllCommand.Execute(null);
        rig.Vm.Canvas.CutCommand.Execute(null);
        Assert.Equal(0u, rig.Layer.Get(1, 1));
        rig.Vm.UndoCommand.Execute(null);
        Assert.Equal(Red, rig.Layer.Get(1, 1));

        rig.Vm.Canvas.DeleteCommand.Execute(null);
        Assert.Equal(0u, rig.Layer.Get(1, 1));
    }

    [Fact]
    public void ShiftLayer_WrapsOrClips()
    {
        using var rig = new Rig();
        rig.Layer.Pixels[0 * 32 + 31] = Red;
        rig.Vm.Canvas.ShiftLayer(1, 0);
        Assert.Equal(Red, rig.Layer.Get(0, 0));

        rig.Vm.Canvas.WrapShift = false;
        rig.Vm.Canvas.ShiftLayer(-1, 0);
        Assert.Equal(0u, rig.Layer.Get(0, 0)); // went past the edge and disappeared
    }

    [Fact]
    public void CanvasSize_Dialog_Resizes_ResetsSelection_AndRequestsFit()
    {
        using var rig = new Rig();
        rig.Dialogs.CanvasSizeFn = (_, _) => new CanvasSizeResult(48, 40, 0.5, 0.5);
        rig.Vm.Canvas.SelectAllCommand.Execute(null);
        int fits = 0;
        rig.Vm.Canvas.FitRequested += () => fits++;
        int resized = 0;
        rig.S.CanvasResized += () => resized++;

        rig.Vm.Canvas.CanvasSizeCommand.Execute(null);
        Assert.Equal((48, 40), (rig.Doc.Width, rig.Doc.Height));
        Assert.Null(rig.Vm.Canvas.SelectionBounds);
        Assert.Equal(1, resized);
        Assert.True(fits >= 1);

        rig.Vm.UndoCommand.Execute(null);
        Assert.Equal((32, 32), (rig.Doc.Width, rig.Doc.Height));
    }

    [Fact]
    public void Zoom_ClampsAndFits()
    {
        using var rig = new Rig();
        var c = rig.Vm.Canvas;
        for (int i = 0; i < 40; i++) c.ZoomInCommand.Execute(null);
        Assert.Equal(64, c.Zoom);
        for (int i = 0; i < 40; i++) c.ZoomOutCommand.Execute(null);
        Assert.Equal(0.0625, c.Zoom);                 // can zoom out below 100%
        Assert.Equal($"{6.25}%", c.ZoomText);

        c.FitZoom(400, 400);
        Assert.Equal(12, c.Zoom); // 32*12=384 fits, 32*16=512 does not
        Assert.Equal("1200%", c.ZoomText);
        rig.Doc.ResizeCanvas(1200, 900);
        c.FitZoom(600, 450);                           // a big picture fits by going below 100%
        Assert.Equal(0.5, c.Zoom);
    }

    [Fact]
    public void PositionText_ShowsOnlyInsideLayer()
    {
        using var rig = new Rig();
        rig.Vm.Canvas.UpdatePosition(3, 4);
        Assert.Equal("3, 4", rig.Vm.Canvas.PositionText);
        rig.Vm.Canvas.UpdatePosition(-1, 0);
        Assert.Equal("", rig.Vm.Canvas.PositionText);
    }

    [Fact]
    public void SelectToolCommand_AcceptsEnumOrName()
    {
        using var rig = new Rig();
        rig.Vm.Canvas.SelectToolCommand.Execute("Ellipse");
        Assert.Equal(Tool.Ellipse, rig.Vm.Canvas.Tool);
        rig.Vm.Canvas.SelectToolCommand.Execute(Tool.Fill);
        Assert.Equal(Tool.Fill, rig.Vm.Canvas.Tool);
    }

    [Fact]
    public void BrushSize_IsClampedToRange()
    {
        using var rig = new Rig();
        rig.Vm.Canvas.BrushSize = 99;
        Assert.Equal(16, rig.Vm.Canvas.BrushSize);
        rig.Vm.Canvas.BrushSize = -3;
        Assert.Equal(1, rig.Vm.Canvas.BrushSize);
    }
}

public class LayersAndColorsTests
{
    private static readonly uint Red = Rig.Red;
    private static readonly uint Blue = Rig.Blue;

    [Fact]
    public void Layers_AddDuplicateMoveMergeRemove()
    {
        using var rig = new Rig();
        var layers = rig.Vm.Layers;
        Assert.Single(layers.Items);
        Assert.False(layers.RemoveCommand.CanExecute(null)); // the only layer

        layers.AddCommand.Execute(null);
        Assert.Equal(2, layers.Items.Count);
        Assert.Same(rig.Layer, layers.SelectedItem!.Layer); // the new one is active, it is on top
        Assert.True(layers.RemoveCommand.CanExecute(null));

        rig.Layer.Pixels[0] = Red;
        layers.DuplicateCommand.Execute(null);
        Assert.Equal(3, layers.Items.Count);
        Assert.Equal(Red, rig.Layer.Get(0, 0));

        layers.MoveDownCommand.Execute(null);
        Assert.Equal(1, rig.S.ActiveLayerIndex);

        var below = rig.Doc.Layers[0];
        layers.MergeDownCommand.Execute(null);
        Assert.Equal(2, layers.Items.Count);

        layers.RemoveCommand.Execute(null);
        Assert.Single(layers.Items);
        Assert.Contains(rig.Layer, rig.Doc.Layers);
        Assert.NotNull(layers.SelectedItem);
        _ = below;
    }

    [Fact]
    public void Layers_PropertiesAndVisibilityCommandsUndoAndRedo()
    {
        using var rig = new Rig();
        var layers = rig.Vm.Layers;
        layers.AddCommand.Execute(null);
        var top = layers.Items[0];
        var bottom = layers.Items[1];
        var originalName = top.Name;
        rig.S.Dirty = false;

        top.Locked = true;
        Assert.True(rig.S.Dirty);
        Assert.False(layers.MergeDownCommand.CanExecute(null));
        rig.Doc.History.Undo();
        Assert.False(top.Locked);
        Assert.True(layers.MergeDownCommand.CanExecute(null));
        top.Name = "Ink";
        rig.Doc.History.Undo();
        Assert.Equal(originalName, top.Name);
        rig.Doc.History.Redo();
        Assert.Equal("Ink", top.Name);

        layers.ShowOnlyCommand.Execute(null);
        Assert.False(bottom.Visible);
        Assert.False(layers.MergeDownCommand.CanExecute(null));
        layers.ShowAllCommand.Execute(null);
        Assert.True(bottom.Visible);
        rig.Doc.History.Undo();
        Assert.False(bottom.Visible);
        rig.Doc.History.Undo();
        Assert.True(bottom.Visible);
    }

    [Fact]
    public void Layers_SelectingItem_ChangesActiveLayer()
    {
        using var rig = new Rig();
        rig.Vm.Layers.AddCommand.Execute(null);
        var bottom = rig.Vm.Layers.Items[^1];
        rig.Vm.Layers.SelectedItem = bottom;
        Assert.Same(bottom.Layer, rig.S.ActiveLayer);

        rig.Vm.Canvas.Tool = Tool.Pencil;
        rig.Vm.Colors.SetPrimary(Red);
        rig.Stroke(1, 1, 1, 1);
        Assert.Equal(Red, bottom.Layer.Get(1, 1));
    }

    [Fact]
    public void Layers_SelectionFollowsActiveLayerSetElsewhere()
    {
        using var rig = new Rig();
        rig.Vm.Layers.AddCommand.Execute(null);
        rig.S.SetActiveLayer(rig.Doc.Layers[0]);
        Assert.Same(rig.Doc.Layers[0], rig.Vm.Layers.SelectedItem!.Layer);
    }

    [Fact]
    public void LayerItem_VisibilityAffectsFrameAndMarksDirty()
    {
        using var rig = new Rig();
        rig.Layer.Pixels[0] = Red;
        var frame = new uint[32 * 32];
        int renders = 0;
        rig.S.FrameInvalidated += () => renders++;

        rig.Vm.Layers.Items[0].Visible = false;
        rig.S.ComposeFrame(frame);
        Assert.Equal(0u, frame[0]);
        Assert.True(rig.S.Dirty);
        Assert.True(renders >= 1);
    }

    [Fact]
    public void Layers_RenameMarksDirty()
    {
        using var rig = new Rig();
        rig.Vm.Layers.Items[0].Name = "Персонаж";
        Assert.Equal("Персонаж", rig.Layer.Name);
        Assert.True(rig.S.Dirty);
    }

    [Fact]
    public void Colors_RgbSlidersAndHex_StayInSync()
    {
        using var rig = new Rig();
        var colors = rig.Vm.Colors;
        var changed = new List<string>();
        colors.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        colors.Red = 255;
        Assert.Equal(Red, colors.Primary);
        Assert.Equal("#FF0000", colors.PrimaryHex);
        Assert.Contains("PrimaryHex", changed);

        colors.PrimaryHex = "#0000FF";
        Assert.Equal(Blue, colors.Primary);
        Assert.Equal(255, colors.Blue);
        Assert.Equal(0, colors.Red);
    }

    [Fact]
    public void Colors_InvalidHex_RevertsAndNotifies()
    {
        using var rig = new Rig();
        var changed = new List<string>();
        rig.Vm.Colors.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);
        rig.Vm.Colors.PrimaryHex = "zzz";
        Assert.Equal(Pixel.Rgb(0, 0, 0), rig.Vm.Colors.Primary);
        Assert.Contains("PrimaryHex", changed); // the UI will restore the previous text
    }

    [Fact]
    public void Colors_PaletteClick_AndSwap()
    {
        using var rig = new Rig();
        var colors = rig.Vm.Colors;
        colors.PaletteClick(Red, right: false);
        colors.PaletteClick(Blue, right: true);
        Assert.Equal((Red, Blue), (colors.Primary, colors.Secondary));
        colors.SwapColorsCommand.Execute(null);
        Assert.Equal((Blue, Red), (colors.Primary, colors.Secondary));
    }

    [Fact]
    public void Palette_AddRemoveSortAndUnused()
    {
        using var rig = new Rig();
        var colors = rig.Vm.Colors;
        Assert.Equal(16, colors.Palette.Count);

        colors.SetPrimary(Pixel.Rgb(1, 2, 3));
        colors.AddToPaletteCommand.Execute(null);
        colors.AddToPaletteCommand.Execute(null); // a duplicate is not added
        Assert.Equal(17, colors.Palette.Count);

        colors.SelectedPaletteColor = Pixel.Rgb(1, 2, 3);
        colors.RemoveFromPaletteCommand.Execute(null);
        Assert.Equal(16, colors.Palette.Count);

        colors.SortByLuminanceCommand.Execute(null);
        Assert.True(PaletteTools.Luma(colors.Palette[0]) <= PaletteTools.Luma(colors.Palette[^1]));

        rig.Layer.Pixels[0] = colors.Palette[3];
        colors.RemoveUnusedCommand.Execute(null);
        Assert.Single(colors.Palette);
    }

    [Fact]
    public void Palette_SaveAndOpen_UseDialogs()
    {
        using var rig = new Rig();
        var path = Path.Combine(rig.Dir, "p.hex");
        rig.Dialogs.SaveFileFn = () => path;
        rig.Vm.Colors.SavePaletteCommand.Execute(null);
        Assert.True(File.Exists(path));

        rig.Vm.Colors.Palette.Clear();
        rig.Dialogs.OpenFileFn = () => path;
        rig.Vm.Colors.OpenPaletteCommand.Execute(null);
        Assert.Equal(16, rig.Vm.Colors.Palette.Count);
    }

    [Fact]
    public void Stats_CountColors_Highlight_AndReplaceWithUndo()
    {
        using var rig = new Rig();
        rig.Layer.Pixels[0] = Red;
        rig.Layer.Pixels[1] = Red;
        rig.Layer.Pixels[2] = Blue;
        rig.Vm.Colors.RefreshStats();
        Assert.Equal(2, rig.Vm.Colors.Stats.Count);
        Assert.Equal(new ColorCount(Red, 2), rig.Vm.Colors.Stats[0]);
        Assert.Contains("2 ", rig.Vm.Colors.StatsHeader);

        rig.Vm.Colors.SelectedStat = rig.Vm.Colors.Stats[0];
        Assert.Equal(Red, rig.S.Highlight);
        var frame = new uint[32 * 32];
        rig.S.ComposeFrame(frame);
        Assert.Equal(Red, frame[0]);          // the highlighted color is not touched
        Assert.NotEqual(Blue, frame[2]);      // the others are darkened
        Assert.NotEqual(0u, frame[2]);

        rig.Vm.Colors.SetPrimary(Pixel.Rgb(0, 255, 0));
        rig.Vm.Colors.ReplaceStatColorCommand.Execute(null);
        Assert.Equal(Pixel.Rgb(0, 255, 0), rig.Layer.Get(0, 0));
        Assert.Null(rig.S.Highlight);

        rig.Vm.UndoCommand.Execute(null);
        Assert.Equal(Red, rig.Layer.Get(0, 0));
    }

    [Fact]
    public void Stats_DeletePixelsOfTheSelectedColor_MakesThemTransparent_AndUndoRestores()
    {
        using var rig = new Rig();
        rig.Layer.Pixels[0] = Red; rig.Layer.Pixels[5] = Red; rig.Layer.Pixels[2] = Blue;
        rig.Vm.Colors.RefreshStats();
        rig.Vm.Colors.DeleteStatColorCommand.Execute(null);
        Assert.Contains("Выберите цвет", rig.S.StatusText);          // the color is not chosen: nothing was erased
        Assert.Equal(Red, rig.Layer.Pixels[0]);

        rig.Vm.Colors.SelectedStat = rig.Vm.Colors.Stats.First(c => c.Color == Red);
        rig.Vm.Colors.DeleteStatColorCommand.Execute(null);
        Assert.Equal(0u, rig.Layer.Pixels[0]);
        Assert.Equal(0u, rig.Layer.Pixels[5]);
        Assert.Equal(Blue, rig.Layer.Pixels[2]);                    // the other colors are in place
        Assert.Contains("2", rig.S.StatusText);

        rig.Vm.UndoCommand.Execute(null);
        Assert.Equal(Red, rig.Layer.Pixels[0]);
    }

    [Fact]
    public void Stats_ReplaceWithoutSelection_ShowsHint_AndClearHighlight()
    {
        using var rig = new Rig();
        rig.Vm.Colors.ReplaceStatColorCommand.Execute(null);
        Assert.Contains("Выберите цвет", rig.S.StatusText);

        rig.Layer.Pixels[0] = Red;
        rig.Vm.Colors.RefreshStats();
        rig.Vm.Colors.SelectedStat = rig.Vm.Colors.Stats[0];
        rig.Vm.Colors.ClearHighlightCommand.Execute(null);
        Assert.Null(rig.S.Highlight);
    }
}

public class AutomationViewModelTests
{
    private static readonly uint Red = Rig.Red;
    private static readonly uint Blue = Rig.Blue;

    private static string WriteJson(Rig rig, string name, string json)
    {
        var path = Path.Combine(rig.Dir, name);
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void Template_AppliesToActiveLayer_AndReportsPassedChecks()
    {
        using var rig = new Rig();
        rig.Layer.Pixels[0] = Red;
        var path = WriteJson(rig, "t.json", """{"name":"flip","steps":[{"op":"flipH"}],"checks":[{"check":"maxColors","value":5}]}""");
        rig.Vm.Automation.RunTemplateCommand.Execute(path);
        Assert.Equal(Red, rig.Layer.Get(31, 0));
        Assert.Contains("Проверки пройдены", rig.S.StatusText);
        Assert.Empty(rig.Dialogs.Warnings);
    }

    [Fact]
    public void Template_FailedRequiredCheck_WarnsButKeepsResult()
    {
        using var rig = new Rig();
        rig.Layer.Pixels[0] = Red;
        rig.Layer.Pixels[1] = Blue;
        var path = WriteJson(rig, "t.json", """{"steps":[{"op":"flipH"}],"checks":[{"check":"maxColors","value":1}]}""");
        rig.Vm.Automation.RunTemplateCommand.Execute(path);
        Assert.Equal(Red, rig.Layer.Get(31, 0));
        Assert.Single(rig.Dialogs.Warnings);
        Assert.Contains("НЕ ПРОЙДЕНО", rig.Dialogs.Warnings[0]);
    }

    [Fact]
    public void Template_OptionalCheckFailure_IsInfoNotWarning()
    {
        using var rig = new Rig();
        rig.Layer.Pixels[0] = Red;
        rig.Layer.Pixels[1] = Blue;
        var path = WriteJson(rig, "t.json", """{"steps":[],"checks":[{"check":"maxColors","value":1,"required":false}]}""");
        rig.Vm.Automation.RunTemplateCommand.Execute(path);
        Assert.Single(rig.Dialogs.Infos);
        Assert.Empty(rig.Dialogs.Warnings);
    }

    [Fact]
    public void Template_StepError_ChangesNothing()
    {
        using var rig = new Rig();
        rig.Layer.Pixels[0] = Red;
        var path = WriteJson(rig, "t.json", """{"steps":[{"op":"flipH"},{"op":"nope"}]}""");
        rig.Vm.Automation.RunTemplateCommand.Execute(path);
        Assert.Equal(Red, rig.Layer.Get(0, 0));
        Assert.Single(rig.Dialogs.Warnings);
        Assert.Contains("шаг 2", rig.Dialogs.Warnings[0]);
    }

    [Fact]
    public void Template_AllLayers_IsOneUndoStep()
    {
        using var rig = new Rig();
        rig.Layer.Pixels[0] = Red;
        rig.Vm.Layers.AddCommand.Execute(null);
        rig.Layer.Pixels[0] = Blue;
        var path = WriteJson(rig, "t.json", """{"steps":[{"op":"flipV"}]}""");

        rig.Vm.Automation.AllLayers = true;
        rig.Vm.Automation.RunTemplateCommand.Execute(path);
        Assert.Equal(Red, rig.Doc.Layers[0].Get(0, 31));
        Assert.Equal(Blue, rig.Doc.Layers[1].Get(0, 31));

        rig.Vm.UndoCommand.Execute(null);
        Assert.Equal(Red, rig.Doc.Layers[0].Get(0, 0));
        Assert.Equal(Blue, rig.Doc.Layers[1].Get(0, 0));
    }

    [Fact]
    public void Template_AllLayers_UsesOnlyUnlockedLayerEvenWhenActiveIsLocked()
    {
        using var rig = new Rig();
        var bottom = rig.Layer;
        bottom.Pixels[0] = Red;
        rig.Vm.Layers.AddCommand.Execute(null);
        var locked = rig.Layer;
        rig.Vm.Layers.SelectedItem!.Locked = true;
        locked.Pixels[0] = Blue;
        var path = WriteJson(rig, "t.json", """[{"op":"flipV"}]""");

        rig.Vm.Automation.AllLayers = true;
        rig.Vm.Automation.RunPipelineFile(path, preview: true);

        Assert.Same(locked, rig.Layer);
        Assert.Equal(Blue, locked.Get(0, 0));
        Assert.Equal(Red, bottom.Get(0, 31));
        rig.Doc.History.Undo();
        Assert.Equal(Red, bottom.Get(0, 0));
    }

    [Fact]
    public void ExportSelectionMaskUsesOriginalCanvasCoordinates()
    {
        using var rig = new Rig();
        const string output = "mask.png";
        rig.Dialogs.SaveFileFn = () => output;
        rig.Vm.Canvas.Tool = Tool.Select;
        rig.Stroke(4, 5, 7, 8);
        rig.Vm.Canvas.ExportSelectionMaskCommand.Execute(null);

        var mask = rig.Images.Files[output];
        Assert.Equal((32, 32), (mask.Width, mask.Height));
        Assert.Equal(0xFFFFFFFFu, mask.Get(4, 5));
        Assert.Equal(0xFFFFFFFFu, mask.Get(7, 8));
        Assert.Equal(0u, mask.Get(3, 5));
        Assert.Equal(0u, mask.Get(7, 9));
    }

    [Fact]
    public void Template_SelectionOnlyPreservesPixelsOutsideSelection()
    {
        using var rig = new Rig();
        rig.Layer.Pixels[0] = Red;
        rig.Layer.Pixels[6] = Red;
        rig.Vm.Canvas.Tool = Tool.Select;
        rig.Stroke(0, 0, 2, 2);
        var path = WriteJson(rig, "t.json", """[{"op":"replaceColor","from":"#F00","to":"#00F"}]""");
        rig.Vm.Automation.SelectionOnly = true;

        rig.Vm.Automation.RunPipelineFile(path);

        Assert.Equal(Blue, rig.Layer.Get(0, 0));
        Assert.Equal(Red, rig.Layer.Get(6, 0));
        rig.Doc.History.Undo();
        Assert.Equal(Red, rig.Layer.Get(0, 0));
    }

    [Fact]
    public void Template_AllLayers_RefusesSizeChange()
    {
        using var rig = new Rig();
        rig.Vm.Layers.AddCommand.Execute(null);
        var path = WriteJson(rig, "t.json", """{"steps":[{"op":"resize","width":8}]}""");
        rig.Vm.Automation.AllLayers = true;
        rig.Vm.Automation.RunTemplateCommand.Execute(path);
        Assert.Single(rig.Dialogs.Warnings);
        Assert.Equal(32, rig.Doc.Width);
    }

    [Fact]
    public void Template_SizeChange_OffersNewDocument()
    {
        using var rig = new Rig();
        rig.Layer.Pixels[0] = Red;
        var path = WriteJson(rig, "t.json", """{"steps":[{"op":"resize","width":16,"method":"nearest"}]}""");
        rig.Vm.Automation.RunTemplateCommand.Execute(path);
        Assert.Single(rig.Dialogs.Confirms);
        Assert.Equal(16, rig.Doc.Width);
    }

    [Fact]
    public void Operation_ByMenuSpec_RunsOnLayer()
    {
        using var rig = new Rig();
        rig.Layer.Pixels[5 * 32 + 5] = Red; // a single junk pixel
        rig.Vm.Automation.ApplyOperationCommand.Execute("""removeIslands|{"maxSize":1}""");
        Assert.Equal(0u, rig.Layer.Get(5, 5));
        Assert.Contains("Изменено пикселей: 1", rig.S.StatusText);
    }

    [Fact]
    public void Operation_OnLockedLayer_IsRefused()
    {
        using var rig = new Rig();
        rig.Layer.Pixels[0] = Red;
        rig.Layer.Locked = true;
        rig.Vm.Automation.ApplyOperationCommand.Execute("flipH|{}");
        Assert.Equal(Red, rig.Layer.Get(0, 0));
    }

    [Fact]
    public void Templates_ListSystemFirstThenUser_AndBrokenFilesAreSkipped()
    {
        using var rig = new Rig();
        var dir = rig.Settings.Templates.UserDirectory;
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "a.json"), """{"name":"Alpha","steps":[{"op":"trim"}]}""");
        File.WriteAllText(Path.Combine(dir, "broken.json"), "not json");

        var vm = new MainViewModel(rig.Services, rig.Settings);
        var templates = vm.Automation.Templates;
        Assert.Equal(BuiltInTemplates.All.Count + 1, templates.Count);
        Assert.All(templates.Take(BuiltInTemplates.All.Count), t => Assert.True(t.IsSystem));
        Assert.Contains(templates, t => t is { Name: "Alpha", IsSystem: false });
        Assert.Equal(BuiltInTemplates.All.Count, vm.Automation.SystemTemplateMenu.Count);
        Assert.Single(vm.Automation.UserTemplateMenu);
    }

    [Fact]
    public void BatchAndReferenceCommands_OpenWindows()
    {
        using var rig = new Rig();
        rig.Vm.Automation.BatchCommand.Execute(null);
        rig.Vm.Automation.ReferenceCommand.Execute(null);
        Assert.Equal((1, 1), (rig.Dialogs.BatchShown, rig.Dialogs.ReferenceShown));
    }
}

public class SheetViewModelTests
{
    private static readonly uint Red = Rig.Red;

    [Fact]
    public void SheetGrid_SetAndDisable()
    {
        using var rig = new Rig();
        int overlays = 0;
        rig.S.OverlaysChanged += () => overlays++;

        rig.Dialogs.PromptFn = (_, _, _) => "16x16";
        rig.Vm.Sheet.SheetGridCommand.Execute(null);
        Assert.Equal((16, 16), (rig.Doc.SheetCellWidth, rig.Doc.SheetCellHeight));
        Assert.True(rig.S.Dirty);
        Assert.Equal(1, overlays);

        rig.Dialogs.PromptFn = (_, _, _) => "0";
        rig.Vm.Sheet.SheetGridCommand.Execute(null);
        Assert.False(rig.Doc.HasSheetGrid);

        rig.Dialogs.PromptFn = (_, _, _) => "64x64";
        rig.Vm.Sheet.SheetGridCommand.Execute(null);
        Assert.Contains("больше холста", rig.S.StatusText);

        rig.Dialogs.PromptFn = (_, _, _) => "abc";
        rig.Vm.Sheet.SheetGridCommand.Execute(null);
        Assert.Contains("Не понял", rig.S.StatusText);
    }

    [Fact]
    public void Animation_UsesLayersOrGridCells()
    {
        using var rig = new Rig();
        rig.Vm.Layers.AddCommand.Execute(null);
        rig.Vm.Sheet.AnimationCommand.Execute(null);
        Assert.Equal(2, rig.Dialogs.Animations[0].Count);

        rig.Doc.SheetCellWidth = rig.Doc.SheetCellHeight = 16;
        rig.Layer.Pixels[0] = Red;
        rig.Vm.Sheet.AnimationCommand.Execute(null);
        Assert.Single(rig.Dialogs.Animations[1]); // one cell with content, the tail of empty ones dropped
        Assert.Equal(16, rig.Dialogs.Animations[1][0].Width);
    }

    [Fact]
    public void ImportSheet_MakesLayerPerCell()
    {
        using var rig = new Rig();
        var sheet = new PixelImage(4, 2);
        sheet.Set(0, 0, Red);
        sheet.Set(2, 1, Red);
        rig.Images.Files["sheet.png"] = sheet;
        rig.Dialogs.OpenFileFn = () => "sheet.png";
        rig.Dialogs.PromptFn = (_, _, _) => "2x2";

        rig.Vm.Sheet.ImportSheetCommand.Execute(null);
        Assert.Equal((2, 2), (rig.Doc.Width, rig.Doc.Height));
        Assert.Equal(2, rig.Doc.Layers.Count);
        Assert.Equal("Кадр 1", rig.Doc.Layers[0].Name);
        Assert.Equal(2, rig.Vm.Layers.Items.Count);
        Assert.False(rig.S.Dirty);
    }

    [Fact]
    public void ExportSlices_SavesNonEmptyCells_AndNeedsGrid()
    {
        using var rig = new Rig();
        rig.Dialogs.FolderFn = () => "out";
        rig.Vm.Sheet.ExportSlicesCommand.Execute(null);
        Assert.Contains("Сначала задайте сетку", rig.S.StatusText);

        rig.Doc.SheetCellWidth = rig.Doc.SheetCellHeight = 16;
        rig.Layer.Pixels[0] = Red;
        rig.Vm.Sheet.ExportSlicesCommand.Execute(null);
        Assert.Single(rig.Images.Files);
        Assert.Contains(rig.Images.Files.Keys, k => k.EndsWith("cell_00_00.png"));
    }

    [Fact]
    public void ExportLayersSheet_ComposesLayers()
    {
        using var rig = new Rig();
        rig.Vm.Layers.AddCommand.Execute(null);
        rig.Dialogs.PromptFn = (_, _, _) => "2";
        rig.Dialogs.SaveFileFn = () => "sheet.png";
        rig.Vm.Sheet.ExportLayersSheetCommand.Execute(null);
        Assert.Equal(64, rig.Images.Files["sheet.png"].Width);
        Assert.Equal(32, rig.Images.Files["sheet.png"].Height);
    }
}

public class MainViewModelTests
{
    private static readonly uint Red = Rig.Red;

    [Fact]
    public void Status_RaisesPropertyChanged_SoTheStatusBarUpdatesLive()
    {
        using var rig = new Rig();
        var changed = new List<string>();
        rig.S.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);
        rig.Vm.Canvas.Tool = Tool.Select;
        rig.Vm.Canvas.PointerDown(2, 2, false, false);
        rig.Vm.Canvas.PointerMove(12, 10);
        rig.Vm.Canvas.PointerUp();
        Assert.Contains("StatusText", changed);
        Assert.Equal("Выделено 11×9 (2, 2)", rig.S.StatusText);
    }

    [Fact]
    public void Title_ReflectsNameDirtyAndSize()
    {
        using var rig = new Rig();
        Assert.Equal("Без имени", rig.Vm.DocumentName);
        rig.Vm.Colors.SetPrimary(Red);
        rig.Stroke(0, 0, 1, 0);
        Assert.Equal("Без имени •", rig.Vm.DocumentName); // a dot = there are unsaved edits
    }

    [Fact]
    public void Save_WithoutPath_AsksForOne_ThenClearsDirty_AndRemembersRecent()
    {
        using var rig = new Rig();
        var path = Path.Combine(rig.Dir, "a.pixproj");
        rig.Dialogs.SaveFileFn = () => path;
        rig.Stroke(0, 0, 1, 0);
        Assert.True(rig.S.Dirty);

        rig.Vm.SaveCommand.Execute(null);
        Assert.False(rig.S.Dirty);
        Assert.True(File.Exists(path));
        Assert.Equal(path, rig.S.ProjectPath);
        Assert.Single(rig.Vm.RecentFiles);
        Assert.Equal("a.pixproj", rig.Vm.DocumentName); // after saving there is no dot
    }

    [Fact]
    public void Save_Cancelled_KeepsDirty()
    {
        using var rig = new Rig();
        rig.Stroke(0, 0, 1, 0);
        rig.Dialogs.SaveFileFn = () => null;
        Assert.False(rig.Vm.SaveProject(false));
        Assert.True(rig.S.Dirty);
    }

    [Fact]
    public void Open_LoadsProject_AndRecentOpensAgain()
    {
        using var rig = new Rig();
        var path = Path.Combine(rig.Dir, "b.pixproj");
        var doc = new Document(8, 8);
        doc.Layers[0].Pixels[3] = Red;
        ProjectIO.Save(doc, path);

        rig.Dialogs.OpenFileFn = () => path;
        rig.Vm.OpenCommand.Execute(null);
        Assert.Equal((8, 8), (rig.Doc.Width, rig.Doc.Height));
        Assert.Equal(Red, rig.Layer.Pixels[3]);
        Assert.False(rig.S.Dirty);

        rig.Vm.NewCommand.Execute(null); // NewDocFn returned null: the document does not change
        Assert.Equal(8, rig.Doc.Width);

        rig.Dialogs.NewDocFn = (_, _) => new NewDocumentResult(5, 6);
        rig.Vm.NewCommand.Execute(null);
        Assert.Equal((5, 6), (rig.Doc.Width, rig.Doc.Height));

        rig.Vm.OpenRecentCommand.Execute(rig.Vm.RecentFiles[0]);
        Assert.Equal(8, rig.Doc.Width);
    }

    [Fact]
    public void Open_BrokenFile_ShowsError_AndKeepsDocument()
    {
        using var rig = new Rig();
        var path = Path.Combine(rig.Dir, "broken.pixproj");
        File.WriteAllText(path, "not a zip");
        rig.Vm.OpenProjectFile(path);
        Assert.Single(rig.Dialogs.Errors);
        Assert.Equal(32, rig.Doc.Width);
    }

    [Fact]
    public void ConfirmDiscard_AsksWhenDirty_AndHonoursChoice()
    {
        using var rig = new Rig();
        rig.Dialogs.NewDocFn = (_, _) => new NewDocumentResult(4, 4);

        rig.Vm.NewCommand.Execute(null); // clean: there is no question
        Assert.Equal(0, rig.Dialogs.SaveQuestions);
        Assert.Equal(4, rig.Doc.Width);

        rig.Stroke(0, 0, 1, 0);
        rig.Dialogs.SaveChoiceFn = () => SaveChoice.Cancel;
        rig.Vm.NewCommand.Execute(null);
        Assert.Equal(1, rig.Dialogs.SaveQuestions);
        Assert.Equal(4, rig.Doc.Width);
        Assert.True(rig.S.Dirty);

        rig.Dialogs.SaveChoiceFn = () => SaveChoice.Discard;
        rig.Dialogs.NewDocFn = (_, _) => new NewDocumentResult(9, 9);
        rig.Vm.NewCommand.Execute(null);
        Assert.Equal(9, rig.Doc.Width);

        rig.Stroke(0, 0, 1, 0);
        var path = Path.Combine(rig.Dir, "c.pixproj");
        rig.Dialogs.SaveFileFn = () => path;
        rig.Dialogs.SaveChoiceFn = () => SaveChoice.Save;
        rig.Dialogs.NewDocFn = (_, _) => new NewDocumentResult(3, 3);
        rig.Vm.NewCommand.Execute(null);
        Assert.True(File.Exists(path));
        Assert.Equal(3, rig.Doc.Width);
    }

    [Fact]
    public void ConfirmDiscard_CommitsFloatingSelectionFirst()
    {
        using var rig = new Rig();
        rig.Layer.Pixels[0] = Red;
        rig.Vm.Canvas.SelectAllCommand.Execute(null);
        rig.Vm.Canvas.Nudge(2, 0, false);
        Assert.True(rig.Vm.Canvas.HasFloat);
        rig.Dialogs.SaveChoiceFn = () => SaveChoice.Cancel;
        Assert.False(rig.Vm.ConfirmDiscard());
        Assert.False(rig.Vm.Canvas.HasFloat);
        Assert.Equal(Red, rig.Layer.Get(2, 0));
    }

    [Fact]
    public void Autosave_WritesOnlyWhenDirty_AndStartupOffersRestore()
    {
        using var rig = new Rig();
        rig.Vm.Autosave();
        Assert.False(File.Exists(rig.Settings.AutosavePath)); // clean: nothing to save

        rig.Vm.Colors.SetPrimary(Red);
        rig.Stroke(2, 2, 2, 2);
        rig.Vm.Autosave();
        Assert.True(File.Exists(rig.Settings.AutosavePath));

        var rig2Vm = new MainViewModel(rig.Services, rig.Settings);
        rig2Vm.Startup(null);
        Assert.Contains(rig.Dialogs.Confirms, c => c.Contains("автосохранение"));
        Assert.Equal(Red, rig2Vm.Session.Doc.Layers[0].Get(2, 2));
        Assert.True(rig2Vm.Session.Dirty);
    }

    [Fact]
    public void Startup_DeclinedRestore_DeletesAutosave_AndOpensStartupFile()
    {
        using var rig = new Rig();
        rig.Stroke(0, 0, 0, 0);
        rig.Vm.Autosave();
        var proj = Path.Combine(rig.Dir, "s.pixproj");
        ProjectIO.Save(new Document(7, 7), proj);

        rig.Dialogs.ConfirmFn = _ => false;
        rig.Vm.Startup(proj);
        Assert.False(File.Exists(rig.Settings.AutosavePath));
        Assert.Equal(7, rig.Doc.Width);
    }

    [Fact]
    public void TryClose_DeletesAutosave_UnlessCancelled()
    {
        using var rig = new Rig();
        rig.Stroke(0, 0, 0, 0);
        rig.Vm.Autosave();
        Assert.True(File.Exists(rig.Settings.AutosavePath));

        rig.Dialogs.SaveChoiceFn = () => SaveChoice.Cancel;
        Assert.False(rig.Vm.TryClose());
        Assert.True(File.Exists(rig.Settings.AutosavePath));

        rig.Dialogs.SaveChoiceFn = () => SaveChoice.Discard;
        Assert.True(rig.Vm.TryClose());
        Assert.False(File.Exists(rig.Settings.AutosavePath));
    }

    [Fact]
    public void Import_AsNewLayer_AddsClippedLayer_AndMakesItActive()
    {
        using var rig = new Rig();
        var img = new PixelImage(40, 2);
        img.Set(0, 0, Red);
        img.Set(39, 0, Red); // outside the canvas
        rig.Images.Files["pic.png"] = img;
        rig.Dialogs.OpenFileFn = () => "pic.png";
        rig.Dialogs.ImportConfigure = vm => { vm.RemoveIslands = false; vm.SelectedTarget = vm.Targets[1]; };

        rig.Vm.ImportCommand.Execute(null);
        Assert.Equal(2, rig.Doc.Layers.Count);
        Assert.Equal("pic", rig.Layer.Name);
        Assert.Equal(Red, rig.Layer.Get(0, 0));
        Assert.Contains("обрезано", rig.S.StatusText);
        Assert.Same(rig.Layer, rig.Vm.Layers.SelectedItem!.Layer);
    }

    [Fact]
    public void Import_AsNewDocument_ReplacesDocument()
    {
        using var rig = new Rig();
        rig.Images.Files["pic.png"] = TestImages.Solid(6, 4);
        rig.Dialogs.OpenFileFn = () => "pic.png";
        rig.Dialogs.ImportConfigure = _ => { };
        rig.Vm.ImportCommand.Execute(null);
        Assert.Equal((6, 4), (rig.Doc.Width, rig.Doc.Height));
    }

    [Fact]
    public void Import_CancelledDialog_ChangesNothing_AndClipboardImportNeedsImage()
    {
        using var rig = new Rig();
        rig.Vm.ImportClipboardCommand.Execute(null);
        Assert.Contains("нет изображения", rig.S.StatusText);

        rig.Clipboard.SetExternal(new PixelImage(2, 2));
        rig.Vm.ImportClipboardCommand.Execute(null); // the import dialog was cancelled
        Assert.Equal(32, rig.Doc.Width);
    }

    [Fact]
    public void ExportPng_SavesFlattenedImage()
    {
        using var rig = new Rig();
        rig.Layer.Pixels[0] = Red;
        rig.Dialogs.SaveFileFn = () => "out.png";
        rig.Vm.ExportPngCommand.Execute(null);
        Assert.Equal(Red, rig.Images.Files["out.png"].Pixels[0]);
    }

    [Fact]
    public void DroppedFiles_AreRoutedByExtension()
    {
        using var rig = new Rig();
        var proj = Path.Combine(rig.Dir, "d.pixproj");
        ProjectIO.Save(new Document(5, 5), proj);
        rig.Vm.HandleDroppedFile(proj);
        Assert.Equal(5, rig.Doc.Width);

        rig.Images.Files["x.png"] = TestImages.Solid(3, 3);
        rig.Dialogs.ImportConfigure = _ => { };
        rig.Vm.HandleDroppedFile("x.png");
        Assert.Equal(3, rig.Doc.Width);

        var tpl = Path.Combine(rig.Dir, "f.json");
        File.WriteAllText(tpl, """{"steps":[{"op":"flipH"}]}""");
        rig.Vm.HandleDroppedFile(tpl); // runs as a template without errors
        Assert.Empty(rig.Dialogs.Errors);
    }

    [Fact]
    public void UndoRedo_CommandsReflectHistory()
    {
        using var rig = new Rig();
        Assert.False(rig.Vm.UndoCommand.CanExecute(null));
        rig.Stroke(0, 0, 0, 0);
        Assert.True(rig.Vm.UndoCommand.CanExecute(null));
        Assert.False(rig.Vm.RedoCommand.CanExecute(null));

        int raised = 0;
        rig.Vm.UndoCommand.CanExecuteChanged += (_, _) => raised++;
        rig.Vm.UndoCommand.Execute(null);
        Assert.True(raised >= 1);
        Assert.True(rig.Vm.RedoCommand.CanExecute(null));
        rig.Vm.RedoCommand.Execute(null);
        Assert.Equal(Pixel.Rgb(0, 0, 0), rig.Layer.Get(0, 0)); // the repeat returned the black stroke
    }

    [Fact]
    public void Theme_Command_SavesSettings_NotifiesAndAppliesTheme()
    {
        using var rig = new Rig();
        Assert.True(rig.Vm.IsThemeDark); // dark by default, as in the layout
        var changed = new List<string>();
        rig.Vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        rig.Vm.SetThemeCommand.Execute("Light");
        Assert.True(rig.Vm.IsThemeLight);
        Assert.False(rig.Vm.IsThemeDark);
        Assert.Equal(["Light"], rig.Theme.Applied);
        Assert.Contains("IsThemeLight", changed);
        Assert.Equal("Light", AppSettings.Load(Path.Combine(rig.Dir, "settings")).Theme);
    }

    [Fact]
    public void Exit_RaisesCloseRequested()
    {
        using var rig = new Rig();
        bool closed = false;
        rig.Vm.CloseRequested += () => closed = true;
        rig.Vm.ExitCommand.Execute(null);
        Assert.True(closed);
    }

    [Fact]
    public void FrameEvents_FireOnEditsAndNewDocuments()
    {
        using var rig = new Rig();
        int frames = 0, contents = 0, docs = 0;
        rig.S.FrameInvalidated += () => frames++;
        rig.S.ContentChanged += () => contents++;
        rig.S.DocumentChanged += () => docs++;

        rig.Stroke(0, 0, 2, 0);
        Assert.True(frames >= 1 && contents >= 1);

        rig.Dialogs.NewDocFn = (_, _) => new NewDocumentResult(4, 4);
        rig.Dialogs.SaveChoiceFn = () => SaveChoice.Discard;
        rig.Vm.NewCommand.Execute(null);
        Assert.Equal(1, docs);
    }
}

public class SettingsAndHelpersTests
{
    [Theory]
    [InlineData("32x32", true, 32, 32)]
    [InlineData("16×8", true, 16, 8)]
    [InlineData("24 12", true, 24, 12)]
    [InlineData("10", true, 10, 10)]
    [InlineData("0", true, 0, 0)]
    [InlineData("abc", false, 0, 0)]
    [InlineData("-5x3", false, 0, 0)]
    [InlineData("", false, 0, 0)]
    public void SizeParser_Cases(string text, bool ok, int w, int h)
    {
        Assert.Equal(ok, SizeParser.TryParse(text, out var rw, out var rh));
        if (ok) Assert.Equal((w, h), (rw, rh));
    }

    [Fact]
    public void Settings_RecentIsDeduplicatedAndLimited()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pixed-settings-" + Guid.NewGuid().ToString("N"));
        try
        {
            var s = AppSettings.Load(dir);
            for (int i = 0; i < 15; i++) s.AddRecent(Path.Combine(dir, $"f{i}.pixproj"));
            s.AddRecent(Path.Combine(dir, "f14.pixproj"));
            Assert.Equal(10, s.Recent.Count);
            Assert.EndsWith("f14.pixproj", s.Recent[0]);
            Assert.Equal(1, s.Recent.Count(r => r.EndsWith("f14.pixproj")));

            var again = AppSettings.Load(dir); // saved to disk
            Assert.Equal(10, again.Recent.Count);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void Settings_BrokenFile_FallsBackToDefaults()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pixed-settings-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "settings.json"), "{ not json");
            var s = AppSettings.Load(dir);
            Assert.Equal("Dark", s.Theme);
            Assert.Empty(s.Recent);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void RelayCommand_RespectsCanExecute()
    {
        int ran = 0;
        bool allowed = false;
        var cmd = new RelayCommand(() => ran++, () => allowed);
        Assert.False(cmd.CanExecute(null));
        allowed = true;
        Assert.True(cmd.CanExecute(null));
        cmd.Execute(null);
        Assert.Equal(1, ran);
    }
}

public static class TestImages
{
    /// <summary>A picture filled entirely with red: it does not degenerate when empty edges are trimmed.</summary>
    public static PixelImage Solid(int w, int h)
    {
        var img = new PixelImage(w, h);
        for (int i = 0; i < img.Pixels.Length; i++) img.Pixels[i] = Rig.Red;
        return img;
    }
}
