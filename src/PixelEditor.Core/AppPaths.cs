// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
namespace PixelEditor.Core;

/// <summary>
/// Where the application keeps user data. The PIXED_DATA_DIR environment variable overrides the root
/// (for tests and portable use), otherwise %AppData%\PixelEditor.
/// </summary>
public static class AppPaths
{
    public static string DataDirectory
    {
        get
        {
            var overridden = Environment.GetEnvironmentVariable("PIXED_DATA_DIR");
            return !string.IsNullOrWhiteSpace(overridden)
                ? overridden
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PixelEditor");
        }
    }

    public static string PalettesDirectory => Path.Combine(DataDirectory, "palettes");
    public static string PipelinesDirectory => Path.Combine(DataDirectory, "pipelines");
    public static string PluginsDirectory => Path.Combine(DataDirectory, "plugins");
}
