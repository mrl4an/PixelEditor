// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Text.Json;

namespace PixelEditor.Core.Ops;

/// <summary>Pipeline step arguments (a JSON object) with safe getters.</summary>
public sealed class OpArgs(JsonElement obj, string? baseDir)
{
    public static OpArgs Empty { get; } = new(default, null);

    private bool TryGet(string name, out JsonElement value)
    {
        value = default;
        if (obj.ValueKind != JsonValueKind.Object) return false;
        foreach (var p in obj.EnumerateObject())
            if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) { value = p.Value; return p.Value.ValueKind != JsonValueKind.Null; }
        return false;
    }

    public bool Has(string name) => TryGet(name, out _);

    public JsonElement Object(string name) =>
        TryGet(name, out var value) && value.ValueKind == JsonValueKind.Object
            ? value : throw new ArgumentException(CoreText.F("Параметр '{0}' должен быть объектом.", name));

    public IReadOnlyList<(int X, int Y, uint Color)> PixelEdits(string name)
    {
        if (!TryGet(name, out var value) || value.ValueKind != JsonValueKind.Array)
            throw new ArgumentException(CoreText.F("Нужен параметр {0}: [[x, y, цвет], ...]", name));
        var edits = new List<(int, int, uint)>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() != 3 ||
                item[0].ValueKind != JsonValueKind.Number || !item[0].TryGetInt32(out int x) ||
                item[1].ValueKind != JsonValueKind.Number || !item[1].TryGetInt32(out int y) ||
                item[2].ValueKind != JsonValueKind.String || !Pixel.TryParseHex(item[2].GetString(), out uint color))
                throw new ArgumentException(CoreText.F("{0}: каждая правка это [x, y, цвет]", name));
            edits.Add((x, y, color));
        }
        return edits;
    }

    /// <summary>An array of numbers (for example [x, y, w, h]); false if the parameter is missing or is not an array of numbers.</summary>
    /// <summary>A list of points [[x, y], ...].</summary>
    public List<(int X, int Y)> Points(string name)
    {
        if (!TryGet(name, out var v) || v.ValueKind != JsonValueKind.Array) throw new ArgumentException(CoreText.F("Нужен параметр {0}: [[x,y], ...]", name));
        var list = new List<(int, int)>();
        foreach (var p in v.EnumerateArray())
        {
            if (p.ValueKind != JsonValueKind.Array || p.GetArrayLength() != 2) throw new ArgumentException(CoreText.F("{0}: каждая точка это [x, y]", name));
            list.Add(((int)Math.Round(p[0].GetDouble()), (int)Math.Round(p[1].GetDouble())));
        }
        return list;
    }

    public bool TryGetArray(string name, out List<int> values)
    {
        values = [];
        if (!TryGet(name, out var v) || v.ValueKind != JsonValueKind.Array) return false;
        foreach (var e in v.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Number) return false;
            values.Add((int)Math.Round(e.GetDouble()));
        }
        return true;
    }

    public int Int(string name, int def) =>
        TryGet(name, out var v) && v.ValueKind == JsonValueKind.Number ? (int)Math.Round(v.GetDouble()) : def;

    public double Double(string name, double def) =>
        TryGet(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : def;

    public bool Bool(string name, bool def) =>
        TryGet(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : def;

    public string? String(string name, string? def = null) =>
        TryGet(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : def;

    public uint Color(string name, uint def)
    {
        var s = String(name);
        if (s == null) return def;
        if (!Pixel.TryParseHex(s, out var c)) throw new ArgumentException(CoreText.F("Параметр '{0}': неверный цвет '{1}'", name, s));
        return c;
    }

    /// <summary>
    /// Palette: an array of hex strings in "palette", a library palette name in "paletteName" (for example "PICO-8" or your own)
    /// or a path to .hex/.gpl in "paletteFile" (relative to the pipeline file).
    /// </summary>
    public List<uint>? Palette()
    {
        var libraryName = String("paletteName");
        if (libraryName != null)
        {
            var found = PaletteLibrary.Default.Find(libraryName)
                        ?? throw new ArgumentException(CoreText.F("Палитра «{0}» не найдена в библиотеке", libraryName));
            return found.Colors.ToList();
        }

        if (TryGet("palette", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            var list = new List<uint>();
            foreach (var e in arr.EnumerateArray())
            {
                if (!Pixel.TryParseHex(e.GetString(), out var c) || Pixel.IsEmpty(c))
                    throw new ArgumentException(CoreText.F("Палитра: неверный цвет '{0}'", e));
                list.Add(c);
            }
            return list;
        }
        var file = String("paletteFile");
        if (file == null) return null;
        var path = Path.IsPathRooted(file) || baseDir == null ? file : Path.Combine(baseDir, file);
        return PaletteIO.Load(path);
    }
}

public sealed record OperationInfo(
    string Name,
    string description,
    string[] parameters,
    Func<PixelImage, OpArgs, PixelImage> Run,
    string Source = "builtin")
{
    /// <summary>Description in the current language (the source text in code is Russian, see CoreText).</summary>
    public string Description => CoreText.T(description);

    public string[] Parameters => parameters.Select(CoreText.T).ToArray();
}

/// <summary>The operation registry: shared by the editor, pipelines and the CLI (what an AI controls).</summary>
public static class Operations
{
    private static readonly Dictionary<string, OperationInfo> Map = Build();
    private static readonly object Gate = new();

    public static IEnumerable<OperationInfo> All { get { lock (Gate) return Map.Values.ToList(); } }

    public static OperationInfo Get(string name)
    {
        lock (Gate)
            return Map.TryGetValue(name, out var op)
                ? op
                : throw new ArgumentException(CoreText.F("Неизвестная операция '{0}'. Доступно: {1}", name, string.Join(", ", Map.Keys)));
    }

    public static void Register(OperationInfo op)
    {
        lock (Gate)
            if (!Map.TryAdd(op.Name, op))
                throw new ArgumentException(CoreText.F("Операция '{0}' уже существует", op.Name));
    }

    private static Dictionary<string, OperationInfo> Build()
    {
        var ops = new List<OperationInfo>
        {
            new("resize",
                "Изменить размер. method: box (усреднение, для фото), mode (самый частый цвет, для пиксель-арта), nearest.",
                ["width:int", "height:int", "scale:number (0.25 = в 4 раза меньше)", "keepAspect:bool=true (с width+height вписывает в рамку)", "method:string=box", "noUpscale:bool=false (не увеличивать: если картинка уже не больше цели, она остаётся как есть)"],
                Resize),
            new("alphaThreshold",
                "Бинаризовать прозрачность: альфа >= value становится непрозрачным пикселем, остальное пусто.",
                ["value:int=128"],
                (img, a) => Transform.BinarizeAlpha(img, a.Int("value", 128))),
            new("quantize",
                "Уменьшить число цветов до N. space: rgb (median cut) или lab (перцептивно: k-средних в Lab, акценты сохраняются лучше).",
                ["colors:int=16", "dither:bool=false", "space:string=rgb"],
                (img, a) =>
                {
                    bool lab = string.Equals(a.String("space", "rgb"), "lab", StringComparison.OrdinalIgnoreCase);
                    var pal = lab ? PerceptualQuantizer.BuildPalette(img, Math.Max(1, a.Int("colors", 16))) : Quantizer.BuildPalette(img, Math.Max(1, a.Int("colors", 16)));
                    if (pal.Count == 0) return img.Clone();
                    return lab ? PerceptualQuantizer.Apply(img, pal, a.Bool("dither", false)) : Quantizer.Apply(img, pal, a.Bool("dither", false));
                }),
            new("applyPalette",
                "Привести все цвета к ближайшим из палитры.",
                ["palette:string[] (hex)", "paletteFile:string (.hex/.gpl)", "dither:bool=false", "space:string=rgb (или lab)"],
                (img, a) =>
                {
                    var pal = a.Palette() ?? throw new ArgumentException(CoreText.T("applyPalette: нужен palette или paletteFile"));
                    if (pal.Count == 0) throw new ArgumentException(CoreText.T("applyPalette: палитра пуста"));
                    return string.Equals(a.String("space", "rgb"), "lab", StringComparison.OrdinalIgnoreCase)
                        ? PerceptualQuantizer.Apply(img, pal, a.Bool("dither", false))
                        : Quantizer.Apply(img, pal, a.Bool("dither", false));
                }),
            new("removeIslands",
                "Убрать мусор: связные группы пикселей размером <= maxSize.",
                ["maxSize:int=1", "diagonal:bool=true"],
                (img, a) => Cleanup.RemoveIslands(img, a.Int("maxSize", 1), a.Bool("diagonal", true))),
            new("fillHoles",
                "Закрасить замкнутые пустые дырки внутри фигуры цветом соседей.",
                ["maxSize:int=без ограничения"],
                (img, a) => Cleanup.FillHoles(img, a.Int("maxSize", int.MaxValue))),
            new("removeBackground",
                "Убрать фон (сделать прозрачным). Фон моделируется по краю картинки несколькими цветовыми кластерами и сравнивается по восприятию (Lab); допуск tolerance подбирается сам по разбросу цветов края, плавный градиент и шум фона заливка проходит сама (follow), а резкая граница предмета её останавливает. Антиалиасный ореол на краю убирается (defringe). По умолчанию стирается то, что связано с краем; замкнутый фон внутри предмета (между рукой и телом) стирается, если занимает не меньше 3% картинки (enclosed), поэтому белки глаз остаются. Пиксель-арт с точным цветом фона обрабатывается точно. Явный tolerance включает прежний режим без следования за градиентом (пока не задан follow); tolerance 0 это только точный цвет.",
                ["color:color=по краям", "tolerance:int=авто (допуск цвета от фона в единицах восприятия, примерно как шаг канала RGB; 0 только точный цвет)", "edgesOnly:bool=true", "follow:int=авто (фон-градиент и шум: допустимый шаг между соседями; 0 выключает; при заданном tolerance по умолчанию выключено)", "limit:int=90 (при follow: насколько фон может уйти от модели фона)", "defringe:bool=true (убрать антиалиасный ореол на краю предмета)", "enclosed:string=auto (замкнутый фон внутри предмета: off, auto, all)", "simplify:int=0 (0 выключено; N: маска считается на копии, сведённой к N цветам, и переносится на исходные пиксели: для рисованных и текстурных картинок, 6-12)", "denoise:int=0 (для simplify: радиус медианного фильтра копии)", "enclosedMin:int=0 (для simplify: наименьшая замкнутая область фона в пикселях, которую стирать; 0 это автоматически)"],
                (img, a) =>
                {
                    uint? color = a.Has("color") ? a.Color("color", Pixel.Empty) : null;
                    int? tolerance = a.Has("tolerance") ? a.Int("tolerance", 0) : null, follow = a.Has("follow") ? a.Int("follow", 0) : null;
                    bool edgesOnly = a.Bool("edgesOnly", true);
                    int limit = a.Int("limit", 90);
                    if (a.Int("simplify", 0) > 0)
                        return Background.RemoveViaSimplified(img, a.Int("simplify", 0), a.Int("denoise", 0), a.String("enclosed", "auto") ?? "auto", a.Int("enclosedMin", 0), a.Bool("defringe", true),
                            s => Background.Remove(s, color, tolerance, edgesOnly, follow, limit, false, "off"));
                    return Background.Remove(img, color, tolerance, edgesOnly, follow, limit, a.Bool("defringe", true), a.String("enclosed", "auto"));
                }),
            new("adjust",
                "Цветокоррекция: яркость, контраст, насыщенность (-100..100). Делай перед quantize/applyPalette: у фото после уменьшения цвета блёклые, контраст +15 и насыщенность +20 оживляют пиксель-арт.",
                ["brightness:number=0", "contrast:number=0", "saturation:number=0"],
                (img, a) => Adjust.Apply(img, a.Double("brightness", 0), a.Double("contrast", 0), a.Double("saturation", 0))),
            new("polygonMask",
                "Оставить только то, что внутри многоугольника (points: [[x,y],...] в координатах картинки), остальное прозрачное. Для фото, где фон похож на предмет и cutout не справляется: посмотри картинку и обведи предмет грубым контуром. keepOutside=true вырезает многоугольник.",
                ["points:[[x,y],...]", "keepOutside:bool=false"],
                (img, a) => ShapeMask.Apply(img, a.Points("points"), a.Bool("keepOutside", false))),
            new("light",
                "Освещение плоского спрайта в стиле пиксель-арта: силуэт считается выпуклой «подушкой» (высота растёт от края, внутренние цветовые области добавляют свои бугры), свет идёт с стороны from; освещённость режется на несколько полос (bands), каждая полоса это ступень палитры из собственного цвета пикселя со сдвигом оттенка (тени к синему, свет к жёлтому). Без градиентов: у каждого цвета несколько чётких оттенков. Тёмная обводка не трогается (keepOutline). Прозрачность и силуэт не меняются. С palette/paletteName новые цвета подбираются из палитры.",
                ["from:string=NW (N,S,E,W,NE,NW,SE,SW)", "strength:number=0.8 (размер ступеней цвета)", "bands:int=3 (2 тень и база; 3 тень, база, свет; 4 плюс глубокая тень; 5 плюс блик)", "depth:int=0 (на сколько пикселей от края поднимается объём; 0 это автоматически по размеру спрайта)", "keepOutline:bool=true (не трогать тёмную обводку)", "perRegion:number=0.2 (0..1: насколько внутренние цветовые области формируют объём)", "rim:bool=false (тонкая светлая линия на освещённом краю)", "elevation:number=40 (высота источника света над картинкой, градусы)", "dither:bool=false (дизеринг на границах полос)", "paletteName:string=нет (подбирать цвета из палитры)"],
                (img, a) => Lighting.Light(img, new LightOptions
                {
                    ToLight = Lighting.ToLight(a.String("from", "NW")),
                    Strength = Math.Clamp(a.Double("strength", 0.8), 0, 3),
                    Bands = a.Int("bands", 3),
                    Depth = a.Int("depth", 0),
                    KeepOutline = a.Bool("keepOutline", true),
                    PerRegion = a.Double("perRegion", 0.2),
                    Rim = a.Bool("rim", false),
                    Elevation = a.Double("elevation", 40),
                    Dither = a.Bool("dither", false),
                    Palette = a.Palette(),
                })),
            new("edgeShade",
                "Свет и тень по краям (прежнее имя, теперь то же освещение, что и light с тремя полосами): источник света с стороны from (NW сверху слева); край, обращённый к свету, светлеет (highlight), противоположный темнеет (shadow). depth 0 или 1 это автоматическая глубина объёма. Для новых работ используйте light.",
                ["from:string=NW (N,S,E,W,NE,NW,SE,SW)", "depth:int=1 (0 и 1: автоматически)", "highlight:number=0.2 (сила света, 0.2 нейтрально)", "shadow:number=0.25 (сила тени, 0.25 нейтрально)"],
                (img, a) => Lighting.EdgeShade(img, Lighting.ToLight(a.String("from", "NW")), a.Int("depth", 1), a.Double("highlight", 0.2), a.Double("shadow", 0.25))),
            new("cutout",
                "Вырезать предмет со сложного фона без нейросети: цвета фона берутся с рамки (до clusters штук; цвет, занимающий малую часть рамки, фоном не считается, поэтому предмет у края не съедается). Режим distance (по умолчанию): считается расстояние каждого пикселя до цветов фона в Lab, карта сглаживается, порог находится сам (или задайте threshold), фоном считается то, что ниже порога и связано с краем картинки; заливка сама идёт за плавным градиентом и шумом (follow), модель фона уточняется по найденному фону, пока результат не перестанет меняться, поэтому повторный запуск ничего не меняет. Антиалиасный ореол убирается (defringe), замкнутый фон внутри предмета стирается, если занимает не меньше 3% картинки (enclosed). Режим flood: заливка от краёв, не пересекающая границы предмета (edgeStop). Остаётся самая большая область (keepLargest). Для однотонного фона проще removeBackground.",
                ["clusters:int=4", "tolerance:int=30", "edgeStop:int=45 (0 выключает барьер границ)", "keepLargest:bool=true", "keepRect:int[]=нет ([x,y,w,h]: здесь точно предмет)", "mode:string=distance (distance: расстояние до цветов фона в Lab с автопорогом, устойчивее на фото; flood: заливка с барьером границ)", "threshold:int=0 (для distance: порог расстояния, 0 это автоматически)", "smooth:int=0 (для distance: радиус сглаживания карты расстояний; нужен только для очень шумного фона)", "follow:number=авто (для distance: допустимый шаг цвета Lab между соседями при следовании за градиентом и шумом; 0 выключает)", "defringe:bool=true (для distance: убрать антиалиасный ореол на краю предмета)", "enclosed:string=auto (для distance: замкнутый фон внутри предмета: off, auto, all)", "simplify:int=0 (0 выключено; N: маска считается на копии, сведённой к N цветам, и переносится на исходные пиксели: для рисованных и текстурных картинок, 6-12)", "denoise:int=0 (для simplify: радиус медианного фильтра копии)", "enclosedMin:int=0 (для simplify: наименьшая замкнутая область фона в пикселях, которую стирать; 0 это автоматически)"],
                (img, a) =>
                {
                    PixelRect? keep = null;
                    if (a.TryGetArray("keepRect", out var kr) && kr.Count == 4) keep = new PixelRect(kr[0], kr[1], kr[2], kr[3]);
                    if (a.Int("simplify", 0) > 0 && !a.String("mode", "distance").Equals("flood", StringComparison.OrdinalIgnoreCase))
                        return Background.RemoveViaSimplified(img, a.Int("simplify", 0), a.Int("denoise", 0), a.String("enclosed", "auto") ?? "auto", a.Int("enclosedMin", 0), a.Bool("defringe", true),
                            s => Cutout.RemoveByDistance(s, a.Int("clusters", 4), a.Int("threshold", 0), a.Int("smooth", 0), a.Bool("keepLargest", true), keep,
                                a.Has("follow") ? a.Double("follow", 0) : -1, false, "off"));
                    if (!a.String("mode", "distance").Equals("flood", StringComparison.OrdinalIgnoreCase))
                        return Cutout.RemoveByDistance(img, a.Int("clusters", 4), a.Int("threshold", 0), a.Int("smooth", 0), a.Bool("keepLargest", true), keep,
                            a.Has("follow") ? a.Double("follow", 0) : -1, a.Bool("defringe", true), a.String("enclosed", "auto"));
                    return Cutout.Remove(img, a.Int("clusters", 4), a.Int("tolerance", 30), a.Int("edgeStop", 45), a.Bool("keepLargest", true), keep);
                }),
            new("removeOutline",
                "Убрать контур (обводку) спрайта, в том числе рваный. Контур это крайние пиксели у прозрачности цвета color (по умолчанию самый частый цвет на краю). passes слоёв подряд, остановка когда подходящих пикселей на краю не осталось. dark=true снимает любые тёмные крайние пиксели (обводка из нескольких оттенков).",
                ["color:color=самый частый на краю", "tolerance:int=40", "dark:bool=false", "maxLuma:int=70 (для dark)", "passes:int=3", "diagonal:bool=false"],
                (img, a) => OutlineRemoval.Remove(img, a.Has("color") ? a.Color("color", Pixel.Empty) : null, a.Int("tolerance", 40), a.Bool("dark", false),
                    a.Int("maxLuma", 70), a.Int("passes", 3), a.Bool("diagonal", false))),
            new("cleanEdges",
                "Почистить края после вырезания фона: убрать ореол цвета matte на краю (passes слоёв), одинокие пиксели (specks) и закрыть щели в краю (fillGaps: прозрачный пиксель с >= minNeighbors непрозрачными соседями из 8).",
                ["matte:color=нет (например #000000 для чёрного ореола)", "tolerance:int=40", "passes:int=1", "specks:bool=true", "fillGaps:bool=true", "minNeighbors:int=6"],
                (img, a) => Background.CleanEdges(img, a.Has("matte") ? a.Color("matte", Pixel.Empty) : null, a.Int("tolerance", 40), a.Int("passes", 1),
                    a.Bool("specks", true), a.Bool("fillGaps", true), a.Int("minNeighbors", 6))),
            new("mergeRareColors",
                "Слить цвета, у которых меньше minCount пикселей, с ближайшим частым цветом.",
                ["minCount:int=3"],
                (img, a) => Cleanup.MergeRareColors(img, a.Int("minCount", 3))),
            new("simplifyColorRegions",
                "Слить небольшие цветовые пятна с близким цветом соседней крупной области, сохраняя прозрачность.",
                ["maxArea:int=4", "tolerance:int=32"],
                (img, a) => ColorRegions.Simplify(img, a.Int("maxArea", 4), a.Int("tolerance", 32))),
            new("applySpriteStyle",
                "Привести спрайт к профилю: холст, опорная линия, палитра, обводка и свет.",
                ["profile:object"],
                (img, a) => SpriteStyle.Apply(img, SpriteStyle.Parse(a.Object("profile").GetRawText()))),
            new("patchPixels",
                "Точечные правки в координатах холста от нуля. Цвет transparent стирает пиксель.",
                ["edits:[[x,y,color],...]"],
                (img, a) =>
                {
                    var result = img.Clone();
                    foreach (var (x, y, color) in a.PixelEdits("edits"))
                    {
                        if (!result.InBounds(x, y))
                            throw new ArgumentException(CoreText.F("Пиксель ({0}, {1}) за пределами изображения", x, y));
                        result.Set(x, y, color);
                    }
                    return result;
                }),
            new("replaceColor",
                "Заменить один цвет другим (to=transparent удаляет цвет).",
                ["from:hex", "to:hex"],
                (img, a) =>
                {
                    if (!a.Has("from")) throw new ArgumentException(CoreText.T("replaceColor: нужен from"));
                    return Cleanup.ReplaceColor(img, a.Color("from", Pixel.Empty), a.Color("to", Pixel.Empty));
                }),
            new("outline",
                "Автообводка. mode: outside|inside|both. Для outside холст расширяется автоматически (autoPad).",
                ["color:hex=#000000", "mode:string=outside", "thickness:int=1", "diagonal:bool=false", "autoPad:bool=true", "shade:bool=false (цветной контур: тёмный оттенок соседнего цвета)", "darken:number=0.55 (для shade)"],
                Outline_),
            new("pad", "Добавить пустые поля вокруг.", ["amount:int=1 (или left/top/right/bottom)"],
                (img, a) =>
                {
                    int n = a.Int("amount", 1);
                    return Transform.Pad(img, a.Int("left", n), a.Int("top", n), a.Int("right", n), a.Int("bottom", n));
                }),
            new("trim", "Обрезать пустые края.", [], (img, _) => Transform.Trim(img)),
            new("shift", "Сдвинуть содержимое на dx, dy пикселей; wrap=true зацикливает (тайлы).",
                ["dx:int=0", "dy:int=0", "wrap:bool=true"],
                (img, a) => Transform.Shift(img, a.Int("dx", 0), a.Int("dy", 0), a.Bool("wrap", true))),
            new("resizeCanvas", "Изменить размер холста без масштабирования. anchor: topleft|top|topright|left|center|right|bottomleft|bottom|bottomright.",
                ["width:int", "height:int", "anchor:string=topleft", "multipleOf:int=0 (округлить размер вверх до кратного N)", "onlyGrow:bool=false (не меньше текущего размера: ничего не обрезать)"],
                (img, a) =>
                {
                    int w = a.Int("width", img.Width), h = a.Int("height", img.Height);
                    int m = a.Int("multipleOf", 0);
                    if (m > 1) { w = (w + m - 1) / m * m; h = (h + m - 1) / m * m; }
                    if (a.Bool("onlyGrow", false)) { w = Math.Max(w, img.Width); h = Math.Max(h, img.Height); }
                    if (w < 1 || h < 1 || w > 8192 || h > 8192) throw new ArgumentException(CoreText.T("resizeCanvas: размер от 1 до 8192"));
                    var (ax, ay) = Transform.ParseAnchor(a.String("anchor"));
                    return Transform.ResizeCanvas(img, w, h, ax, ay);
                }),
            new("rotate",
                "Повернуть на произвольный угол по часовой стрелке. smooth=true: чёткий поворот без новых цветов (увеличение, поворот, уменьшение частым цветом). expand=true расширяет холст под повёрнутое.",
                ["angle:number=15", "smooth:bool=true", "expand:bool=false"],
                (img, a) => Rotation.Rotate(img, a.Double("angle", 15), a.Bool("smooth", true), a.Bool("expand", false))),
            new("flipH", "Отразить по горизонтали.", [], (img, _) => Transform.FlipH(img)),
            new("flipV", "Отразить по вертикали.", [], (img, _) => Transform.FlipV(img)),
            new("rotate90", "Повернуть по часовой стрелке.", ["times:int=1"], (img, a) => Transform.Rotate90(img, a.Int("times", 1))),
        };
        return ops.Concat(MoreOpsRegistry.All()).ToDictionary(o => o.Name, StringComparer.OrdinalIgnoreCase);
    }

    private static PixelImage Resize(PixelImage img, OpArgs a)
    {
        int w = a.Int("width", 0), h = a.Int("height", 0);
        double scale = a.Double("scale", 0);
        bool keep = a.Bool("keepAspect", true);

        if (scale > 0)
        {
            w = (int)Math.Max(1, Math.Round(img.Width * scale));
            h = (int)Math.Max(1, Math.Round(img.Height * scale));
        }
        else if (w > 0 && h > 0)
        {
            if (keep)
            {
                double k = Math.Min((double)w / img.Width, (double)h / img.Height);
                w = (int)Math.Max(1, Math.Round(img.Width * k));
                h = (int)Math.Max(1, Math.Round(img.Height * k));
            }
        }
        else if (w > 0)
            h = keep ? (int)Math.Max(1, Math.Round((double)img.Height * w / img.Width)) : img.Height;
        else if (h > 0)
            w = keep ? (int)Math.Max(1, Math.Round((double)img.Width * h / img.Height)) : img.Width;
        else
            throw new ArgumentException(CoreText.T("resize: укажите width, height или scale"));

        var method = (a.String("method", "box") ?? "box").ToLowerInvariant() switch
        {
            "nearest" => ResizeMethod.Nearest,
            "box" or "area" or "average" => ResizeMethod.Box,
            "mode" or "dominant" => ResizeMethod.Mode,
            "smart" or "edge" => ResizeMethod.Smart,
            var m => throw new ArgumentException(CoreText.F("resize: неизвестный method '{0}' (nearest|box|mode|smart)", m)),
        };
        if (a.Bool("noUpscale", false) && w >= img.Width && h >= img.Height) return img.Clone();
        return Resampler.Resize(img, w, h, method);
    }

    private static PixelImage Outline_(PixelImage img, OpArgs a)
    {
        var mode = (a.String("mode", "outside") ?? "outside").ToLowerInvariant() switch
        {
            "outside" => OutlineMode.Outside,
            "inside" => OutlineMode.Inside,
            "both" => OutlineMode.Both,
            var m => throw new ArgumentException(CoreText.F("outline: неизвестный mode '{0}' (outside|inside|both)", m)),
        };
        int t = Math.Max(1, a.Int("thickness", 1));
        if (a.Bool("autoPad", true) && mode != OutlineMode.Inside)
            img = Transform.Pad(img, t, t, t, t);
        return Outline.Apply(img, a.Color("color", Pixel.Rgb(0, 0, 0)), mode, a.Bool("diagonal", false), t, a.Bool("shade", false) ? a.Double("darken", 0.55) : null);
    }
}
