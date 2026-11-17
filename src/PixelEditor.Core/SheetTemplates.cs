// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PixelEditor.Core;

/// <summary>A sheet template frame: a name ("stand", "step 1") and a delay in seconds.</summary>
public sealed record SheetFrame(string Name, double Delay);

/// <summary>
/// An animation sheet layout template: row names (directions or animations) and frame names with delays.
/// For example "Character: stand and walk": rows S, N, E, W, frames "stand", "step 1", "step 2".
/// </summary>
public sealed record SheetTemplate(string Id, string Name, string Description, IReadOnlyList<string> Rows, IReadOnlyList<SheetFrame> Frames, bool IsSystem, string? FilePath)
{
    /// <summary>Rows are the standard RSI directions (1, 4 or 8 in the usual order): the template also fits RSI.</summary>
    public bool IsRsiCompatible => Rows.Count is 1 or 4 or 8 &&
        (Rows.Count == 1 || Rows.SequenceEqual(RsiLayout.DirectionNames(Rows.Count)));

    public string Summary => CoreText.F("{0} стр. × {1} кадр.", Rows.Count, Frames.Count);
}

public static class BuiltInSheetTemplates
{
    private static SheetTemplate Make(string id, string name, string description, string[] rows, params (string Name, double Delay)[] frames) =>
        new("system:" + id, CoreText.T(name), CoreText.T(description), rows.Select(CoreText.T).ToList(),
            frames.Select(f => new SheetFrame(CoreText.T(f.Name), f.Delay)).ToList(), true, null);

    private static readonly string[] Dirs4 = ["S", "N", "E", "W"];

    /// <summary>The list is rebuilt on every access: names are always in the current language.</summary>
    public static IReadOnlyList<SheetTemplate> All => Build();

    private static IReadOnlyList<SheetTemplate> Build() =>
    [
        Make("still", "Один кадр", "Просто неподвижная картинка: один вид, один кадр (предмет, иконка).", ["вид"], ("кадр", 1.0)),
        Make("still-4", "Стоит, 4 стороны", "Персонаж или мебель без анимации: юг, север, восток, запад.", Dirs4, ("стоит", 1.0)),
        Make("walk-4", "Ходьба, 4 стороны (стоит, шаг 1, шаг 2)", "Классика для персонажей: поза покоя и два шага на каждую сторону света.",
            Dirs4, ("стоит", 0.2), ("шаг 1", 0.2), ("шаг 2", 0.2)),
        Make("walk-4-cycle", "Ходьба, 4 стороны (цикл из 4 кадров)", "Стоит, шаг 1, стоит, шаг 2: плавная ходьба по кругу.",
            Dirs4, ("стоит", 0.15), ("шаг 1", 0.15), ("стоит 2", 0.15), ("шаг 2", 0.15)),
        Make("walk-8", "Ходьба, 8 сторон (стоит, шаг 1, шаг 2)", "То же, но с диагоналями.",
            ["S", "N", "E", "W", "SE", "SW", "NE", "NW"], ("стоит", 0.2), ("шаг 1", 0.2), ("шаг 2", 0.2)),
        Make("idle-breath", "Покой с дыханием (2 кадра)", "Лёгкое движение в покое: вдох и выдох.", Dirs4, ("вдох", 0.6), ("выдох", 0.6)),
        Make("attack-4", "Атака, 4 стороны (замах, удар, возврат)", "Три кадра удара на каждую сторону.", Dirs4, ("замах", 0.12), ("удар", 0.08), ("возврат", 0.15)),
        Make("door", "Дверь (закрыта, открывается, открыта)", "Один вид: закрыта, два кадра открывания, открыта.", ["вид"],
            ("закрыта", 1.0), ("открывается 1", 0.1), ("открывается 2", 0.1), ("открыта", 1.0)),
        Make("platformer", "Платформер (покой, бег, прыжок, падение)", "Строки это анимации, кадры внутри строки по 4.",
            ["idle", "run", "jump", "fall"], ("1", 0.12), ("2", 0.12), ("3", 0.12), ("4", 0.12)),
        Make("effect", "Эффект (6 кадров)", "Вспышка, взрыв, магия: один вид, шесть кадров.", ["вид"],
            ("1", 0.06), ("2", 0.06), ("3", 0.06), ("4", 0.06), ("5", 0.06), ("6", 0.06)),
    ];
}

/// <summary>Sheet template library: system ones plus your own in a folder (JSON: name, description, rows, frames[{name, delay}]).</summary>
public sealed class SheetTemplateLibrary
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public SheetTemplateLibrary(string userDirectory) => UserDirectory = userDirectory;

    public string UserDirectory { get; }

    public IReadOnlyList<SheetTemplate> All()
    {
        var list = new List<SheetTemplate>(BuiltInSheetTemplates.All);
        if (Directory.Exists(UserDirectory))
        {
            var user = new List<SheetTemplate>();
            foreach (var file in Directory.GetFiles(UserDirectory, "*.json"))
            {
                var t = TryLoad(file);
                if (t != null) user.Add(t);
            }
            list.AddRange(user.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase));
        }
        return list;
    }

    public SheetTemplate? Find(string nameOrId) =>
        All().FirstOrDefault(t => t.Id.Equals(nameOrId, StringComparison.OrdinalIgnoreCase)) ??
        All().FirstOrDefault(t => CoreText.SameName(t.Name, nameOrId));

    /// <summary>Saves your own template. The name is required and differs from the others; at least one row and one frame, names without repeats.</summary>
    public SheetTemplate Save(string name, string description, IEnumerable<string> rows, IEnumerable<SheetFrame> frames, string? existingId = null)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0) throw new ArgumentException(CoreText.T("Введите название шаблона"));
        var rowList = rows.Select(r => r.Trim()).Where(r => r.Length > 0).ToList();
        var frameList = frames.Select(f => f with { Name = f.Name.Trim() }).Where(f => f.Name.Length > 0).ToList();
        if (rowList.Count == 0) throw new ArgumentException(CoreText.T("Нужна хотя бы одна строка"));
        if (frameList.Count == 0) throw new ArgumentException(CoreText.T("Нужен хотя бы один кадр"));
        if (rowList.Select(r => r.ToLowerInvariant()).Distinct().Count() != rowList.Count) throw new ArgumentException(CoreText.T("Названия строк не должны повторяться"));
        if (frameList.Any(f => f.Delay <= 0 || f.Delay > 60)) throw new ArgumentException(CoreText.T("Задержка кадра от 0 до 60 секунд"));

        var clash = All().FirstOrDefault(t => t.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase) &&
                                              !string.Equals(t.Id, existingId, StringComparison.OrdinalIgnoreCase));
        if (clash != null)
            throw new ArgumentException(clash.IsSystem ? CoreText.F("Название «{0}» занято стандартным шаблоном", name) : CoreText.F("Шаблон «{0}» уже существует", name));

        Directory.CreateDirectory(UserDirectory);
        string? oldPath = existingId != null ? All().FirstOrDefault(t => t.Id.Equals(existingId, StringComparison.OrdinalIgnoreCase))?.FilePath : null;
        var path = oldPath ?? UniquePath(name);
        var node = new JsonObject
        {
            ["name"] = name,
            ["description"] = (description ?? "").Trim(),
            ["rows"] = new JsonArray(rowList.Select(r => (JsonNode?)JsonValue.Create(r)).ToArray()),
            ["frames"] = new JsonArray(frameList.Select(f => (JsonNode?)new JsonObject { ["name"] = f.Name, ["delay"] = Math.Round(f.Delay, 4) }).ToArray()),
        };
        File.WriteAllText(path, node.ToJsonString(Json));
        return TryLoad(path)!;
    }

    public bool Delete(string id)
    {
        var t = All().FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (t == null || t.IsSystem || t.FilePath == null || !File.Exists(t.FilePath)) return false;
        File.Delete(t.FilePath);
        return true;
    }

    private string UniquePath(string name)
    {
        var baseName = PaletteLibrary.Slug(name);
        var path = Path.Combine(UserDirectory, baseName + ".json");
        for (int i = 2; File.Exists(path); i++) path = Path.Combine(UserDirectory, $"{baseName}-{i}.json");
        return path;
    }

    private static SheetTemplate? TryLoad(string file)
    {
        try
        {
            var o = JsonNode.Parse(File.ReadAllText(file)) as JsonObject;
            var name = o?["name"]?.GetValue<string>();
            var rows = (o?["rows"] as JsonArray)?.Select(n => n!.GetValue<string>()).ToList();
            var frames = (o?["frames"] as JsonArray)?.Select(n => new SheetFrame(n!["name"]!.GetValue<string>(), n["delay"]?.GetValue<double>() ?? 0.1)).ToList();
            if (string.IsNullOrWhiteSpace(name) || rows is not { Count: > 0 } || frames is not { Count: > 0 }) return null;
            return new SheetTemplate("user:" + Path.GetFileName(file), name, o!["description"]?.GetValue<string>() ?? "", rows, frames, false, file);
        }
        catch { return null; }
    }
}
