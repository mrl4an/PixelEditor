// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Windows;
using Microsoft.Win32;

namespace PixelEditor.App.Themes;

/// <summary>
/// Theme switching: swaps the palette dictionary (Palette.Dark / Palette.Light) in the application resources. Styles refer to tokens through
/// DynamicResource, so all open windows are recolored at once. The window title bar is custom (see the Window style), so the system one is left alone.
/// </summary>
public static class ThemeManager
{
    private const string PaletteMarker = "Themes/Palette.";
    private static bool _initialized;

    /// <summary>Once at startup: makes the Window style mandatory for all windows, including descendants (MainWindow, dialogs).</summary>
    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        // the implicit Window style does not apply to derived classes: set it as the default style
        FrameworkElement.StyleProperty.OverrideMetadata(typeof(Window),
            new FrameworkPropertyMetadata { DefaultValue = Application.Current.FindResource(typeof(Window)) });
    }

    public static bool IsDark { get; private set; } = true;

    /// <summary>Light, Dark or System (as in Windows).</summary>
    public static void Apply(string theme)
    {
        IsDark = theme switch
        {
            "Dark" => true,
            "Light" => false,
            _ => !SystemUsesLightTheme(),
        };

        var merged = Application.Current.Resources.MergedDictionaries;
        var fresh = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/PixelEditor;component/Themes/Palette.{(IsDark ? "Dark" : "Light")}.xaml"),
        };
        int index = merged.ToList().FindIndex(d => d.Source != null && d.Source.OriginalString.Contains(PaletteMarker));
        if (index >= 0) merged[index] = fresh; else merged.Insert(0, fresh);
    }

    private static bool SystemUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is not int v || v != 0;
        }
        catch { return false; }
    }
}
