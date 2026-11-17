// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Text.Json;
using System.Text.Json.Serialization;
using PixelEditor.Core;
using PixelEditor.Core.Ops;

namespace PixelEditor.ViewModels;

/// <summary>Settings in settings.json and the autosave path. Read and write errors are not critical.</summary>
public sealed class AppSettings
{
    public List<string> Recent { get; set; } = new();
    public string Theme { get; set; } = "Dark";
    public string? LastRsiParent { get; set; }

    /// <summary>UI language code (ru, en or a file from the locales folder). Applied at startup.</summary>
    public string Language { get; set; } = "ru";

    /// <summary>Animation sheet layout templates: standard and your own (the sheettemplates folder).</summary>
    [JsonIgnore] public SheetTemplateLibrary SheetTemplates => new(Path.Combine(Directory, "sheettemplates"));
    public string? LastExportFolder { get; set; }

    /// <summary>The sheet layout template, delay (only one the user typed) and frame size last used in the animation sheet / RSI state / chat dialogs.</summary>
    public string? LastSheetTemplateId { get; set; }
    public double? LastSheetDelay { get; set; }
    public int LastSheetFrameWidth { get; set; } = 32;
    public int LastSheetFrameHeight { get; set; } = 32;

    /// <summary>New document size at startup (and the hint in the "New" window): the last chosen one is remembered.</summary>
    public int DefaultWidth { get; set; } = 64;
    public int DefaultHeight { get; set; } = 64;

    // the selected tool and its settings (restored at startup)
    public string ToolName { get; set; } = "Pencil";
    public double BrushSize { get; set; } = 1;
    public double ShapeThickness { get; set; } = 1;
    public bool FilledShapes { get; set; }
    public double FillTolerance { get; set; }
    public bool FillWholeLayer { get; set; }
    /// <summary>Useful default for painted pictures where neighbouring pixels differ slightly (0 selects only the exact color).</summary>
    public const double DefaultWandTolerance = 32;
    public double WandTolerance { get; set; } = DefaultWandTolerance;
    /// <summary>Settings schema revision: 0 = written before the wand default changed (those files stored WandTolerance 0).</summary>
    public int SettingsRevision { get; set; }
    public double EraserTolerance { get; set; } = DefaultWandTolerance;
    public bool EraserContiguous { get; set; } = true;
    public bool EraserFollow { get; set; }
    public bool WandContiguous { get; set; } = true;
    public bool WandFollow { get; set; }
    public string TextFont { get; set; } = "5x7";
    public double TextScale { get; set; } = 1;
    public string LastShape { get; set; } = "Line";
    public string LastSelection { get; set; } = "Select";
    /// <summary>Obsolete: rotation is always crisp now. Kept so old settings.json files still load.</summary>
    public bool RotationSmooth { get; set; } = true;
    public bool ShowPixelGrid { get; set; } = true;
    public bool NewLayerForObjects { get; set; } = true;

    /// <summary>Show the start screen when the editor starts without a file (an old settings.json without the field means true).</summary>
    public bool ShowStartScreen { get; set; } = true;

    /// <summary>Color section shows the HSV square (true) or the RGB sliders (false).</summary>
    public bool ColorPickerHsv { get; set; } = true;

    [JsonIgnore] public string Directory { get; private set; } = DefaultDirectory();
    [JsonIgnore] public string AutosavePath => Path.Combine(Directory, "autosave.pixproj");
    private string FilePath => Path.Combine(Directory, "settings.json");

    /// <summary>Palette library: system and your own (the palettes folder in the data folder).</summary>
    [JsonIgnore] public PaletteLibrary Palettes => new(Path.Combine(Directory, "palettes"));

    /// <summary>Processing template library: system and your own (the pipelines folder in the data folder).</summary>
    [JsonIgnore] public PipelineLibrary Templates => new(Path.Combine(Directory, "pipelines"));

    /// <summary>Data folder: the PIXED_DATA_DIR environment variable (for tests and portable use) or %AppData%\PixelEditor.</summary>
    public static string DefaultDirectory() => AppPaths.DataDirectory;

    /// <summary>Loads settings from a folder (%AppData%\PixelEditor by default). Tests pass a temporary folder.</summary>
    public static AppSettings Load(string? directory = null)
    {
        directory ??= DefaultDirectory();
        AppSettings result = new();
        try
        {
            var file = Path.Combine(directory, "settings.json");
            if (File.Exists(file))
                result = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(file)) ?? new AppSettings();
        }
        catch { /* broken settings are replaced by the defaults */ }
        // the "follow Windows" theme was removed from the UI: an old saved "System" (or any unknown value) becomes the default dark theme
        if (result.Theme is not ("Light" or "Dark")) result.Theme = "Dark";
        if (result.SettingsRevision < 1)
        {
            // older versions saved the wand tolerance 0 (exact color) on the first preference change
            if (result.WandTolerance <= 0) result.WandTolerance = DefaultWandTolerance;
            result.SettingsRevision = 1;
        }
        result.Directory = directory;
        return result;
    }

    public void Save()
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* do not interfere if there is no write permission */ }
    }

    public void AddRecent(string path)
    {
        var full = Path.GetFullPath(path);
        Recent.RemoveAll(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase));
        Recent.Insert(0, full);
        if (Recent.Count > 10) Recent.RemoveRange(10, Recent.Count - 10);
        Save();
    }

    public void DeleteAutosave()
    {
        try { if (File.Exists(AutosavePath)) File.Delete(AutosavePath); } catch { }
    }
}

/// <summary>
/// A template to run. Source is either a path to a JSON file (your own templates, a file from disk) or the JSON text itself (system ones):
/// both are understood by Pipeline.Load/PixelEngine.LoadPipeline through <see cref="TemplateSource"/>.
/// </summary>
public sealed record TemplateItem(string Name, string Source, bool IsSystem = false, string Description = "", string? Id = null)
{
    public static TemplateItem From(TemplateInfo t) => new(t.Name, t.FilePath ?? t.Json, t.IsSystem, t.Description, t.Id);
    public string Kind => IsSystem ? Loc.T("settings.system") : Loc.T("settings.custom");
}

public static class TemplateSource
{
    /// <summary>A template from a file path or from JSON text.</summary>
    public static Pipeline Load(string source)
    {
        var t = source.TrimStart();
        return t.StartsWith('{') || t.StartsWith('[') ? Pipeline.Parse(source) : Pipeline.Load(source);
    }
}

public static class SizeParser
{
    /// <summary>Parses "32x32", "32×32" or "32 32". A single number gives a square.</summary>
    public static bool TryParse(string? text, out int width, out int height)
    {
        width = height = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split(['x', 'X', '×', ' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1 && int.TryParse(parts[0], out width)) { height = width; return width >= 0; }
        return parts.Length == 2 && int.TryParse(parts[0], out width) && int.TryParse(parts[1], out height) && width >= 0 && height >= 0;
    }
}
