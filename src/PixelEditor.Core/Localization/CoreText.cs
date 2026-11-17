// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace PixelEditor.Core;

/// <summary>
/// Core texts (errors, descriptions of operations and checks, built-in templates and palettes, CLI and MCP help).
/// The source string in code is Russian; the English translation lives in the resource Locales/core.en.json (key: the Russian string).
/// Language: the PIXED_LANG environment variable (ru or en), English by default (for AI and the CLI); the editor sets the UI language
/// through <see cref="Language"/>. No translation: the source string is returned.
/// </summary>
public static class CoreText
{
    private static readonly object Gate = new();
    private static Dictionary<string, string>? _en;       // Russian string -> English
    private static Dictionary<string, string>? _reverse;  // English -> Russian
    private static string _language = Normalize(Environment.GetEnvironmentVariable("PIXED_LANG"));
    private static readonly AsyncLocal<string?> Scoped = new();

    private static string Normalize(string? code) => string.IsNullOrWhiteSpace(code) ? "en" : code.Trim().ToLowerInvariant();

    /// <summary>Language code: ru returns the source strings, any other takes the English translation.</summary>
    public static string Language
    {
        get => Scoped.Value ?? _language;
        set => _language = Normalize(value);
    }

    /// <summary>Temporarily changes the language only for the current call chain (tests, parallel requests); Dispose restores the previous one.</summary>
    public static IDisposable Scope(string language)
    {
        var previous = Scoped.Value;
        Scoped.Value = Normalize(language);
        return new ScopeToken(previous);
    }

    private sealed class ScopeToken(string? previous) : IDisposable
    {
        public void Dispose() => Scoped.Value = previous;
    }

    private static Dictionary<string, string> English
    {
        get
        {
            if (_en != null) return _en;
            lock (Gate)
            {
                if (_en != null) return _en;
                var map = new Dictionary<string, string>(StringComparer.Ordinal);
                try
                {
                    using var s = typeof(CoreText).Assembly.GetManifestResourceStream("PixelEditor.Core.Locales.core.en.json");
                    if (s != null)
                        foreach (var p in JsonSerializer.Deserialize<Dictionary<string, string>>(s) ?? new())
                            map[p.Key] = p.Value;
                }
                catch { /* without the table the source strings stay */ }
                _reverse = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in map) _reverse.TryAdd(p.Value, p.Key);
                return _en = map;
            }
        }
    }

    /// <summary>A string in the current language (the Russian source if the language is ru or there is no translation).</summary>
    public static string T(string source)
    {
        if (Language == "ru" || source.Length == 0) return source;
        var key = source.Contains('\r') ? source.Replace("\r\n", "\n") : source;   // multi-line texts (help) in the table with \n
        return English.TryGetValue(key, out var en) ? en : source;
    }

    /// <summary>Like <see cref="T"/>, then string.Format: a format with {0}, {1}... (from interpolation).</summary>
    public static string F(string source, params object?[] args) => string.Format(CultureInfo.CurrentCulture, T(source), args);

    /// <summary>
    /// Brings a name in any language to the Russian source (to find built-in templates and palettes by name:
    /// the same thing is found in Russian and in English). No match: returns the string as it is.
    /// </summary>
    public static string ToSource(string text)
    {
        _ = English;
        return _reverse != null && _reverse.TryGetValue(text, out var ru) ? ru : text;
    }

    /// <summary>Whether the name matches the given one in either language.</summary>
    public static bool SameName(string a, string b) =>
        string.Equals(a.Trim(), b.Trim(), StringComparison.CurrentCultureIgnoreCase) ||
        string.Equals(ToSource(a.Trim()), ToSource(b.Trim()), StringComparison.CurrentCultureIgnoreCase);
}
