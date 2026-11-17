// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.IO;
using System.Windows;
using PixelEditor.App.Themes;
using PixelEditor.Core;
using PixelEditor.ViewModels;

namespace PixelEditor.App;

public partial class App : Application
{
    public static AppSettings Settings { get; private set; } = AppSettings.Load();

    /// <summary>A .pixproj file passed on the command line (double click in Explorer).</summary>
    public static string? StartupFile { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        Log.Application = "PixelEditor";
        Log.Info($"{AppInfo.Name} {AppInfo.Version} started");
        // unhandled errors go to the log; UI ones do not close the window, for the others the log is written before exit
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error(args.Exception, "Unhandled exception on the UI thread", "app");
            args.Handled = true;
            try
            {
                MessageBox.Show(Loc.T("app.unexpected_error", Log.CurrentFile), AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { }
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error(args.ExceptionObject as Exception ?? new Exception(Convert.ToString(args.ExceptionObject)), "Unhandled exception, the application is closing", "app");
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Unobserved task exception", "app");
            args.SetObserved();
        };
        Settings = AppSettings.Load();
        StartupFile = e.Args.FirstOrDefault(a => File.Exists(a) && a.EndsWith(".pixproj", StringComparison.OrdinalIgnoreCase));
        Loc.Instance.SetLanguage(Settings.Language);
        ThemeManager.Initialize();
        ThemeManager.Apply(Settings.Theme);
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Info("Exit with code " + e.ApplicationExitCode);
        base.OnExit(e);
    }

    /// <summary>Light, Dark or System.</summary>
    public static void ApplyTheme(string theme) => ThemeManager.Apply(theme);
}
