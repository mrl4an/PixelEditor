// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Text;
using System.Text.RegularExpressions;

namespace PixelEditor.Core;

/// <summary>An argument after resolving: <see cref="Value"/> is the text to use; <see cref="Expanded"/> is true when it was read from a file (@path) or stdin (-).</summary>
public sealed record ResolvedArg(string Value, bool Expanded, string? SourceFile)
{
    /// <summary>The folder of the @file the text came from (relative paths inside it resolve against it); null for stdin and plain arguments.</summary>
    public string? BaseDir => SourceFile == null ? null : Path.GetDirectoryName(Path.GetFullPath(SourceFile));
}

/// <summary>
/// Uniform "read this argument from elsewhere" forms for command-line arguments that carry JSON or text:
/// <c>@path</c> reads the argument from a file, <c>-</c> reads it from stdin (UTF-8, a BOM is tolerated).
/// Anything else is returned untouched, so plain paths, inline JSON and template names keep working as before.
/// It exists because PowerShell and cmd mangle the quotes of inline JSON.
/// </summary>
public sealed class InputArgs
{
    private readonly Func<TextReader> _stdin;
    private bool _stdinUsed;

    /// <param name="stdin">Stdin factory (tests inject a reader); null: the process standard input as UTF-8.</param>
    public InputArgs(Func<TextReader>? stdin = null) =>
        _stdin = stdin ?? (() => new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true));

    /// <summary>true for "-" and for "@something".</summary>
    public static bool IsSpecial(string? arg) => arg == "-" || (arg != null && arg.Length > 1 && arg[0] == '@');

    public ResolvedArg Resolve(string arg)
    {
        if (!IsSpecial(arg)) return new ResolvedArg(arg, false, null);
        if (arg == "-")
        {
            if (_stdinUsed) throw new ArgumentException(CoreText.T("Стандартный ввод (-) можно использовать только для одного аргумента команды"));
            _stdinUsed = true;
            using var reader = _stdin();
            return NonEmpty(arg, StripBom(reader.ReadToEnd()), null);
        }
        var path = arg[1..];
        if (!File.Exists(path))
            throw new FileNotFoundException(CoreText.F("Файл «{0}» не найден (аргумент {1})", path, arg), path);
        return NonEmpty(arg, ReadTextFile(path), path);
    }

    /// <summary>Reads a UTF-8 text file; a BOM is dropped.</summary>
    public static string ReadTextFile(string path) => StripBom(File.ReadAllText(path, new UTF8Encoding(false)));

    private static string StripBom(string s) => s.Length > 0 && s[0] == '﻿' ? s[1..] : s;

    private static ResolvedArg NonEmpty(string arg, string text, string? file) =>
        string.IsNullOrWhiteSpace(text)
            ? throw new ArgumentException(CoreText.F("Аргумент {0} пуст", arg))
            : new ResolvedArg(text, true, file);
}

/// <summary>
/// Expands <c>*</c> and <c>?</c> in the file-name part of an argument (Windows shells do not expand globs). Natural order: frame2 before frame10.
/// An argument without wildcards is returned as is; a pattern that matches nothing is an error.
/// </summary>
public static class FileGlob
{
    private static int SeparatorIndex(string s) => s.LastIndexOfAny(['/', '\\']);

    private static bool Has(string s) => s.AsSpan().IndexOfAny('*', '?') >= 0;

    /// <summary>true if the file-name part of the argument has * or ?.</summary>
    public static bool HasWildcard(string arg) => Has(arg[(SeparatorIndex(arg) + 1)..]);

    public static List<string> Expand(IEnumerable<string> args)
    {
        var result = new List<string>();
        foreach (var a in args) result.AddRange(ExpandOne(a));
        return result;
    }

    public static IReadOnlyList<string> ExpandOne(string arg)
    {
        if (!HasWildcard(arg)) return [arg];
        int sep = SeparatorIndex(arg);
        var dir = sep < 0 ? "" : arg[..(sep + 1)];
        var pattern = arg[(sep + 1)..];
        if (Has(dir)) throw new ArgumentException(CoreText.F("Подстановочные знаки * и ? допустимы только в имени файла: {0}", arg));
        var searchDir = dir.Length == 0 ? "." : dir;
        if (!Directory.Exists(searchDir)) throw new DirectoryNotFoundException(CoreText.F("Папка не найдена: {0}", searchDir));

        var regex = new Regex("^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);
        var names = Directory.EnumerateFiles(searchDir).Select(Path.GetFileName).Where(n => n != null && regex.IsMatch(n)).Select(n => n!).ToList();
        if (names.Count == 0) throw new FileNotFoundException(CoreText.F("По шаблону «{0}» не найдено ни одного файла", arg));
        names.Sort(NaturalCompare);
        return names.Select(n => dir + n).ToList();
    }

    /// <summary>Case-insensitive comparison where digit runs compare as numbers (frame2 &lt; frame10).</summary>
    public static int NaturalCompare(string? a, string? b)
    {
        a ??= ""; b ??= "";
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
            {
                int si = i, sj = j;
                while (i < a.Length && char.IsDigit(a[i])) i++;
                while (j < b.Length && char.IsDigit(b[j])) j++;
                var na = a[si..i].TrimStart('0'); var nb = b[sj..j].TrimStart('0');
                int c = na.Length != nb.Length ? na.Length.CompareTo(nb.Length) : string.CompareOrdinal(na, nb);
                if (c != 0) return c;
            }
            else
            {
                int c = char.ToUpperInvariant(a[i]).CompareTo(char.ToUpperInvariant(b[j]));
                if (c != 0) return c;
                i++; j++;
            }
        }
        int rest = (a.Length - i).CompareTo(b.Length - j);
        return rest != 0 ? rest : string.CompareOrdinal(a, b);
    }
}
