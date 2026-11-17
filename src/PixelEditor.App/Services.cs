// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using PixelEditor.App.Themes;
using PixelEditor.Core;
using PixelEditor.Imaging;
using PixelEditor.ViewModels;

namespace PixelEditor.App;

/// <summary>
/// WPF dialogs and windows for the view models. The only place that says which window shows which model.
/// </summary>
public sealed class DialogService(Window owner) : IDialogService
{
    private static Window CreateView(DialogViewModel vm) => vm switch
    {
        MessageViewModel => new MessageDialog(),
        RsiProjectViewModel => new RsiProjectDialog(),
        RsiStateViewModel => new RsiStateDialog(),
        RsiOpenViewModel => new RsiOpenDialog(),
        AnimationExportViewModel => new AnimationExportDialog(),
        ImageSizeViewModel => new ImageSizeDialog(),
        PreviewViewModel => new PreviewWindow(),
        ResultPreviewViewModel => new ResultPreviewWindow(),
        ChatPipelineViewModel => new ChatPipelineDialog(),
        OperationTunerViewModel => new OperationTunerDialog(),
        NameDescriptionViewModel => new NameDescriptionDialog(),
        PaletteLibraryViewModel => new PaletteLibraryWindow(),
        TemplateLibraryViewModel => new TemplateLibraryWindow(),
        PipelineEditorViewModel => new PipelineEditorWindow(),
        PromptViewModel => new PromptDialog(),
        NewDocumentViewModel => new NewDocumentDialog(),
        StartScreenViewModel => new StartScreenDialog(),
        CanvasSizeViewModel => new CanvasSizeDialog(),
        ImportViewModel => new ImportDialog(),
        AnimationViewModel => new AnimationWindow(),
        BatchViewModel => new BatchWindow(),
        ReferenceViewModel => new ToolsWindow(),
        _ => throw new NotSupportedException($"Нет окна для модели {vm.GetType().Name}"),
    };

    private Window Prepare(DialogViewModel vm, bool modal)
    {
        var window = CreateView(vm);
        window.Owner = owner;
        window.DataContext = vm;
        window.SetBinding(Window.TitleProperty, new Binding(nameof(DialogViewModel.Title)));
        vm.CloseRequested += ok =>
        {
            if (!modal) { window.Close(); return; }
            try { window.DialogResult = ok; }
            catch (InvalidOperationException) { window.Close(); }
        };
        // SizeToContent together with WindowChrome leaves black margins on screens scaled above 100%: recompute the size after showing
        window.ContentRendered += (_, _) =>
        {
            var mode = window.SizeToContent;
            if (mode == SizeToContent.Manual) return;
            window.SizeToContent = SizeToContent.Manual;
            window.SizeToContent = mode;
        };
        window.Closed += (_, _) => vm.NotifyClosed();
        return window;
    }

    public bool? ShowDialog(DialogViewModel dialog)
    {
        var result = Prepare(dialog, modal: true).ShowDialog();
        RestoreInput();
        return result;
    }

    /// <summary>
    /// After a modal window, return the main window to a working state: release the mouse capture and focus the window,
    /// otherwise the first wheel scrolls and hotkeys (zoom) may not reach the canvas until it is clicked.
    /// </summary>
    private void RestoreInput()
    {
        System.Windows.Input.Mouse.Capture(null);
        owner.Activate();
        owner.Dispatcher.BeginInvoke(() => owner.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    public void Show(DialogViewModel dialog) => Prepare(dialog, modal: false).Show();

    public bool Confirm(string message, string title) =>
        this.Ask(MessageViewModel.Confirm(title, message)) == MessageViewModel.Yes;

    public SaveChoice AskSaveChanges() =>
        this.Ask(MessageViewModel.SaveChanges()) switch
        {
            MessageViewModel.Save => SaveChoice.Save,
            MessageViewModel.Discard => SaveChoice.Discard,
            _ => SaveChoice.Cancel,
        };

    public void ShowError(string message) => this.Ask(MessageViewModel.Error(message));

    public void ShowInfo(string message, string title) => this.Ask(MessageViewModel.Info(title, message));

    public void ShowWarning(string message, string title) => this.Ask(MessageViewModel.Warning(title, message));

    public string? PickOpenFile(string filter)
    {
        var dlg = new OpenFileDialog { Filter = filter };
        return dlg.ShowDialog(owner) == true ? dlg.FileName : null;
    }

    public string? PickSaveFile(string filter, string defaultName)
    {
        var dlg = new SaveFileDialog { Filter = filter, FileName = defaultName };
        return dlg.ShowDialog(owner) == true ? dlg.FileName : null;
    }

    public string? PickFolder(string title)
    {
        var dlg = new OpenFolderDialog { Title = title };
        return dlg.ShowDialog(owner) == true ? dlg.FolderName : null;
    }
}

public sealed class ClipboardService : IClipboardService
{
    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    public uint SequenceNumber => GetClipboardSequenceNumber();

    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp"];

    /// <summary>
    /// Reads a picture from the clipboard trying formats in descending quality: PNG (browsers, editors, with transparency),
    /// an ordinary bitmap (screenshots, Paint) and a picture file copied in Explorer. Any failure moves on to the next way.
    /// </summary>
    public PixelImage? GetImage()
    {
        IDataObject? data;
        try { data = Clipboard.GetDataObject(); }
        catch { return null; } // the clipboard is busy with another application
        if (data == null) return null;

        try
        {
            if (data.GetDataPresent("PNG") && data.GetData("PNG") is Stream png)
            {
                using var copy = new MemoryStream();
                png.CopyTo(copy);
                copy.Position = 0;
                var decoder = BitmapDecoder.Create(copy, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                return ImageIO.FromBitmap(decoder.Frames[0]);
            }
        }
        catch { /* a broken PNG: try the next way */ }

        try
        {
            if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } bmp)
                return ImageIO.FromBitmap(bmp, zeroAlphaMeansOpaque: true);
        }
        catch { /* the raster is unreadable: try files */ }

        try
        {
            if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] files)
            {
                var file = files.FirstOrDefault(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()) && File.Exists(f));
                if (file != null) return ImageIO.Load(file);
            }
        }
        catch { /* the file did not open */ }

        return null;
    }

    public string? GetText()
    {
        try { return Clipboard.ContainsText() ? Clipboard.GetText() : null; }
        catch { return null; } // the clipboard is busy with another application
    }

    public bool TrySetText(string text)
    {
        for (int i = 0; i < 5; i++)
        {
            try { Clipboard.SetText(text); return true; }
            catch { System.Threading.Thread.Sleep(40); }
        }
        return false;
    }

    /// <summary>Puts both an ordinary bitmap and a PNG: other programs get the picture and we keep transparency when pasting.</summary>
    public bool TrySetImage(PixelImage image)
    {
        try
        {
            var data = new DataObject();
            data.SetImage(ImageIO.ToBitmap(image));
            data.SetData("PNG", new MemoryStream(ImageIO.EncodePng(image)));
            Clipboard.SetDataObject(data, true);
            return true;
        }
        catch
        {
            return false; // the clipboard is busy with another application: an internal copy remains
        }
    }
}

public sealed class ImageStore : IImageStore
{
    public PixelImage Load(string path) => ImageIO.Load(path);
    public void SavePng(PixelImage image, string path) => ImageIO.SavePng(image, path);
}

public sealed class ThemeService : IThemeService
{
    public void Apply(string theme) => ThemeManager.Apply(theme);
}

/// <summary>A model timer on top of DispatcherTimer (fires on the UI thread).</summary>
public sealed class DispatcherUiTimer : IUiTimer
{
    private readonly DispatcherTimer _timer;

    public DispatcherUiTimer(TimeSpan interval)
    {
        _timer = new DispatcherTimer { Interval = interval };
        _timer.Tick += (_, _) => Tick?.Invoke();
    }

    public TimeSpan Interval { get => _timer.Interval; set => _timer.Interval = value; }
    public bool IsEnabled => _timer.IsEnabled;
    public event Action? Tick;
    public void Start() => _timer.Start();
    public void Stop() => _timer.Stop();
}

public sealed class UiTimerFactory : IUiTimerFactory
{
    public IUiTimer Create(TimeSpan interval) => new DispatcherUiTimer(interval);
}

public sealed class ShellService : IShellService
{
    public void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
    }
}

/// <summary>Batch processing on a background thread through the shared engine.</summary>
public sealed class BatchRunner : IBatchRunner
{
    public Task<BatchResult> RunAsync(string templatePath, string inputDir, string outputDir, bool recurse) =>
        Task.Run(() =>
        {
            var report = PixelEngine.Batch(PixelEngine.LoadPipeline(templatePath), inputDir, outputDir, recurse);
            return new BatchResult(report.Total, report.Ok, report.ChecksFailed, report.Errors,
                report.Items.Select(i => new BatchItemResult(i.Input, i.Status, i.Problems)).ToList());
        });
}
