// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using PixelEditor.Core;

namespace PixelEditor.ViewModels;

/// <summary>A UI language: the code (ru, en) and the name for the menu.</summary>
public sealed record LanguageInfo(string Code, string Name);

/// <summary>
/// Localization by keys. Tables: the built-in resources Locales/*.json (ru is the source of truth) and your own files
/// %AppData%\PixelEditor\locales\*.json (file name = language code; such a file extends or replaces the built-in one).
/// No key in a language: Russian is used, and if it is missing there too, the key itself is returned.
/// XAML uses the indexer (through the loc:T markup), view models use <see cref="T"/>.
/// When the language changes PropertyChanged("Item[]") is raised: bindings update without a restart.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public const string DefaultLanguage = "ru";
    public const string NameKey = "language.name";

    /// <summary>The shared application instance. The default language is Russian.</summary>
    public static Loc Instance { get; } = CreateInstance();

    private static Loc CreateInstance()
    {
        CoreText.Language = DefaultLanguage;   // core texts (templates, errors) follow the UI language
        return new Loc();
    }

    private readonly Dictionary<string, Dictionary<string, string>> _tables = new(StringComparer.OrdinalIgnoreCase);
    private readonly string? _userDirectory;
    private string _current = DefaultLanguage;

    /// <param name="userDirectory">The folder with your own languages; null: %AppData%\PixelEditor\locales (PIXED_DATA_DIR is honored).</param>
    public Loc(string? userDirectory = null)
    {
        _userDirectory = userDirectory ?? Path.Combine(AppPaths.DataDirectory, "locales");
        Reload();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? LanguageChanged;

    public string CurrentLanguage => _current;

    /// <summary>The languages found (built in and from the user folder), Russian first.</summary>
    public IReadOnlyList<LanguageInfo> Available =>
        _tables.Select(p => new LanguageInfo(p.Key, p.Value.TryGetValue(NameKey, out var n) && n.Length > 0 ? n : p.Key))
            .OrderBy(l => l.Code == DefaultLanguage ? 0 : 1).ThenBy(l => l.Code, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>The keys of a language table (to check translation completeness).</summary>
    public IReadOnlyCollection<string> Keys(string language) =>
        _tables.TryGetValue(language, out var t) ? t.Keys : Array.Empty<string>();

    /// <summary>Text by key for XAML bindings.</summary>
    public string this[string key] => Get(key);

    /// <summary>Text by key with string.Format substitution if arguments are passed.</summary>
    public string Get(string key, params object[] args)
    {
        string text;
        if (_tables.TryGetValue(_current, out var cur) && cur.TryGetValue(key, out var s)) text = s;
        else if (_tables.TryGetValue(DefaultLanguage, out var ru) && ru.TryGetValue(key, out var r)) text = r;
        else return key;
        if (args.Length == 0) return text;
        try { return string.Format(CultureInfo.CurrentCulture, text, args); }
        catch (FormatException) { return text; }
    }

    /// <summary>Text by key in the shared instance (for strings in view models).</summary>
    public static string T(string key, params object[] args) => Instance.Get(key, args);

    /// <summary>Switches the language. An unknown code is rejected (returns false), the language does not change.</summary>
    public bool SetLanguage(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) code = DefaultLanguage;
        var match = _tables.Keys.FirstOrDefault(k => string.Equals(k, code, StringComparison.OrdinalIgnoreCase));
        if (match == null) return false;
        if (match == _current) return true;
        _current = match;
        if (ReferenceEquals(this, Instance)) CoreText.Language = _current;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguage)));
        LanguageChanged?.Invoke();
        return true;
    }

    /// <summary>Rereads the tables (built in, then the user files).</summary>
    public void Reload()
    {
        _tables.Clear();
        var asm = typeof(Loc).Assembly;
        foreach (var res in asm.GetManifestResourceNames())
        {
            if (!res.StartsWith("Locales.", StringComparison.Ordinal) || !res.EndsWith(".json", StringComparison.Ordinal)) continue;
            var code = res["Locales.".Length..^".json".Length];
            using var stream = asm.GetManifestResourceStream(res);
            if (stream == null) continue;
            using var reader = new StreamReader(stream);
            Merge(code, reader.ReadToEnd());
        }
        try
        {
            if (_userDirectory != null && Directory.Exists(_userDirectory))
                foreach (var file in Directory.GetFiles(_userDirectory, "*.json"))
                    Merge(Path.GetFileNameWithoutExtension(file), File.ReadAllText(file));
        }
        catch { /* a broken user file does not interfere */ }
        if (!_tables.ContainsKey(DefaultLanguage)) _tables[DefaultLanguage] = new();
        if (!_tables.ContainsKey(_current)) _current = DefaultLanguage;
    }

    private void Merge(string code, string json)
    {
        try
        {
            var data = JsonSerializer.Deserialize<Dictionary<string, string>>(json,
                new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (data == null) return;
            if (!_tables.TryGetValue(code, out var table)) _tables[code] = table = new(StringComparer.Ordinal);
            foreach (var p in data) table[p.Key] = p.Value;
        }
        catch { /* skip an unintelligible file */ }
    }
}
