// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PixelEditor.Core.Ops;

/// <summary>A library template: system (built in) or user (a JSON file).</summary>
public sealed record TemplateInfo(
    string Id,
    string Name,
    string Description,
    string Json,
    bool IsSystem,
    string? FilePath)
{
    public int StepCount => CountOf("steps");
    public int CheckCount => CountOf("checks");

    /// <summary>The template's declared parameters (empty when it has none or the JSON does not parse).</summary>
    public IReadOnlyList<PipelineParam> Params
    {
        get { try { return Pipeline.Parse(Json).Params; } catch { return []; } }
    }

    private int CountOf(string key)
    {
        try
        {
            var node = JsonNode.Parse(Json);
            var array = node is JsonArray a && key == "steps" ? a : (node as JsonObject)?[key] as JsonArray;
            return array?.Count ?? 0;
        }
        catch { return 0; }
    }
}

/// <summary>Standard templates. They cannot be changed or deleted, but you can make your own copy and edit it.</summary>
public static class BuiltInTemplates
{
    private static TemplateInfo Make(string id, string json)
    {
        var node = JsonNode.Parse(json)!.AsObject();
        // the name and description are translated into the current language in the JSON itself too (a template copy gets the same name)
        var name = CoreText.T(node["name"]!.GetValue<string>());
        var description = CoreText.T(node["description"]?.GetValue<string>() ?? "");
        node["name"] = name;
        if (node["description"] != null) node["description"] = description;
        if (node["params"] is JsonArray plist)
            foreach (var pn in plist.OfType<JsonObject>())
            {
                foreach (var key in new[] { "label", "hint" })
                    if (pn[key]?.GetValue<string>() is { } text) pn[key] = CoreText.T(text);
                if (pn["choices"] is JsonArray cl)
                    foreach (var c in cl.OfType<JsonObject>())
                        if (c["label"]?.GetValue<string>() is { } cl2) c["label"] = CoreText.T(cl2);
            }
        var translated = node.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        return new TemplateInfo("system:" + id, name, description, translated, IsSystem: true, FilePath: null);
    }

    /// <summary>The list is rebuilt on every access: names and descriptions are always in the current language.</summary>
    public static IReadOnlyList<TemplateInfo> All => Build();

    private static IReadOnlyList<TemplateInfo> Build() =>
    [
        Make("photo-to-pixel-art", """
            {
              "name": "Фото в пиксель-арт",
              "description": "Любая картинка в пиксель-арт: при желании убрать фон, уменьшить с сохранением краёв, свести к N цветам по восприятию, почистить мусор, добавить контур. Результат проверяется.",
              "params": [
                { "name": "size", "type": "int", "default": 64, "min": 8, "max": 512, "label": "Размер", "hint": "Длинная сторона результата в пикселях. Меньшую картинку не увеличивает." },
                { "name": "colors", "type": "int", "default": 16, "min": 2, "max": 64, "label": "Цветов", "hint": "Сколько цветов оставить." },
                { "name": "background", "type": "choice", "default": "keep", "label": "Фон", "hint": "Оставить как есть, убрать автоматически (однотонный и плавный фон) или вырезать предмет со сложного фона.",
                  "choices": [ { "value": "keep", "label": "Оставить" }, { "value": "auto", "label": "Убрать" }, { "value": "complex", "label": "Убрать (сложный фон)" } ] },
                { "name": "outline", "type": "choice", "default": "none", "label": "Контур", "hint": "Обводка снаружи: чёрная или тёмный оттенок соседнего цвета.",
                  "choices": [ { "value": "none", "label": "Без контура" }, { "value": "black", "label": "Чёрный" }, { "value": "colored", "label": "Цветной" } ] }
              ],
              "steps": [
                { "op": "removeBackground", "when": "$background==auto" },
                { "op": "cutout", "when": "$background==complex" },
                { "op": "alphaThreshold", "value": 128 },
                { "op": "cleanEdges", "specks": true, "fillGaps": true, "when": "$background!=keep" },
                { "op": "trim" },
                { "op": "adjust", "contrast": 10, "saturation": 12 },
                { "op": "resize", "width": "$size", "height": "$size", "keepAspect": true, "method": "smart", "noUpscale": true },
                { "op": "unsharp", "radius": 1, "amount": 0.6 },
                { "op": "alphaThreshold", "value": 128 },
                { "op": "quantize", "colors": "$colors", "space": "lab" },
                { "op": "mergeRareColors", "minCount": 2 },
                { "op": "despeckle", "maxArea": 2, "diagonal": false },
                { "op": "removeIslands", "maxSize": 2 },
                { "op": "outline", "color": "#000000", "mode": "outside", "diagonal": false, "when": "$outline==black" },
                { "op": "outline", "shade": true, "darken": 0.6, "mode": "outside", "diagonal": false, "when": "$outline==colored" }
              ],
              "checks": [
                { "check": "maxColors", "value": "$colors", "when": "$outline==none" },
                { "check": "maxColors", "value": "$colors+1", "when": "$outline==black" },
                { "check": "maxColors", "value": "$colors*2", "when": "$outline==colored" },
                { "check": "size", "maxWidth": "$size", "maxHeight": "$size", "when": "$outline==none" },
                { "check": "size", "maxWidth": "$size+2", "maxHeight": "$size+2", "when": "$outline!=none" },
                { "check": "binaryAlpha" },
                { "check": "noIslands", "maxSize": 2 }
              ]
            }
            """),
        Make("downscale-pixel-art", """
            {
              "name": "Уменьшить пиксель-арт",
              "description": "Вписывает картинку в рамку size×size, выбирая самый частый цвет в блоке: новые цвета не появляются, края остаются чёткими. Для пиксель-арта, который был увеличен, и для скриншотов.",
              "params": [
                { "name": "size", "type": "int", "default": 64, "min": 1, "max": 1024, "label": "Размер", "hint": "Длинная сторона результата в пикселях. Меньшую картинку не увеличивает." },
                { "name": "method", "type": "choice", "default": "mode", "label": "Способ", "hint": "Самый частый цвет в блоке или ближайший пиксель.",
                  "choices": [ { "value": "mode", "label": "Частый цвет" }, { "value": "nearest", "label": "Ближайший пиксель" } ] }
              ],
              "steps": [
                { "op": "alphaThreshold", "value": 128 },
                { "op": "resize", "width": "$size", "height": "$size", "keepAspect": true, "method": "$method", "noUpscale": true },
                { "op": "alphaThreshold", "value": 128 }
              ],
              "checks": [
                { "check": "size", "maxWidth": "$size", "maxHeight": "$size" },
                { "check": "binaryAlpha" }
              ]
            }
            """),
        Make("remove-background", """
            {
              "name": "Убрать фон",
              "description": "Делает фон прозрачным: однотонный или плавный фон убирается сам (цвет берётся с краёв), для сложного фона есть вырезание предмета. Затем чистит одиночные точки и щели на стыке фона и рисунка.",
              "params": [
                { "name": "subject", "type": "choice", "default": "auto", "label": "Фон", "hint": "Обычный: однотонный, градиент, мягкая тень. Сложный: текстура, трава, кирпич (предмет вырезается по отличию от рамки).",
                  "choices": [ { "value": "auto", "label": "Обычный" }, { "value": "complex", "label": "Сложный" } ] },
                { "name": "simplify", "type": "int", "default": 0, "min": 0, "max": 32, "label": "Свести цвета (0 = выкл.)", "hint": "Для рисованных и текстурных картинок: фон ищется на копии, сведённой к N цветам (обычно 8-12), а убирается из исходных пикселей. Не нужно для фото с плавным градиентом." },
                { "name": "enclosed", "type": "choice", "default": "auto", "label": "Замкнутые куски фона", "hint": "Фон, который предмет окружил со всех сторон (небо между ногами, внутри поводьев). Авто: крупные, а в режиме «Свести цвета» и мелкие; все: любые.",
                  "choices": [ { "value": "auto", "label": "Авто" }, { "value": "off", "label": "Не убирать" }, { "value": "all", "label": "Убирать все" } ] },
                { "name": "clean", "type": "bool", "default": true, "label": "Чистить края", "hint": "Убрать одиночные пиксели и закрыть щели на краю предмета." },
                { "name": "trim", "type": "bool", "default": false, "label": "Обрезать пустые поля", "hint": "Обрезать холст по предмету." }
              ],
              "steps": [
                { "op": "removeBackground", "simplify": "$simplify", "enclosed": "$enclosed", "when": "$subject==auto" },
                { "op": "cutout", "simplify": "$simplify", "enclosed": "$enclosed", "when": "$subject==complex" },
                { "op": "alphaThreshold", "value": 128 },
                { "op": "cleanEdges", "specks": true, "fillGaps": true, "when": "$clean" },
                { "op": "removeIslands", "maxSize": 1, "when": "$clean" },
                { "op": "trim", "when": "$trim" }
              ],
              "checks": [
                { "check": "binaryAlpha" },
                { "check": "noIslands", "maxSize": 1, "when": "$clean" },
                { "check": "opaquePixels", "min": 1 }
              ]
            }
            """),
        Make("remove-outline", """
            {
              "name": "Убрать контур",
              "description": "Снимает внешнюю обводку спрайта (цвет берётся с края, подходит и для рваного контура). Режим «тёмный» снимает любой тёмный контур независимо от цвета.",
              "params": [
                { "name": "dark", "type": "bool", "default": false, "label": "Любой тёмный", "hint": "Снимать все тёмные пиксели на краю, а не только самый частый цвет края." },
                { "name": "passes", "type": "int", "default": 3, "min": 1, "max": 8, "label": "Слоёв", "hint": "Сколько слоёв контура снять самое большее (останавливается раньше, когда подходящих пикселей на краю нет)." }
              ],
              "steps": [
                { "op": "alphaThreshold", "value": 128 },
                { "op": "removeOutline", "passes": "$passes", "dark": "$dark" },
                { "op": "removeIslands", "maxSize": 1 }
              ],
              "checks": [
                { "check": "binaryAlpha" },
                { "check": "noIslands", "maxSize": 1 },
                { "check": "opaquePixels", "min": 1 }
              ]
            }
            """),
        Make("clean-sprite", """
            {
              "name": "Почистить спрайт",
              "description": "Убирает мусор у готового пиксель-арта: одиночные пиксели, дырки, редкие оттенки и (по желанию) цветовую крошку. Размер не меняется. 0 выключает пункт.",
              "params": [
                { "name": "islands", "type": "int", "default": 1, "min": 0, "max": 8, "label": "Мусор до (px)", "hint": "Удалить отдельные группы пикселей размером до N. 0: не удалять." },
                { "name": "holes", "type": "int", "default": 1, "min": 0, "max": 8, "label": "Дырки до (px)", "hint": "Закрасить замкнутые дырки размером до N. 0: не трогать." },
                { "name": "rareColors", "type": "int", "default": 0, "min": 0, "max": 20, "label": "Редкие цвета", "hint": "Слить цвета, у которых меньше N пикселей, с ближайшим частым (2-4 для шумных картинок; выше 1 съедает блики из одного пикселя). 0: не сливать." },
                { "name": "speckle", "type": "int", "default": 0, "min": 0, "max": 6, "label": "Крошка до (px)", "hint": "Для оцифрованных картинок: перекрасить пятна одного цвета площадью до N в цвет соседей. 0: выключено." }
              ],
              "steps": [
                { "op": "alphaThreshold", "value": 128 },
                { "op": "removeIslands", "maxSize": "$islands", "when": "$islands" },
                { "op": "fillHoles", "maxSize": "$holes", "when": "$holes" },
                { "op": "despeckle", "maxArea": "$speckle", "diagonal": false, "when": "$speckle" },
                { "op": "mergeRareColors", "minCount": "$rareColors", "when": "$rareColors" }
              ],
              "checks": [
                { "check": "binaryAlpha" },
                { "check": "noIslands", "maxSize": "$islands", "when": "$islands" },
                { "check": "noHoles", "maxSize": "$holes", "when": "$holes" },
                { "check": "noRareColors", "minCount": "$rareColors", "when": "$rareColors" }
              ]
            }
            """),
        Make("apply-palette", """
            {
              "name": "Привести к палитре",
              "description": "Заменяет каждый цвет ближайшим из палитры библиотеки (PICO-8, Game Boy, свои палитры). Результат проверяется: других цветов нет.",
              "params": [
                { "name": "palette", "type": "palette", "default": "PICO-8", "label": "Палитра", "hint": "Название палитры из библиотеки." },
                { "name": "dither", "type": "bool", "default": false, "label": "Дизеринг", "hint": "Растушёвка между цветами палитры (ретро-вид, шумнее)." },
                { "name": "perceptual", "type": "bool", "default": true, "label": "По восприятию", "hint": "Подбирать ближайший цвет в Lab, а не в RGB: ближе к тому, как цвета видит глаз." }
              ],
              "steps": [
                { "op": "alphaThreshold", "value": 128 },
                { "op": "applyPalette", "paletteName": "$palette", "dither": "$dither", "space": "lab", "when": "$perceptual" },
                { "op": "applyPalette", "paletteName": "$palette", "dither": "$dither", "space": "rgb", "when": "!$perceptual" }
              ],
              "checks": [
                { "check": "onlyPalette", "paletteName": "$palette" },
                { "check": "binaryAlpha" }
              ]
            }
            """),
        Make("prepare-for-game", """
            {
              "name": "Подготовить к игре",
              "description": "Финальная подготовка спрайта: чёткая прозрачность, обрезка пустых полей, размер кратный N или фиксированная ячейка. Проверяет число цветов и отсутствие мусора.",
              "params": [
                { "name": "trim", "type": "bool", "default": true, "label": "Обрезать поля", "hint": "Убрать пустые края вокруг спрайта." },
                { "name": "fit", "type": "choice", "default": "multiple", "label": "Размер холста", "hint": "Не менять; сделать кратным N (холст растёт); вписать в ячейку N×N (холст растёт, но спрайт не обрезается: если он больше ячейки, проверка не пройдёт).",
                  "choices": [ { "value": "none", "label": "Не менять" }, { "value": "multiple", "label": "Кратный N" }, { "value": "cell", "label": "Ячейка N×N" } ] },
                { "name": "cell", "type": "int", "default": 16, "min": 1, "max": 1024, "label": "N", "hint": "Кратность или размер ячейки в пикселях." },
                { "name": "anchor", "type": "choice", "default": "center", "label": "Где спрайт", "hint": "Куда поставить спрайт в расширенном холсте.",
                  "choices": [ { "value": "center", "label": "По центру" }, { "value": "bottom", "label": "Снизу по центру" }, { "value": "topleft", "label": "Слева сверху" } ] },
                { "name": "maxColors", "type": "int", "default": 32, "min": 2, "max": 256, "label": "Цветов не больше", "hint": "Предел для проверки." }
              ],
              "steps": [
                { "op": "alphaThreshold", "value": 128 },
                { "op": "trim", "when": "$trim" },
                { "op": "resizeCanvas", "multipleOf": "$cell", "anchor": "$anchor", "when": "$fit==multiple" },
                { "op": "resizeCanvas", "width": "$cell", "height": "$cell", "onlyGrow": true, "anchor": "$anchor", "when": "$fit==cell" }
              ],
              "checks": [
                { "check": "binaryAlpha" },
                { "check": "noIslands", "maxSize": 1 },
                { "check": "maxColors", "value": "$maxColors" },
                { "check": "size", "multipleOf": "$cell", "when": "$fit==multiple" },
                { "check": "size", "width": "$cell", "height": "$cell", "when": "$fit==cell" },
                { "check": "trimmed", "when": "$fit==none" }
              ]
            }
            """),
        Make("check-ready", """
            {
              "name": "Проверить готовый спрайт",
              "description": "Ничего не меняет, только проверяет: чёткая прозрачность, нет мусора и пустых краёв, число цветов и размер в пределах.",
              "params": [
                { "name": "maxColors", "type": "int", "default": 32, "min": 2, "max": 256, "label": "Цветов не больше", "hint": "Предел числа цветов." },
                { "name": "maxSize", "type": "int", "default": 256, "min": 1, "max": 8192, "label": "Размер не больше", "hint": "Предел ширины и высоты в пикселях." }
              ],
              "steps": [],
              "checks": [
                { "check": "binaryAlpha" },
                { "check": "noIslands", "maxSize": 1 },
                { "check": "trimmed", "required": false },
                { "check": "maxColors", "value": "$maxColors" },
                { "check": "size", "maxWidth": "$maxSize", "maxHeight": "$maxSize" }
              ]
            }
            """),
    ];

    /// <summary>Templates of earlier versions that are not listed any more but still run by their old name or id (see <see cref="TemplateAliases"/>).</summary>
    internal static IReadOnlyList<TemplateInfo> Legacy =>
    [
        Make("legacy-limit-16-colors", """
            {
              "name": "Ограничить 16 цветами",
              "description": "Сводит картинку к 16 цветам и убирает почти одинаковые оттенки.",
              "steps": [
                { "op": "alphaThreshold", "value": 128 },
                { "op": "quantize", "colors": 16 },
                { "op": "mergeRareColors", "minCount": 2 }
              ],
              "checks": [
                { "check": "maxColors", "value": 16 }
              ]
            }
            """),
        Make("legacy-outline-1px", """
            {
              "name": "Чёрный контур 1 px",
              "description": "Обводит спрайт чёрной линией снаружи (холст расширяется на 1 px с каждой стороны).",
              "steps": [
                { "op": "alphaThreshold", "value": 128 },
                { "op": "outline", "color": "#000000", "mode": "outside", "thickness": 1, "diagonal": false, "autoPad": true }
              ],
              "checks": [
                { "check": "binaryAlpha" }
              ]
            }
            """),
    ];
}

/// <summary>
/// Old built-in templates that were replaced by parameterized ones. Old names (Russian and English) and ids still resolve:
/// the alias names the new template and the parameter values that reproduce the old behavior.
/// </summary>
public static class TemplateAliases
{
    public sealed record Alias(string[] Names, string OldId, string TargetId, IReadOnlyDictionary<string, object?> Values);

    private static IReadOnlyDictionary<string, object?> V(params (string, object?)[] pairs) => pairs.ToDictionary(p => p.Item1, p => p.Item2);

    public static IReadOnlyList<Alias> All { get; } =
    [
        new(["Фото в спрайт 32×32", "Photo to a 32×32 sprite"], "photo-to-sprite", "photo-to-pixel-art",
            V(("size", 32), ("colors", 16), ("background", "keep"), ("outline", "black"))),
        new(["Фото в спрайт 128×128", "Photo to 128×128 sprite"], "photo-to-sprite-128", "photo-to-pixel-art",
            V(("size", 128), ("colors", 24), ("background", "complex"), ("outline", "colored"))),
        new(["Картинка из интернета в пиксель-арт 64 px", "Picture from the internet to 64 px pixel art"], "web-image-to-pixel-art", "photo-to-pixel-art",
            V(("size", 64), ("colors", 16), ("background", "complex"), ("outline", "colored"))),
        new(["Уменьшить до 64 px (пиксель-арт)", "Downscale to 64 px (pixel art)"], "downscale-64", "downscale-pixel-art",
            V(("size", 64), ("method", "mode"))),
        new(["Привести к палитре PICO-8", "Snap to the PICO-8 palette"], "palette-pico-8", "apply-palette",
            V(("palette", "PICO-8"), ("dither", false), ("perceptual", false))),
        new(["Стиль Game Boy", "Game Boy style"], "game-boy-look", "apply-palette",
            V(("palette", "Game Boy"), ("dither", true), ("perceptual", false))),
        new(["Убрать фон и почистить края", "Remove background and clean edges"], "remove-background", "remove-background",
            V(("subject", "auto"), ("clean", true))),
        new(["Ограничить 16 цветами", "Limit to 16 colors"], "limit-16-colors", "legacy-limit-16-colors", V()),
        new(["Чёрный контур 1 px", "Black outline 1 px"], "outline-1px", "legacy-outline-1px", V()),
    ];

    /// <summary>The alias for an old name or id (the id with or without the "system:" prefix); null when it is not an old built-in.</summary>
    public static Alias? Find(string nameOrId)
    {
        var key = nameOrId.Trim();
        if (key.StartsWith("system:", StringComparison.OrdinalIgnoreCase)) key = key["system:".Length..];
        return All.FirstOrDefault(a => a.OldId.Equals(key, StringComparison.OrdinalIgnoreCase) ||
                                       a.Names.Any(n => n.Equals(nameOrId.Trim(), StringComparison.CurrentCultureIgnoreCase)));
    }
}

/// <summary>
/// Template library: system ones (BuiltInTemplates) plus your own in a folder (one JSON file per template).
/// Your own can be created, changed and deleted; system ones cannot. A template by name is available from the CLI and to an AI too.
/// </summary>
public sealed class PipelineLibrary
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public PipelineLibrary(string userDirectory) => UserDirectory = userDirectory;

    public static PipelineLibrary Default => new(AppPaths.PipelinesDirectory);

    public string UserDirectory { get; }

    /// <summary>System ones first, then your own alphabetically. Files that do not parse as a template are skipped.</summary>
    public IReadOnlyList<TemplateInfo> All()
    {
        var list = new List<TemplateInfo>(BuiltInTemplates.All);
        if (Directory.Exists(UserDirectory))
        {
            var user = new List<TemplateInfo>();
            foreach (var file in Directory.GetFiles(UserDirectory, "*.json"))
            {
                var info = TryLoad(file);
                if (info != null) user.Add(info);
            }
            list.AddRange(user.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase));
        }
        return list;
    }

    public TemplateInfo? Find(string nameOrId) =>
        All().FirstOrDefault(t => t.Id.Equals(nameOrId, StringComparison.OrdinalIgnoreCase)) ??
        All().FirstOrDefault(t => CoreText.SameName(t.Name, nameOrId)) ??
        FindAlias(nameOrId)?.Template;

    /// <summary>Like <see cref="Find"/>, plus the preset values when the name is an old built-in alias (otherwise Preset is null).</summary>
    public (TemplateInfo Template, IReadOnlyDictionary<string, object?>? Preset)? FindWithPreset(string nameOrId)
    {
        var direct = All().FirstOrDefault(t => t.Id.Equals(nameOrId, StringComparison.OrdinalIgnoreCase)) ??
                     All().FirstOrDefault(t => CoreText.SameName(t.Name, nameOrId));
        if (direct != null) return (direct, null);
        var alias = FindAlias(nameOrId);
        return alias == null ? null : (alias.Value.Template, alias.Value.Values);
    }

    /// <summary>An old built-in name or id: the template that replaced it and the parameter values that reproduce the old behavior.</summary>
    public (TemplateInfo Template, IReadOnlyDictionary<string, object?> Values)? FindAlias(string nameOrId)
    {
        var alias = TemplateAliases.Find(nameOrId);
        if (alias == null) return null;
        var target = BuiltInTemplates.Legacy.Concat(BuiltInTemplates.All).FirstOrDefault(t => t.Id == "system:" + alias.TargetId);
        return target == null ? null : (target, alias.Values);
    }

    /// <summary>
    /// Saves your own template. json is the template body ({steps, checks} or an array of steps); the name and description are written into it.
    /// Unknown operations and checks are not accepted. With existingId it overwrites an existing own template.
    /// </summary>
    public TemplateInfo Save(string name, string description, string json, string? existingId = null)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0) throw new ArgumentException(CoreText.T("Введите название шаблона"));

        var parsed = Pipeline.Parse(json); // throws FormatException for a wrong shape
        var problems = parsed.Validate();
        if (problems.Count > 0) throw new ArgumentException(CoreText.T("Шаблон содержит ошибки: ") + string.Join("; ", problems));
        if (parsed.Steps.Count == 0 && parsed.CheckSteps.Count == 0)
            throw new ArgumentException(CoreText.T("В шаблоне нет ни шагов, ни проверок"));

        var clash = All().FirstOrDefault(t => t.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase) &&
                                              !string.Equals(t.Id, existingId, StringComparison.OrdinalIgnoreCase));
        if (clash != null)
            throw new ArgumentException(clash.IsSystem
                ? CoreText.F("Название «{0}» занято системным шаблоном, выберите другое", name)
                : CoreText.F("Шаблон «{0}» уже существует", name));

        Directory.CreateDirectory(UserDirectory);
        string? oldPath = null;
        if (existingId != null)
        {
            var existing = All().FirstOrDefault(t => t.Id.Equals(existingId, StringComparison.OrdinalIgnoreCase));
            if (existing is { IsSystem: true }) throw new InvalidOperationException(CoreText.T("Системный шаблон нельзя изменить"));
            oldPath = existing?.FilePath;
        }

        var body = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!;
        var root = new JsonObject { ["name"] = name, ["description"] = (description ?? "").Trim() };
        if (body is JsonArray steps)
        {
            root["steps"] = steps.DeepClone();
        }
        else
        {
            foreach (var (key, value) in body.AsObject())
                if (key is not ("name" or "description")) root[key] = value?.DeepClone();
        }

        var path = oldPath ?? UniquePath(name);
        File.WriteAllText(path, root.ToJsonString(Json));
        return TryLoad(path)!;
    }

    /// <summary>Deletes your own template. System ones are not deleted (returns false).</summary>
    public bool Delete(string id)
    {
        var info = All().FirstOrDefault(t => t.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (info == null || info.IsSystem || info.FilePath == null || !File.Exists(info.FilePath)) return false;
        File.Delete(info.FilePath);
        return true;
    }

    private string UniquePath(string name)
    {
        var baseName = PaletteLibrary.Slug(name);
        var path = Path.Combine(UserDirectory, baseName + ".json");
        for (int i = 2; File.Exists(path); i++) path = Path.Combine(UserDirectory, $"{baseName}-{i}.json");
        return path;
    }

    private static TemplateInfo? TryLoad(string file)
    {
        try
        {
            var text = File.ReadAllText(file);
            var pipeline = Pipeline.Parse(text, Path.GetDirectoryName(Path.GetFullPath(file)));
            var name = string.IsNullOrWhiteSpace(pipeline.Name) || pipeline.Name == "pipeline"
                ? Path.GetFileNameWithoutExtension(file)
                : pipeline.Name;
            return new TemplateInfo("user:" + Path.GetFileNameWithoutExtension(file), name, pipeline.Description ?? "", text,
                IsSystem: false, FilePath: file);
        }
        catch { return null; }
    }
}
