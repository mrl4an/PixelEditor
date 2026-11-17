// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Reflection;
using PixelEditor.Core.Ops;

namespace PixelEditor.Core.Plugins;

/// <summary>A custom processing operation. The class must be public, not abstract, with a public parameterless constructor.</summary>
public interface IPixelOperation
{
    string Name { get; }
    string Description { get; }
    IReadOnlyList<string> Parameters { get; }
    PixelImage Run(PixelImage input, OpArgs args);
}

/// <summary>A custom result check. The class requirements are the same as for IPixelOperation.</summary>
public interface IPixelCheck
{
    string Name { get; }
    string Description { get; }
    IReadOnlyList<string> Parameters { get; }
    CheckResult Run(PixelImage image, OpArgs args);
}

public sealed record PluginLoadInfo(string File, IReadOnlyList<string> Operations, IReadOnlyList<string> Checks, string? Error);

/// <summary>
/// Loads plugins from DLLs: any public class implementing IPixelOperation or IPixelCheck is registered in the registries.
/// A plugin runs with the application's rights: load only your own and reviewed DLLs.
/// </summary>
public static class PluginLoader
{
    private static readonly List<PluginLoadInfo> LoadedList = new();
    private static readonly HashSet<string> LoadedPaths = new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<PluginLoadInfo> Loaded => LoadedList;

    /// <summary>Standard folders: plugins next to the program and %AppData%\PixelEditor\plugins.</summary>
    public static IEnumerable<string> DefaultDirectories()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "plugins");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PixelEditor", "plugins");
    }

    public static void LoadDefaults()
    {
        foreach (var dir in DefaultDirectories()) LoadDirectory(dir);
    }

    public static IReadOnlyList<PluginLoadInfo> LoadDirectory(string dir)
    {
        var result = new List<PluginLoadInfo>();
        if (!Directory.Exists(dir)) return result;
        foreach (var file in Directory.GetFiles(dir, "*.dll").OrderBy(f => f))
        {
            var name = Path.GetFileName(file);
            // the editor's own dependencies sit next to it when a plugin is built: skip them
            if (name.StartsWith("PixelEditor.", StringComparison.OrdinalIgnoreCase)) continue;
            var full = Path.GetFullPath(file);
            if (!LoadedPaths.Add(full)) continue;

            PluginLoadInfo info;
            try
            {
                info = RegisterAssembly(Assembly.LoadFrom(full), name);
            }
            catch (Exception ex)
            {
                info = new PluginLoadInfo(name, [], [], ex.Message);
            }
            LoadedList.Add(info);
            result.Add(info);
        }
        return result;
    }

    public static PluginLoadInfo RegisterAssembly(Assembly asm, string? label = null)
    {
        var ops = new List<string>();
        var checks = new List<string>();
        var errors = new List<string>();
        var source = asm.GetName().Name ?? "plugin";

        Type[] types;
        try { types = asm.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray()!; errors.Add(CoreText.T("часть типов не загрузилась")); }

        foreach (var t in types)
        {
            if (!t.IsClass || t.IsAbstract || !t.IsPublic || t.GetConstructor(Type.EmptyTypes) == null) continue;
            try
            {
                if (typeof(IPixelOperation).IsAssignableFrom(t))
                {
                    var o = (IPixelOperation)Activator.CreateInstance(t)!;
                    Operations.Register(new OperationInfo(o.Name, o.Description, o.Parameters.ToArray(), o.Run, source));
                    ops.Add(o.Name);
                }
                if (typeof(IPixelCheck).IsAssignableFrom(t))
                {
                    var c = (IPixelCheck)Activator.CreateInstance(t)!;
                    Checks.Register(new CheckInfo(c.Name, c.Description, c.Parameters.ToArray(), c.Run, source));
                    checks.Add(c.Name);
                }
            }
            catch (Exception ex)
            {
                errors.Add($"{t.Name}: {ex.Message}");
            }
        }
        return new PluginLoadInfo(label ?? source, ops, checks, errors.Count == 0 ? null : string.Join("; ", errors));
    }
}
