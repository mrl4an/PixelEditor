// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Collections.ObjectModel;
using System.IO;

namespace PixelEditor.ViewModels;

/// <summary>A one-click blank canvas size on the start screen.</summary>
public sealed record StartSizeItem(int Width, int Height)
{
    public string Label => $"{Width}×{Height}";
}

/// <summary>A recent file on the start screen: the file name and its folder are shown separately.</summary>
public sealed record StartRecentItem(string Path, string Name, string Folder);

/// <summary>
/// The start (welcome) screen: one-click ways to begin work. Every choice remembers the action, closes the dialog
/// and the owner runs the action after the dialog is gone (so a cancelled New dialog does not bring the start screen back).
/// The actions themselves are the existing commands of <see cref="MainViewModel"/>, nothing is duplicated here.
/// </summary>
public sealed class StartScreenViewModel : DialogViewModel
{
    private static readonly int[] PresetSizes = [16, 32, 48, 64];

    private readonly MainViewModel _main;

    public StartScreenViewModel(MainViewModel main)
    {
        _main = main;
        var s = main.Settings;

        foreach (var n in PresetSizes) Sizes.Add(new StartSizeItem(n, n));
        // the last used size (the one the New dialog remembers) is added when it is not one of the presets
        if (s.DefaultWidth >= 1 && s.DefaultHeight >= 1 && !Sizes.Any(z => z.Width == s.DefaultWidth && z.Height == s.DefaultHeight))
            Sizes.Add(new StartSizeItem(s.DefaultWidth, s.DefaultHeight));

        main.RefreshRecent();
        foreach (var r in main.RecentFiles)
            Recent.Add(new StartRecentItem(r.Path, Path.GetFileName(r.Path), Path.GetDirectoryName(r.Path) ?? ""));

        NewSizeCommand = new RelayCommand(p => { if (p is StartSizeItem z) Choose(() => main.NewBlank(z.Width, z.Height)); });
        OtherSizeCommand = new RelayCommand(() => Choose(() => main.NewCommand.Execute(null)));
        AnimationSheetCommand = new RelayCommand(() => Choose(() => main.Rsi.NewSheetCommand.Execute(null)));
        // without an RSI folder a state cannot be created yet: the "New RSI" flow creates the folder and offers the first state
        RsiStateCommand = new RelayCommand(() => Choose(() =>
        {
            if (main.Rsi.CurrentFolder == null) main.Rsi.NewProjectCommand.Execute(null);
            else main.Rsi.NewStateCommand.Execute(null);
        }));
        OpenCommand = new RelayCommand(() => Choose(() => main.OpenCommand.Execute(null)));
        ImportCommand = new RelayCommand(() => Choose(() => main.ImportCommand.Execute(null)));
        OpenRecentCommand = new RelayCommand(p => { if (p is StartRecentItem r) Choose(() => main.OpenRecentCommand.Execute(r.Path)); });
    }

    public override string Title => Loc.T("start.title");

    public ObservableCollection<StartSizeItem> Sizes { get; } = new();
    public ObservableCollection<StartRecentItem> Recent { get; } = new();
    public bool HasRecent => Recent.Count > 0;
    public bool HasNoRecent => Recent.Count == 0;

    /// <summary>The "Show at startup" checkbox: stored in the settings at once.</summary>
    public bool ShowAtStartup
    {
        get => _main.Settings.ShowStartScreen;
        set
        {
            if (_main.Settings.ShowStartScreen == value) return;
            _main.Settings.ShowStartScreen = value;
            _main.Settings.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>The action chosen by the user; the owner runs it after the dialog has closed. Null when the dialog was just closed.</summary>
    public Action? Chosen { get; private set; }

    public RelayCommand NewSizeCommand { get; }
    public RelayCommand OtherSizeCommand { get; }
    public RelayCommand AnimationSheetCommand { get; }
    public RelayCommand RsiStateCommand { get; }
    public RelayCommand OpenCommand { get; }
    public RelayCommand ImportCommand { get; }
    public RelayCommand OpenRecentCommand { get; }

    private void Choose(Action action)
    {
        Chosen = action;
        Close(true);
    }
}
