// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using PixelEditor.Core;

namespace PixelEditor.ViewModels;

public enum SaveChoice { Save, Discard, Cancel }
public enum ImportTarget { NewDocument, NewLayer }

public sealed record NewDocumentResult(int Width, int Height);
public sealed record CanvasSizeResult(int Width, int Height, double AnchorX, double AnchorY);
public sealed record ImportResult(PixelImage Image, ImportTarget Target);

/// <summary>
/// The base of a dialog model. A dialog closes itself through Close; which window to show is decided by the view layer
/// (the model type → window mapping is set in one place, in the dialog service).
/// </summary>
public abstract class DialogViewModel : ObservableObject
{
    protected DialogViewModel()
    {
        CancelCommand = new RelayCommand(() => Close(false));
    }

    public abstract string Title { get; }
    public RelayCommand CancelCommand { get; }

    /// <summary>The model asks to close the window with a result (true: confirmed, false: cancelled).</summary>
    public event Action<bool>? CloseRequested;

    protected void Close(bool result) => CloseRequested?.Invoke(result);

    /// <summary>Called by the view after the window closes: stop timers and so on.</summary>
    public void NotifyClosed() => OnClosed();

    protected virtual void OnClosed() { }
}

/// <summary>Everything the models ask of the UI: dialogs, windows, file pickers. Replaced in tests.</summary>
public interface IDialogService
{
    /// <summary>Shows a modal dialog. Returns true on confirmation, false on cancel.</summary>
    bool? ShowDialog(DialogViewModel dialog);

    /// <summary>Shows a modeless window (for example the animation).</summary>
    void Show(DialogViewModel dialog);

    bool Confirm(string message, string title);
    SaveChoice AskSaveChanges();
    void ShowError(string message);
    void ShowInfo(string message, string title);
    void ShowWarning(string message, string title);

    string? PickOpenFile(string filter);
    string? PickSaveFile(string filter, string defaultName);
    string? PickFolder(string title);
}

public interface IClipboardService
{
    PixelImage? GetImage();
    bool TrySetImage(PixelImage image);

    /// <summary>Text from the clipboard (null if it is not text there or the clipboard is busy).</summary>
    string? GetText();

    bool TrySetText(string text);

    /// <summary>Changes on every change of the system clipboard: it tells us whether the clipboard still holds our copy.</summary>
    uint SequenceNumber { get; }
}

public interface IImageStore
{
    PixelImage Load(string path);
    void SavePng(PixelImage image, string path);
}

public interface IThemeService
{
    void Apply(string theme);
}

public interface IUiTimer
{
    TimeSpan Interval { get; set; }
    bool IsEnabled { get; }
    event Action? Tick;
    void Start();
    void Stop();
}

public interface IUiTimerFactory
{
    IUiTimer Create(TimeSpan interval);
}

public interface IShellService
{
    /// <summary>Opens a folder in Explorer (creates it if missing).</summary>
    void OpenFolder(string path);
}

public sealed record BatchItemResult(string File, string Status, IReadOnlyList<string> Problems);
public sealed record BatchResult(int Total, int Ok, int ChecksFailed, int Errors, IReadOnlyList<BatchItemResult> Items);

public interface IBatchRunner
{
    Task<BatchResult> RunAsync(string templatePath, string inputDir, string outputDir, bool recurse);
}

/// <summary>All application services as one bundle.</summary>
public sealed record AppServices(
    IDialogService Dialogs,
    IClipboardService Clipboard,
    IImageStore Images,
    IThemeService Theme,
    IUiTimerFactory Timers,
    IBatchRunner Batch,
    IShellService Shell);
