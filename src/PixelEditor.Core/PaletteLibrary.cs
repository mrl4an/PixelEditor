// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PixelEditor.Core;

/// <summary>A library palette: system (built in, immutable) or user (a JSON file).</summary>
public sealed record PaletteInfo(
    string Id,
    string Name,
    string Description,
    IReadOnlyList<uint> Colors,
    bool IsSystem,
    string? FilePath);

/// <summary>Built-in palettes. They cannot be changed or deleted, but can be applied or saved as your own copy.</summary>
public static class BuiltInPalettes
{
    private static PaletteInfo Make(string id, string name, string description, params string[] hex) =>
        new("system:" + id, CoreText.T(name), CoreText.T(description),
            hex.Select(h => Pixel.TryParseHex(h, out var c) ? c : throw new FormatException(CoreText.F("Палитра {0}: неверный цвет {1}", name, h))).ToList(),
            IsSystem: true, FilePath: null);

    /// <summary>The list is rebuilt on every access: names and descriptions are always in the current language.</summary>
    public static IReadOnlyList<PaletteInfo> All => Build();

    private static IReadOnlyList<PaletteInfo> Build() =>
    [
        Make("pico-8", "PICO-8", "16 цветов фэнтези-консоли PICO-8: яркие, хорошо сочетаются. Хороший старт для любого спрайта.",
            "000000", "1D2B53", "7E2553", "008751", "AB5236", "5F574F", "C2C3C7", "FFF1E8",
            "FF004D", "FFA300", "FFEC27", "00E436", "29ADFF", "83769C", "FF77A8", "FFCCAA"),
        Make("dawnbringer-16", "DawnBringer 16", "Классическая палитра из 16 цветов (DawnBringer): мягкие, приглушённые тона, универсальна.",
            "140C1C", "442434", "30346D", "4E4A4E", "854C30", "346524", "D04648", "757161",
            "597DCE", "D27D2C", "8595A1", "6DAA2C", "D2AA99", "6DC2CA", "DAD45E", "DEEED6"),
        Make("sweetie-16", "Sweetie 16", "16 цветов (GrafxKid): сочные, тёплые, с хорошим контрастом для персонажей и интерфейса.",
            "1A1C2C", "5D275D", "B13E53", "EF7D57", "FFCD75", "A7F070", "38B764", "257179",
            "29366F", "3B5DC9", "41A6F6", "73EFF7", "F4F4F4", "94B0C2", "566C86", "333C57"),
        Make("endesga-32", "Endesga 32", "32 цвета (ENDESGA): богатая палитра для детальных спрайтов и сцен.",
            "BE4A2F", "D77643", "EAD4AA", "E4A672", "B86F50", "733E39", "3E2731", "A22633",
            "E43B44", "F77622", "FEAE34", "FEE761", "63C74D", "3E8948", "265C42", "193C3E",
            "124E89", "0099DB", "2CE8F5", "FFFFFF", "C0CBDC", "8B9BB4", "5A6988", "3A4466",
            "262B44", "181425", "FF0044", "68386C", "B55088", "F6757A", "E8B796", "C28569"),
        Make("game-boy", "Game Boy", "4 оттенка зелёного оригинального Game Boy. Для ретро-стиля.",
            "0F380F", "306230", "8BAC0F", "9BBC0F"),
        Make("grayscale-8", "Градации серого (8)", "8 ступеней серого от чёрного до белого. Для теней, масок и чернобелых спрайтов.",
            "000000", "242424", "494949", "6D6D6D", "929292", "B6B6B6", "DBDBDB", "FFFFFF"),
    ];
}

/// <summary>
/// Palette library: system ones (BuiltInPalettes) plus user ones in a folder (one JSON file per palette:
/// name, description, colors). User palettes can be created, changed and deleted; system ones cannot.
/// </summary>
public sealed class PaletteLibrary
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public PaletteLibrary(string userDirectory) => UserDirectory = userDirectory;

    /// <summary>The library in the standard application data folder.</summary>
    public static PaletteLibrary Default => new(AppPaths.PalettesDirectory);

    public string UserDirectory { get; }

    /// <summary>System ones first, then your own alphabetically. Corrupted files are skipped.</summary>
    public IReadOnlyList<PaletteInfo> All()
    {
        var list = new List<PaletteInfo>(BuiltInPalettes.All);
        if (Directory.Exists(UserDirectory))
        {
            var user = new List<PaletteInfo>();
            foreach (var file in Directory.GetFiles(UserDirectory, "*.json"))
            {
                var info = TryLoad(file);
                if (info != null) user.Add(info);
            }
            list.AddRange(user.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase));
        }
        return list;
    }

    /// <summary>Finds by name (case-insensitive) or by id.</summary>
    public PaletteInfo? Find(string nameOrId) =>
        All().FirstOrDefault(p => p.Id.Equals(nameOrId, StringComparison.OrdinalIgnoreCase)) ??
        All().FirstOrDefault(p => CoreText.SameName(p.Name, nameOrId));

    /// <summary>
    /// Saves your own palette. With existingId it overwrites (and renames the file if the name changed).
    /// The name is required and must not match another palette, there must be at least one color.
    /// </summary>
    public PaletteInfo Save(string name, string description, IEnumerable<uint> colors, string? existingId = null)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0) throw new ArgumentException(CoreText.T("Введите название палитры"));
        var list = colors.Where(c => !Pixel.IsEmpty(c)).Select(c => c | 0xFF000000u).Distinct().ToList();
        if (list.Count == 0) throw new ArgumentException(CoreText.T("В палитре нет ни одного цвета"));

        var clash = All().FirstOrDefault(p => p.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase) &&
                                              !string.Equals(p.Id, existingId, StringComparison.OrdinalIgnoreCase));
        if (clash != null)
            throw new ArgumentException(clash.IsSystem
                ? CoreText.F("Название «{0}» занято системной палитрой, выберите другое", name)
                : CoreText.F("Палитра «{0}» уже существует", name));

        Directory.CreateDirectory(UserDirectory);
        string? oldPath = null;
        if (existingId != null)
        {
            var existing = All().FirstOrDefault(p => p.Id.Equals(existingId, StringComparison.OrdinalIgnoreCase));
            if (existing is { IsSystem: true }) throw new InvalidOperationException(CoreText.T("Системную палитру нельзя изменить"));
            oldPath = existing?.FilePath;
        }

        var path = oldPath ?? UniquePath(name);
        var node = new JsonObject
        {
            ["name"] = name,
            ["description"] = (description ?? "").Trim(),
            ["colors"] = new JsonArray(list.Select(c => (JsonNode?)JsonValue.Create(Pixel.ToHex(c))).ToArray()),
        };
        File.WriteAllText(path, node.ToJsonString(Json));
        return TryLoad(path)!;
    }

    /// <summary>Deletes your own palette. System ones are not deleted (returns false).</summary>
    public bool Delete(string id)
    {
        var info = All().FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (info == null || info.IsSystem || info.FilePath == null || !File.Exists(info.FilePath)) return false;
        File.Delete(info.FilePath);
        return true;
    }

    /// <summary>A safe file name from the title: letters and digits, everything else becomes a hyphen.</summary>
    public static string Slug(string name)
    {
        var chars = name.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        var slug = string.Join("-", new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
        return slug.Length == 0 ? "palette" : slug.Length > 60 ? slug[..60] : slug;
    }

    private string UniquePath(string name)
    {
        var baseName = Slug(name);
        var path = Path.Combine(UserDirectory, baseName + ".json");
        for (int i = 2; File.Exists(path); i++) path = Path.Combine(UserDirectory, $"{baseName}-{i}.json");
        return path;
    }

    private static PaletteInfo? TryLoad(string file)
    {
        try
        {
            var node = JsonNode.Parse(File.ReadAllText(file))?.AsObject();
            var name = node?["name"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(name)) return null;
            var colors = new List<uint>();
            foreach (var c in node!["colors"]?.AsArray() ?? [])
                if (Pixel.TryParseHex(c?.GetValue<string>(), out var color) && !Pixel.IsEmpty(color)) colors.Add(color);
            if (colors.Count == 0) return null;
            return new PaletteInfo("user:" + Path.GetFileNameWithoutExtension(file), name.Trim(),
                node["description"]?.GetValue<string>() ?? "", colors, IsSystem: false, FilePath: file);
        }
        catch { return null; }
    }
}
