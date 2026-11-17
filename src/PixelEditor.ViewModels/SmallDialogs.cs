// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using PixelEditor.Core;

namespace PixelEditor.ViewModels;

/// <summary>Input of a single string.</summary>
public sealed class PromptViewModel : DialogViewModel
{
    private string _text;

    public PromptViewModel(string title, string label, string defaultValue)
    {
        Title = title;
        Label = label;
        _text = defaultValue;
        OkCommand = new RelayCommand(() => Close(true));
    }

    public override string Title { get; }
    public string Label { get; }

    public string Text
    {
        get => _text;
        set => SetProperty(ref _text, value);
    }

    public RelayCommand OkCommand { get; }
}

public sealed class NewDocumentViewModel : DialogViewModel
{
    private string _width, _height, _error = "";

    public NewDocumentViewModel(int width, int height)
    {
        _width = width.ToString();
        _height = height.ToString();
        OkCommand = new RelayCommand(Ok);
    }

    public override string Title => Loc.T("small_dlg.new_document");

    public string WidthText { get => _width; set => SetProperty(ref _width, value); }
    public string HeightText { get => _height; set => SetProperty(ref _height, value); }
    public string Error { get => _error; private set => SetProperty(ref _error, value); }

    public NewDocumentResult? Result { get; private set; }
    public RelayCommand OkCommand { get; }

    private void Ok()
    {
        if (!int.TryParse(_width, out var w) || !int.TryParse(_height, out var h) || w < 1 || h < 1 || w > 4096 || h > 4096)
        {
            Error = Loc.T("small_dlg.the_size_must_be_a");
            return;
        }
        Result = new NewDocumentResult(w, h);
        Close(true);
    }
}

public enum AnchorPosition { TopLeft, Top, TopRight, Left, Center, Right, BottomLeft, Bottom, BottomRight }

public sealed class CanvasSizeViewModel : DialogViewModel
{
    private string _width, _height, _error = "";
    private AnchorPosition _anchor = AnchorPosition.TopLeft;

    public CanvasSizeViewModel(int width, int height)
    {
        _width = width.ToString();
        _height = height.ToString();
        OkCommand = new RelayCommand(Ok);
    }

    public override string Title => Loc.T("small_dlg.canvas_size");

    public string WidthText { get => _width; set => SetProperty(ref _width, value); }
    public string HeightText { get => _height; set => SetProperty(ref _height, value); }
    public string Error { get => _error; private set => SetProperty(ref _error, value); }
    public AnchorPosition Anchor { get => _anchor; set => SetProperty(ref _anchor, value); }

    public CanvasSizeResult? Result { get; private set; }
    public RelayCommand OkCommand { get; }

    private void Ok()
    {
        if (!int.TryParse(_width, out var w) || !int.TryParse(_height, out var h) || w < 1 || h < 1 || w > 8192 || h > 8192)
        {
            Error = Loc.T("image_size.the_size_must_be_a");
            return;
        }
        int i = (int)_anchor;
        Result = new CanvasSizeResult(w, h, (i % 3) / 2.0, (i / 3) / 2.0);
        Close(true);
    }
}

/// <summary>Short ways to show typical dialogs: they create a model, show it and return the result.</summary>
public static class DialogExtensions
{
    /// <summary>Shows an error and writes the exception with its stack to the log (the log is always English, the window is in the UI language).</summary>
    public static void ShowError(this IDialogService dialogs, Exception ex)
    {
        Log.Error(ex, "Operation failed and an error dialog was shown", "ui");
        dialogs.ShowError(ex.Message);
    }

    public static string? Prompt(this IDialogService dialogs, string title, string label, string defaultValue)
    {
        var vm = new PromptViewModel(title, label, defaultValue);
        return dialogs.ShowDialog(vm) == true ? vm.Text.Trim() : null;
    }

    public static NewDocumentResult? AskNewDocument(this IDialogService dialogs, int width, int height)
    {
        var vm = new NewDocumentViewModel(width, height);
        return dialogs.ShowDialog(vm) == true ? vm.Result : null;
    }

    public static ImageSizeResult? AskImageSize(this IDialogService dialogs, int width, int height)
    {
        var vm = new ImageSizeViewModel(width, height);
        return dialogs.ShowDialog(vm) == true ? vm.Result : null;
    }

    public static CanvasSizeResult? AskCanvasSize(this IDialogService dialogs, int width, int height)
    {
        var vm = new CanvasSizeViewModel(width, height);
        return dialogs.ShowDialog(vm) == true ? vm.Result : null;
    }

    public static ImportResult? AskImport(this IDialogService dialogs, IUiTimerFactory timers, PixelImage source, string name,
        IReadOnlyList<uint> palette, bool canAddLayer)
    {
        var vm = new ImportViewModel(dialogs, timers, source, name, palette, canAddLayer);
        return dialogs.ShowDialog(vm) == true ? vm.Result : null;
    }
}
