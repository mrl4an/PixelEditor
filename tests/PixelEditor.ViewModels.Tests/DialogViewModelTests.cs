// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using PixelEditor.Core;
using PixelEditor.ViewModels;
using PixelEditor.Core.Ops;

namespace PixelEditor.ViewModels.Tests;

public class SmallDialogTests
{
    [Fact]
    public void Prompt_ReturnsTrimmedTextOrNull()
    {
        var dialogs = new FakeDialogs { PromptFn = (_, _, _) => "  32x32 " };
        Assert.Equal("32x32", dialogs.Prompt("t", "l", "d"));
        dialogs.PromptFn = (_, _, _) => null;
        Assert.Null(dialogs.Prompt("t", "l", "d"));
    }

    [Fact]
    public void NewDocument_ValidatesBeforeClosing()
    {
        var vm = new NewDocumentViewModel(32, 32);
        bool? closed = null;
        vm.CloseRequested += ok => closed = ok;

        vm.WidthText = "abc";
        vm.OkCommand.Execute(null);
        Assert.Null(closed);
        Assert.Contains("от 1 до 4096", vm.Error);

        vm.WidthText = "5000";
        vm.OkCommand.Execute(null);
        Assert.Null(closed);

        vm.WidthText = "64";
        vm.HeightText = "16";
        vm.OkCommand.Execute(null);
        Assert.True(closed);
        Assert.Equal(new NewDocumentResult(64, 16), vm.Result);
    }

    [Fact]
    public void Cancel_ClosesWithFalse()
    {
        var vm = new NewDocumentViewModel(8, 8);
        bool? closed = null;
        vm.CloseRequested += ok => closed = ok;
        vm.CancelCommand.Execute(null);
        Assert.False(closed);
        Assert.Null(vm.Result);
    }

    [Theory]
    [InlineData(AnchorPosition.TopLeft, 0.0, 0.0)]
    [InlineData(AnchorPosition.Top, 0.5, 0.0)]
    [InlineData(AnchorPosition.TopRight, 1.0, 0.0)]
    [InlineData(AnchorPosition.Left, 0.0, 0.5)]
    [InlineData(AnchorPosition.Center, 0.5, 0.5)]
    [InlineData(AnchorPosition.Right, 1.0, 0.5)]
    [InlineData(AnchorPosition.BottomLeft, 0.0, 1.0)]
    [InlineData(AnchorPosition.Bottom, 0.5, 1.0)]
    [InlineData(AnchorPosition.BottomRight, 1.0, 1.0)]
    public void CanvasSize_MapsAnchorToFractions(AnchorPosition anchor, double ax, double ay)
    {
        var vm = new CanvasSizeViewModel(32, 32) { Anchor = anchor, WidthText = "40", HeightText = "20" };
        vm.OkCommand.Execute(null);
        Assert.Equal(new CanvasSizeResult(40, 20, ax, ay), vm.Result);
    }

    [Fact]
    public void CanvasSize_RejectsBadSize()
    {
        var vm = new CanvasSizeViewModel(32, 32) { HeightText = "0" };
        vm.OkCommand.Execute(null);
        Assert.Null(vm.Result);
        Assert.Contains("от 1 до 8192", vm.Error);
    }
}

public class ImportViewModelTests
{
    private static readonly uint Red = Rig.Red;

    private static PixelImage Solid(int w, int h)
    {
        var img = new PixelImage(w, h);
        for (int i = 0; i < img.Pixels.Length; i++) img.Pixels[i] = Red;
        return img;
    }

    private static ImportViewModel Make(PixelImage img, FakeDialogs? dialogs = null, IReadOnlyList<uint>? palette = null, bool canAddLayer = true,
        IUiTimerFactory? timers = null) =>
        new(dialogs ?? new FakeDialogs(), timers, img, "pic.png", palette ?? [], canAddLayer);

    [Fact]
    public void DefaultIsTheOriginalImage_NoResizeTrimOrCleanup()
    {
        var vm = Make(Solid(200, 100));
        Assert.Equal(("200", "100"), (vm.WidthText, vm.HeightText));
        Assert.StartsWith("Результат: 200×100", vm.ResultInfo);
        Assert.False(vm.TrimEmpty);
        Assert.False(vm.RemoveIslands);
        Assert.False(vm.MergeRareColors);
        Assert.Equal("0", vm.MaxColorsText);
        // the pipeline has only transparency binarization: nothing is cropped or shrunk
        Assert.DoesNotContain("trim", vm.PipelineJson);
        Assert.DoesNotContain("resize", vm.PipelineJson);
        Assert.DoesNotContain("quantize", vm.PipelineJson);
        Assert.Equal(200, vm.Preview!.Width);
    }

    [Fact]
    public void ImportWithDefaults_ReturnsThePixelsUntouched()
    {
        var img = new PixelImage(5, 3);
        img.Set(0, 0, Red);
        img.Set(4, 2, Rig.Blue); // single pixels at the edges do not disappear
        var vm = Make(img);
        vm.ImportCommand.Execute(null);
        Assert.Equal(img.Pixels, vm.Result!.Image.Pixels);
    }

    [Fact]
    public void SizePresets_ScaleByLongestSide_AndRestoreTheOriginal()
    {
        var vm = Make(Solid(200, 100));
        vm.SetLongestSideCommand.Execute(32);
        Assert.Equal(("32", "16"), (vm.WidthText, vm.HeightText));
        Assert.StartsWith("Результат: 32×16", vm.ResultInfo);
        Assert.Contains("resize", vm.PipelineJson);

        vm.SetLongestSideCommand.Execute("64"); // a parameter from XAML arrives as a string
        Assert.Equal(("64", "32"), (vm.WidthText, vm.HeightText));

        vm.OriginalSizeCommand.Execute(null);
        Assert.Equal(("200", "100"), (vm.WidthText, vm.HeightText));
        Assert.DoesNotContain("resize", vm.PipelineJson);
    }

    [Fact]
    public void SizePresets_WorkOnTheTrimmedBounds_WhenTrimIsOn()
    {
        var img = new PixelImage(100, 100);
        for (int y = 40; y < 60; y++) for (int x = 10; x < 90; x++) img.Set(x, y, Red); // 80×20
        var vm = Make(img);
        vm.TrimEmpty = true;
        vm.SetLongestSideCommand.Execute(40);
        Assert.Equal(("40", "10"), (vm.WidthText, vm.HeightText));
        vm.OriginalSizeCommand.Execute(null);
        Assert.Equal(("80", "20"), (vm.WidthText, vm.HeightText));
    }

    [Fact]
    public void SmallImages_KeepTheirSize()
    {
        var vm = Make(Solid(20, 10));
        Assert.Equal(("20", "10"), (vm.WidthText, vm.HeightText));
    }

    [Fact]
    public void AspectLock_SyncsHeightAndWidth()
    {
        var vm = Make(Solid(100, 50));
        vm.WidthText = "20";
        Assert.Equal("10", vm.HeightText);
        vm.HeightText = "8";
        Assert.Equal("16", vm.WidthText);

        vm.KeepAspect = false;
        vm.WidthText = "30";
        Assert.Equal("8", vm.HeightText); // without the link the height is not touched
    }

    [Fact]
    public void TrimToggle_RecalculatesHeightFromTrimmedProportions()
    {
        var img = new PixelImage(100, 100);
        for (int y = 40; y < 60; y++) for (int x = 10; x < 90; x++) img.Set(x, y, Red); // 80×20 in the center
        var vm = Make(img);
        vm.TrimEmpty = true;
        vm.WidthText = "40";
        Assert.Equal("10", vm.HeightText); // 80:20

        vm.TrimEmpty = false;
        Assert.Equal("40", vm.HeightText); // 100:100
    }

    [Fact]
    public void PipelineJson_ReflectsOptions()
    {
        var vm = Make(Solid(100, 100), palette: [Red]);
        Assert.DoesNotContain("\"trim\"", vm.PipelineJson);
        Assert.DoesNotContain("outline", vm.PipelineJson);

        vm.TrimEmpty = true;
        vm.WidthText = "50";
        vm.MaxColorsText = "16";
        vm.Recompute();
        Assert.Contains("\"trim\"", vm.PipelineJson);
        Assert.Contains("\"resize\"", vm.PipelineJson);
        Assert.Contains("\"quantize\"", vm.PipelineJson);

        vm.Outline = true;
        vm.Recompute();
        Assert.Contains("outline", vm.PipelineJson);

        vm.UsePalette = true;
        vm.Recompute();
        Assert.Contains("applyPalette", vm.PipelineJson);
        Assert.DoesNotContain("quantize", vm.PipelineJson);

        vm.SelectedMethod = vm.Methods[1];
        vm.Recompute();
        Assert.Contains("\"mode\"", vm.PipelineJson);
    }

    [Fact]
    public void Outline_GrowsTheResult()
    {
        var vm = Make(Solid(10, 10));
        Assert.StartsWith("Результат: 10×10", vm.ResultInfo);
        vm.Outline = true;
        vm.Recompute();
        Assert.StartsWith("Результат: 12×12", vm.ResultInfo);
        Assert.Equal(12, vm.Preview!.Width);
    }

    [Fact]
    public void InvalidSize_DisablesImport_AndShowsMessage()
    {
        var vm = Make(Solid(10, 10));
        Assert.True(vm.ImportCommand.CanExecute(null));
        vm.WidthText = "x";
        vm.Recompute();
        Assert.False(vm.ImportCommand.CanExecute(null));
        Assert.Contains("размер", vm.ResultInfo);
        Assert.Null(vm.Preview);
    }

    [Fact]
    public void UsePalette_IsAvailableOnlyWithAPalette()
    {
        Assert.False(Make(Solid(4, 4)).UsePaletteEnabled);
        Assert.True(Make(Solid(4, 4), palette: [Red]).UsePaletteEnabled);
    }

    [Fact]
    public void NewLayerTarget_ExistsOnlyWhenAllowed()
    {
        Assert.Single(Make(Solid(4, 4), canAddLayer: false).Targets);
        Assert.Equal(2, Make(Solid(4, 4), canAddLayer: true).Targets.Count);
    }

    [Fact]
    public void Import_ReturnsPreviewAndTarget_AndClosesWithTrue()
    {
        var vm = Make(Solid(8, 8));
        bool? closed = null;
        vm.CloseRequested += ok => closed = ok;
        vm.SelectedTarget = vm.Targets[1];
        vm.ImportCommand.Execute(null);
        Assert.True(closed);
        Assert.Equal(ImportTarget.NewLayer, vm.Result!.Target);
        Assert.Equal(8, vm.Result.Image.Width);
    }

    [Fact]
    public void SaveTemplate_WritesPipelineJson()
    {
        var dialogs = new FakeDialogs();
        var path = Path.Combine(Path.GetTempPath(), "pixed-tpl-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            dialogs.SaveFileFn = () => path;
            var vm = Make(Solid(8, 8), dialogs);
            vm.SaveTemplateCommand.Execute(null);
            Assert.Equal(vm.PipelineJson, File.ReadAllText(path));
            // a saved template is a working template
            Assert.True(PixelEditor.Core.Ops.Pipeline.Load(path).Run(Solid(8, 8)).Ok);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Changes_AreDebounced_WhenATimerIsProvided()
    {
        var timers = new FakeTimers();
        var vm = Make(Solid(100, 100), timers: timers); // the original by default
        Assert.Contains("100×100", vm.ResultInfo);

        vm.WidthText = "10";
        vm.WidthText = "12";
        Assert.Contains("100×100", vm.ResultInfo); // there was no recompute yet
        timers.FireAll();
        Assert.Contains("12×12", vm.ResultInfo);
    }

    [Fact]
    public void Import_FlushesPendingRecompute()
    {
        var timers = new FakeTimers();
        var vm = Make(Solid(50, 50), timers: timers);
        vm.WidthText = "10";
        vm.ImportCommand.Execute(null);
        Assert.Equal(10, vm.Result!.Image.Width);
    }
}

public class AnimationViewModelTests
{
    private static List<PixelImage> Frames(int n) =>
        Enumerable.Range(0, n).Select(i => new PixelImage(2, 2, [(uint)(0xFF000000 | (uint)(i + 1)), 0, 0, 0])).ToList();

    [Fact]
    public void PlaysFramesOnTick_AndLoops()
    {
        var timers = new FakeTimers();
        var vm = new AnimationViewModel(new FakeDialogs(), timers, Frames(3), "слои");
        Assert.True(vm.IsPlaying);
        Assert.Equal("Пауза", vm.PlayButtonText);
        Assert.Equal("1 / 3", vm.FrameLabel);

        timers.FireAll();
        Assert.Equal(1, vm.FrameValue);
        timers.FireAll();
        timers.FireAll();
        Assert.Equal(0, vm.FrameValue); // it looped
        Assert.True(vm.IsPlaying);
    }

    [Fact]
    public void WithoutLoop_StopsAtTheLastFrame_AndPlayRestarts()
    {
        var timers = new FakeTimers();
        var vm = new AnimationViewModel(new FakeDialogs(), timers, Frames(2), "x") { Loop = false };
        timers.FireAll();
        Assert.Equal(1, vm.FrameValue);
        timers.FireAll();
        Assert.False(vm.IsPlaying);
        Assert.Equal("Пуск", vm.PlayButtonText);

        vm.PlayCommand.Execute(null); // from the end without looping it starts over
        Assert.Equal(0, vm.FrameValue);
        Assert.True(vm.IsPlaying);
    }

    [Fact]
    public void ScrubbingBySlider_PausesPlayback()
    {
        var timers = new FakeTimers();
        var vm = new AnimationViewModel(new FakeDialogs(), timers, Frames(4), "x");
        vm.FrameValue = 2;
        Assert.False(vm.IsPlaying);
        Assert.Equal("3 / 4", vm.FrameLabel);
        Assert.Equal(Frames(4)[2].Pixels[0], vm.CurrentFrame.Pixels[0]);
        Assert.False(timers.Created[0].IsEnabled);
    }

    [Fact]
    public void SingleFrame_CannotPlayOrScrub()
    {
        var vm = new AnimationViewModel(new FakeDialogs(), new FakeTimers(), Frames(1), "x");
        Assert.False(vm.IsPlaying);
        Assert.False(vm.CanScrub);
        Assert.False(vm.PlayCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("8", 125)]
    [InlineData("10", 100)]
    [InlineData("60", 17)]
    [InlineData("1000", 17)] // not faster than 60 frames per second
    [InlineData("abc", 125)]
    [InlineData("0", 125)]
    public void Fps_ControlsDelayAndTimerInterval(string fps, int expectedMs)
    {
        var timers = new FakeTimers();
        var vm = new AnimationViewModel(new FakeDialogs(), timers, Frames(2), "x");
        vm.FpsText = fps;
        Assert.Equal(expectedMs, vm.DelayMs);
        Assert.Equal(TimeSpan.FromMilliseconds(expectedMs), timers.Created[0].Interval);
    }

    [Fact]
    public void ExportGif_WritesAValidFile_AndReportsIt()
    {
        var dialogs = new FakeDialogs();
        var path = Path.Combine(Path.GetTempPath(), "pixed-anim-" + Guid.NewGuid().ToString("N") + ".gif");
        try
        {
            dialogs.SaveFileFn = () => path;
            var vm = new AnimationViewModel(dialogs, new FakeTimers(), Frames(3), "x");
            vm.ExportGifCommand.Execute(null);
            var bytes = File.ReadAllBytes(path);
            Assert.Equal("GIF89a", System.Text.Encoding.ASCII.GetString(bytes, 0, 6));
            Assert.Single(dialogs.Infos);
            Assert.Empty(dialogs.Errors);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ExportGif_ToUnwritablePath_ShowsError()
    {
        var dialogs = new FakeDialogs { SaveFileFn = () => Path.Combine(Path.GetTempPath(), "no-such-dir-" + Guid.NewGuid().ToString("N"), "a.gif") };
        var vm = new AnimationViewModel(dialogs, new FakeTimers(), Frames(2), "x");
        vm.ExportGifCommand.Execute(null);
        Assert.Single(dialogs.Errors);
    }

    [Fact]
    public void Closing_StopsTheTimer()
    {
        var timers = new FakeTimers();
        var vm = new AnimationViewModel(new FakeDialogs(), timers, Frames(3), "x");
        Assert.True(timers.Created[0].IsEnabled);
        vm.NotifyClosed();
        Assert.False(timers.Created[0].IsEnabled);
    }

    [Fact]
    public void SheetViewModel_OpensAnimationWindowWithFrames()
    {
        using var rig = new Rig();
        rig.Vm.Layers.AddCommand.Execute(null);
        rig.Vm.Sheet.AnimationCommand.Execute(null);
        Assert.Single(rig.Dialogs.AnimationWindows);
        Assert.Equal("Кадры: слои снизу вверх", rig.Dialogs.AnimationWindows[0].SourceDescription);
        Assert.True(rig.Dialogs.AnimationWindows[0].IsPlaying);
    }
}

public class BatchViewModelTests
{
    private static (BatchViewModel Vm, Rig Rig) Make()
    {
        var rig = new Rig();
        return (new BatchViewModel(rig.Services, rig.Settings.Templates), rig);
    }

    [Fact]
    public void Run_NeedsTemplateAndFolders()
    {
        var (vm, rig) = Make();
        using var _ = rig;
        vm.SelectedTemplate = null;
        vm.RunCommand.Execute(null);
        Assert.Equal("Выберите шаблон", vm.Summary);

        vm.SelectedTemplate = new TemplateItem("t", "t.json");
        vm.RunCommand.Execute(null);
        Assert.Equal("Укажите обе папки", vm.Summary);
        Assert.Empty(rig.Batch.Calls);
    }

    [Fact]
    public async Task Run_ShowsRowsAndSummary()
    {
        var (vm, rig) = Make();
        using var _ = rig;
        rig.Batch.Result = new BatchResult(3, 1, 1, 1,
        [
            new BatchItemResult("a.png", "ok", []),
            new BatchItemResult("b.png", "checks_failed", ["maxColors: цветов 20, лимит 16"]),
            new BatchItemResult("c.png", "error", ["шаг 1 (resize): нет файла"]),
        ]);
        vm.SelectedTemplate = new TemplateItem("t", "t.json");
        vm.InputDir = "in";
        vm.OutputDir = "out";
        vm.Recurse = true;

        await vm.RunAsync();
        Assert.Equal([("t.json", "in", "out", true)], rig.Batch.Calls);
        Assert.Equal(["Готово", "Проверки не пройдены", "Ошибка"], vm.Rows.Select(r => r.Status));
        Assert.Contains("лимит 16", vm.Rows[1].Details);
        Assert.Equal("Всего 3: готово 1, проверки не пройдены 1, ошибки 1", vm.Summary);
        Assert.False(vm.IsRunning);
        Assert.True(vm.RunCommand.CanExecute(null));
    }

    [Fact]
    public async Task Run_RunnerFailure_ShowsErrorAndUnlocks()
    {
        var (vm, rig) = Make();
        using var _ = rig;
        rig.Batch.Throw = new InvalidOperationException("папки нет");
        vm.SelectedTemplate = new TemplateItem("t", "t.json");
        vm.InputDir = "in";
        vm.OutputDir = "out";
        await vm.RunAsync();
        Assert.Equal("Ошибка: папки нет", vm.Summary);
        Assert.False(vm.IsRunning);
    }

    [Fact]
    public void Browse_FillsFields()
    {
        var (vm, rig) = Make();
        using var _ = rig;
        rig.Dialogs.FolderFn = () => Path.Combine(Path.GetTempPath(), "sprites");
        vm.BrowseInputCommand.Execute(null);
        Assert.EndsWith("sprites", vm.InputDir);
        Assert.EndsWith(Path.Combine("sprites", "processed"), vm.OutputDir); // the output folder is filled in by itself

        rig.Dialogs.FolderFn = () => "custom-out";
        vm.BrowseOutputCommand.Execute(null);
        Assert.Equal("custom-out", vm.OutputDir);

        rig.Dialogs.OpenFileFn = () => "mine.json";
        vm.BrowseTemplateCommand.Execute(null);
        Assert.Equal("mine.json", vm.SelectedTemplate!.Source);
    }

    [Fact]
    public void Templates_ComeFromLibrary_SystemFirstAndFirstIsSelected()
    {
        var (vm, rig) = Make();
        using var _ = rig;
        rig.Settings.Templates.Save("Мой", "", """{"steps":[{"op":"trim"}]}""");
        var vm2 = new BatchViewModel(rig.Services, rig.Settings.Templates);
        Assert.Equal(BuiltInTemplates.All.Count + 1, vm2.Templates.Count);
        Assert.True(vm2.Templates[0].IsSystem);
        Assert.Same(vm2.Templates[0], vm2.SelectedTemplate);
        Assert.Contains(vm2.Templates, t => t is { Name: "Мой", IsSystem: false });
    }

    [Fact]
    public void AutomationCommands_OpenBatchAndReferenceThroughDialogService()
    {
        using var rig = new Rig();
        rig.Vm.Automation.BatchCommand.Execute(null);
        rig.Vm.Automation.ReferenceCommand.Execute(null);
        Assert.Equal((1, 1), (rig.Dialogs.BatchShown, rig.Dialogs.ReferenceShown));
    }
}

public class ReferenceViewModelTests
{
    [Fact]
    public void ListsBuiltInOperationsAndChecks()
    {
        var vm = new ReferenceViewModel(new FakeShell(), "tpl");
        Assert.Contains(vm.Operations, o => o.StartsWith("resize"));
        Assert.Contains(vm.Checks, c => c.StartsWith("maxColors"));
        Assert.All(vm.Operations, o => Assert.Contains("[", o));
        Assert.False(string.IsNullOrWhiteSpace(vm.AiText));
        Assert.NotEmpty(vm.Plugins);
    }

    [Fact]
    public void OpenFolderCommands_GoThroughTheShell()
    {
        var shell = new FakeShell();
        var vm = new ReferenceViewModel(shell, Path.Combine("data", "pipelines"));
        vm.OpenPluginsFolderCommand.Execute(null);
        vm.OpenTemplatesFolderCommand.Execute(null);
        Assert.Equal(2, shell.Opened.Count);
        Assert.EndsWith("plugins", shell.Opened[0]);
        Assert.EndsWith("pipelines", shell.Opened[1]);
    }
}

public class TimerIntegrationTests
{
    [Fact]
    public void Debouncer_CollapsesBurstsIntoOneAction()
    {
        var timers = new FakeTimers();
        int runs = 0;
        var d = new Debouncer(timers, TimeSpan.FromMilliseconds(100), () => runs++);
        d.Trigger(); d.Trigger(); d.Trigger();
        Assert.Equal(0, runs);
        timers.FireAll();
        Assert.Equal(1, runs);
        timers.FireAll(); // the timer is stopped, there is no second run
        Assert.Equal(1, runs);
    }

    [Fact]
    public void Debouncer_FlushRunsPendingImmediately_AndWithoutTimerRunsAtOnce()
    {
        var timers = new FakeTimers();
        int runs = 0;
        var d = new Debouncer(timers, TimeSpan.FromMilliseconds(100), () => runs++);
        d.Flush();
        Assert.Equal(0, runs); // there is nothing to wait for
        d.Trigger();
        d.Flush();
        Assert.Equal(1, runs);

        int direct = 0;
        new Debouncer(null, TimeSpan.Zero, () => direct++).Trigger();
        Assert.Equal(1, direct);
    }

    [Fact]
    public void Stats_AreRecomputedAfterTheDebounceTimer_NotAfterEveryStroke()
    {
        using var rig = new Rig();
        rig.Vm.Colors.SetPrimary(Rig.Red);
        rig.Stroke(0, 0, 3, 0);
        Assert.Empty(rig.Vm.Colors.Stats); // the timer has not fired yet

        rig.Timers.FireAll();
        Assert.Single(rig.Vm.Colors.Stats);
        Assert.Equal(4, rig.Vm.Colors.Stats[0].Count);
    }

    [Fact]
    public void NewDocument_RecomputesStatsImmediately()
    {
        using var rig = new Rig();
        rig.Layer.Pixels[0] = Rig.Red;
        rig.Dialogs.NewDocFn = (_, _) => new NewDocumentResult(4, 4);
        rig.Vm.NewCommand.Execute(null);
        Assert.Empty(rig.Vm.Colors.Stats);
        Assert.Contains("0", rig.Vm.Colors.StatsHeader);
    }

    [Fact]
    public void AutosaveTimer_SavesDirtyDocumentOnTick_AndStopsOnClose()
    {
        using var rig = new Rig();
        var timer = rig.Timers.Created.Last(); // the autosave is created last in the root model
        Assert.True(timer.IsEnabled);
        Assert.Equal(TimeSpan.FromSeconds(60), timer.Interval);

        rig.Timers.Created.ForEach(t => t.Fire());
        Assert.False(File.Exists(rig.Settings.AutosavePath)); // clean

        rig.Stroke(1, 1, 1, 1);
        rig.Timers.Created.ForEach(t => t.Fire());
        Assert.True(File.Exists(rig.Settings.AutosavePath));

        rig.Dialogs.SaveChoiceFn = () => SaveChoice.Discard;
        Assert.True(rig.Vm.TryClose());
        Assert.False(timer.IsEnabled);
    }
}

public class CanvasInfoTests
{
    [Fact]
    public void WorkspaceLabel_ShowsSize_AndSheetCellUnderTheCursor()
    {
        using var rig = new Rig();
        var changed = new List<string>();
        rig.Vm.Canvas.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);
        Assert.Equal("32 × 32 px", rig.Vm.Canvas.WorkspaceLabel);

        rig.Doc.SheetCellWidth = rig.Doc.SheetCellHeight = 16;
        rig.S.RaiseOverlaysChanged();
        Assert.Contains("WorkspaceLabel", changed);
        Assert.Equal("32 × 32 px · Cell 1, 1", rig.Vm.Canvas.WorkspaceLabel);

        rig.Vm.Canvas.UpdatePosition(20, 3);
        Assert.Equal("32 × 32 px · Cell 2, 1", rig.Vm.Canvas.WorkspaceLabel);
        rig.Vm.Canvas.UpdatePosition(31, 31);
        Assert.Equal("32 × 32 px · Cell 2, 2", rig.Vm.Canvas.WorkspaceLabel);

        rig.Vm.Canvas.UpdatePosition(-5, 4); // outside the canvas: the caption does not jump
        Assert.Equal("32 × 32 px · Cell 2, 2", rig.Vm.Canvas.WorkspaceLabel);
    }

    [Fact]
    public void DocumentSizeText_FollowsCanvasResizeAndNewDocuments()
    {
        using var rig = new Rig();
        Assert.Equal("32 × 32", rig.Vm.Canvas.DocumentSizeText);

        rig.Dialogs.CanvasSizeFn = (_, _) => new CanvasSizeResult(48, 40, 0, 0);
        rig.Vm.Canvas.CanvasSizeCommand.Execute(null);
        Assert.Equal("48 × 40", rig.Vm.Canvas.DocumentSizeText);
        Assert.Equal("48 × 40 px", rig.Vm.Canvas.WorkspaceLabel);

        rig.Dialogs.NewDocFn = (_, _) => new NewDocumentResult(8, 6);
        rig.Dialogs.SaveChoiceFn = () => SaveChoice.Discard;
        rig.Vm.NewCommand.Execute(null);
        Assert.Equal("8 × 6", rig.Vm.Canvas.DocumentSizeText);
    }
}

public class PasteAndShellTests
{
    private static PixelImage Big(int w, int h) => TestImages.Solid(w, h);

    [Fact]
    public void Paste_LargerThanCanvas_AsksAndEnlargesTheCanvas()
    {
        using var rig = new Rig();
        rig.Clipboard.SetExternal(Big(64, 40));
        rig.Vm.Canvas.PasteCommand.Execute(null);

        Assert.Single(rig.Dialogs.Confirms);
        Assert.Contains("64×40", rig.Dialogs.Confirms[0]);
        Assert.Equal((64, 40), (rig.Doc.Width, rig.Doc.Height));
        Assert.True(rig.Vm.Canvas.HasFloat);
        Assert.Equal(Rig.Red, rig.Layer.Get(63, 39)); // pasted whole, into the top left corner
        Assert.Equal(new PixelRect(0, 0, 64, 40), rig.Vm.Canvas.SelectionBounds);

        rig.Vm.Canvas.CommitFloat();
        rig.Vm.UndoCommand.Execute(null); // undo of the paste
        rig.Vm.UndoCommand.Execute(null); // undo of the canvas enlargement
        Assert.Equal((32, 32), (rig.Doc.Width, rig.Doc.Height));
    }

    [Fact]
    public void Paste_LargerThanCanvas_CanBeKeptClipped()
    {
        using var rig = new Rig();
        rig.Dialogs.ConfirmFn = _ => false;
        rig.Clipboard.SetExternal(Big(64, 64));
        rig.Vm.Canvas.PasteCommand.Execute(null);
        Assert.Equal((32, 32), (rig.Doc.Width, rig.Doc.Height));
        Assert.True(rig.Vm.Canvas.HasFloat);
        Assert.Equal(Rig.Red, rig.Layer.Get(31, 31));
    }

    [Fact]
    public void Paste_SmallerThanCanvas_DoesNotAsk()
    {
        using var rig = new Rig();
        rig.Clipboard.SetExternal(Big(10, 10));
        rig.Vm.Canvas.PasteCommand.Execute(null);
        Assert.Empty(rig.Dialogs.Confirms);
        Assert.Equal(32, rig.Doc.Width);
    }

    [Fact]
    public void Paste_TooBigToEnlarge_ShowsErrorAndKeepsDocument()
    {
        using var rig = new Rig();
        rig.Clipboard.SetExternal(new PixelImage(9000, 2)); // more than the 8192 limit
        rig.Vm.Canvas.PasteCommand.Execute(null);
        Assert.Single(rig.Dialogs.Errors);
        Assert.Equal(32, rig.Doc.Width);
        Assert.False(rig.Vm.Canvas.HasFloat);
    }

    [Fact]
    public void ImportFromClipboard_OffersTheOriginalByDefault()
    {
        using var rig = new Rig();
        rig.Clipboard.SetExternal(Big(200, 100));
        PixelImage? seen = null;
        rig.Dialogs.ImportConfigure = vm => seen = vm.Preview;
        rig.Vm.ImportClipboardCommand.Execute(null);
        Assert.Equal((200, 100), (seen!.Width, seen.Height));
        Assert.Equal((200, 100), (rig.Doc.Width, rig.Doc.Height)); // "new document" in the original size
    }

    [Fact]
    public void ToggleTheme_FlipsBetweenDarkAndLight_AndUpdatesLabel()
    {
        using var rig = new Rig();
        Assert.Equal("Тёмная", rig.Vm.ThemeLabel);
        rig.Vm.ToggleThemeCommand.Execute(null);
        Assert.True(rig.Vm.IsThemeLight);
        Assert.Equal("Светлая", rig.Vm.ThemeLabel);
        rig.Vm.ToggleThemeCommand.Execute(null);
        Assert.True(rig.Vm.IsThemeDark);
        Assert.Equal(["Light", "Dark"], rig.Theme.Applied);
    }

    [Fact]
    public void DocumentName_ChangesWithFileAndDirtyState()
    {
        using var rig = new Rig();
        var names = new List<string>();
        rig.Vm.PropertyChanged += (_, e) => { if (e.PropertyName == "DocumentName") names.Add(rig.Vm.DocumentName); };
        rig.Stroke(0, 0, 0, 0);
        Assert.Contains("Без имени •", names);

        var path = Path.Combine(rig.Dir, "hero.pixproj");
        rig.Dialogs.SaveFileFn = () => path;
        rig.Vm.SaveCommand.Execute(null);
        Assert.Equal("hero.pixproj", rig.Vm.DocumentName);
    }
}
