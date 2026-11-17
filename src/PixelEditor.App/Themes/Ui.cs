// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Windows;
using System.Windows.Controls.Primitives;

namespace PixelEditor.App.Themes;

/// <summary>
/// Attached properties for the templates in Themes/Controls. A new element gets the shared look by setting only data, without its own template:
/// Icon and Hotkey (tool button), Subtitle and TitleContent (window title), WindowAction (minimize, maximize, close buttons).
/// </summary>
public static class Ui
{
    public static readonly DependencyProperty IconProperty =
        DependencyProperty.RegisterAttached("Icon", typeof(string), typeof(Ui), new PropertyMetadata(""));

    /// <summary>Vector tool icon (geometry in a 24×24 system, see Icons.xaml). Drawn with a text-colored line; IconFilled fills it.</summary>
    public static readonly DependencyProperty IconDataProperty =
        DependencyProperty.RegisterAttached("IconData", typeof(System.Windows.Media.Geometry), typeof(Ui), new PropertyMetadata(null));

    public static readonly DependencyProperty IconFilledProperty =
        DependencyProperty.RegisterAttached("IconFilled", typeof(bool), typeof(Ui), new PropertyMetadata(false));

    public static readonly DependencyProperty HotkeyProperty =
        DependencyProperty.RegisterAttached("Hotkey", typeof(string), typeof(Ui), new PropertyMetadata(""));

    /// <summary>Muted caption next to the window name in the title bar (for example the open file name).</summary>
    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.RegisterAttached("Subtitle", typeof(string), typeof(Ui), new PropertyMetadata(""));

    /// <summary>Extra elements in the title bar to the left of the window buttons (for example the theme switch).</summary>
    public static readonly DependencyProperty TitleContentProperty =
        DependencyProperty.RegisterAttached("TitleContent", typeof(object), typeof(Ui), new PropertyMetadata(null));

    /// <summary>Title bar button action: Minimize, Maximize (maximizes and restores), Close.</summary>
    public static readonly DependencyProperty WindowActionProperty =
        DependencyProperty.RegisterAttached("WindowAction", typeof(string), typeof(Ui),
            new PropertyMetadata(null, OnWindowActionChanged));

    public static string GetIcon(DependencyObject d) => (string)d.GetValue(IconProperty);
    public static void SetIcon(DependencyObject d, string value) => d.SetValue(IconProperty, value);

    public static System.Windows.Media.Geometry? GetIconData(DependencyObject d) => (System.Windows.Media.Geometry?)d.GetValue(IconDataProperty);
    public static void SetIconData(DependencyObject d, System.Windows.Media.Geometry? value) => d.SetValue(IconDataProperty, value);

    public static bool GetIconFilled(DependencyObject d) => (bool)d.GetValue(IconFilledProperty);
    public static void SetIconFilled(DependencyObject d, bool value) => d.SetValue(IconFilledProperty, value);

    public static string GetHotkey(DependencyObject d) => (string)d.GetValue(HotkeyProperty);
    public static void SetHotkey(DependencyObject d, string value) => d.SetValue(HotkeyProperty, value);

    public static string GetSubtitle(DependencyObject d) => (string)d.GetValue(SubtitleProperty);
    public static void SetSubtitle(DependencyObject d, string value) => d.SetValue(SubtitleProperty, value);

    public static object? GetTitleContent(DependencyObject d) => d.GetValue(TitleContentProperty);
    public static void SetTitleContent(DependencyObject d, object? value) => d.SetValue(TitleContentProperty, value);

    public static string? GetWindowAction(DependencyObject d) => (string?)d.GetValue(WindowActionProperty);
    public static void SetWindowAction(DependencyObject d, string? value) => d.SetValue(WindowActionProperty, value);

    private static void OnWindowActionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ButtonBase button) return;
        button.Click -= OnWindowActionClick;
        if (e.NewValue is string) button.Click += OnWindowActionClick;
    }

    private static void OnWindowActionClick(object sender, RoutedEventArgs e)
    {
        var button = (ButtonBase)sender;
        var window = Window.GetWindow(button);
        if (window == null) return;
        switch (GetWindowAction(button))
        {
            case "Minimize":
                SystemCommands.MinimizeWindow(window);
                break;
            case "Maximize":
                if (window.WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(window);
                else SystemCommands.MaximizeWindow(window);
                break;
            case "Close":
                SystemCommands.CloseWindow(window);
                break;
        }
    }
}
