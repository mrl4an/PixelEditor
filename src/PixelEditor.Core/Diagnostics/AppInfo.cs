// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Reflection;

namespace PixelEditor.Core;

/// <summary>Program information: name, version, author, license. The version is taken from the assembly (Directory.Build.props).</summary>
public static class AppInfo
{
    public const string Name = "Pixel Editor";
    public const string Author = "mrl4an";
    public const string License = "GPL-3.0-or-later";
    public const string Copyright = "Copyright (C) 2026 mrl4an";

    /// <summary>A version like 1.0.0 (without the service part after "+").</summary>
    public static string Version
    {
        get
        {
            var asm = typeof(AppInfo).Assembly;
            var text = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? asm.GetName().Version?.ToString(3) ?? "0.0.0";
            int plus = text.IndexOf('+');
            return plus >= 0 ? text[..plus] : text;
        }
    }

    /// <summary>One line for --version and the About window (always in English).</summary>
    public static string Banner => $"{Name} {Version} ({License}) {Copyright}";
}
