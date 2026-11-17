// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
namespace PixelEditor.Core.Ops;

public readonly record struct CheckResult(bool Passed, string Message)
{
    public static CheckResult Pass(string message) => new(true, message);
    public static CheckResult Fail(string message) => new(false, message);
}

public sealed record CheckInfo(
    string Name,
    string description,
    string[] parameters,
    Func<PixelImage, OpArgs, CheckResult> Run,
    string Source = "builtin")
{
    /// <summary>Description in the current language (the source text in code is Russian, see CoreText).</summary>
    public string Description => CoreText.T(description);

    public string[] Parameters => parameters.Select(CoreText.T).ToArray();
}

/// <summary>Registry of result checks. Built-in ones plus plugins (see Plugins/).</summary>
public static class Checks
{
    private static readonly Dictionary<string, CheckInfo> Map = Build();
    private static readonly object Gate = new();

    public static IEnumerable<CheckInfo> All { get { lock (Gate) return Map.Values.ToList(); } }

    public static CheckInfo Get(string name)
    {
        lock (Gate)
            return Map.TryGetValue(name, out var c)
                ? c
                : throw new ArgumentException(CoreText.F("Неизвестная проверка '{0}'. Доступно: {1}", name, string.Join(", ", Map.Keys)));
    }

    public static void Register(CheckInfo check)
    {
        lock (Gate)
            if (!Map.TryAdd(check.Name, check))
                throw new ArgumentException(CoreText.F("Проверка '{0}' уже существует", check.Name));
    }

    private static int Need(OpArgs a, string check, string name)
    {
        if (!a.Has(name)) throw new ArgumentException(CoreText.F("Проверка '{0}': нужен параметр '{1}'", check, name));
        return a.Int(name, 0);
    }

    private static Dictionary<string, CheckInfo> Build()
    {
        var list = new List<CheckInfo>
        {
            new("maxColors", "Цветов не больше value.", ["value:int"], (img, a) =>
            {
                int max = Need(a, "maxColors", "value"), n = img.DistinctColors();
                return n <= max ? CheckResult.Pass(CoreText.F("цветов {0} (лимит {1})", n, max)) : CheckResult.Fail(CoreText.F("цветов {0}, лимит {1}", n, max));
            }),
            new("minColors", "Цветов не меньше value.", ["value:int"], (img, a) =>
            {
                int min = Need(a, "minColors", "value"), n = img.DistinctColors();
                return n >= min ? CheckResult.Pass(CoreText.F("цветов {0} (минимум {1})", n, min)) : CheckResult.Fail(CoreText.F("цветов {0}, минимум {1}", n, min));
            }),
            new("size", "Размер картинки: точный (width/height), предельный (maxWidth/maxHeight) или кратный (multipleOf).",
                ["width:int", "height:int", "maxWidth:int", "maxHeight:int", "multipleOf:int (ширина и высота кратны N)"], (img, a) =>
                {
                    var bad = new List<string>();
                    if (a.Has("width") && img.Width != a.Int("width", 0)) bad.Add(CoreText.F("ширина {0}, нужна {1}", img.Width, a.Int("width", 0)));
                    if (a.Has("height") && img.Height != a.Int("height", 0)) bad.Add(CoreText.F("высота {0}, нужна {1}", img.Height, a.Int("height", 0)));
                    if (a.Has("maxWidth") && img.Width > a.Int("maxWidth", 0)) bad.Add(CoreText.F("ширина {0} больше {1}", img.Width, a.Int("maxWidth", 0)));
                    if (a.Has("maxHeight") && img.Height > a.Int("maxHeight", 0)) bad.Add(CoreText.F("высота {0} больше {1}", img.Height, a.Int("maxHeight", 0)));
                    int mul = a.Int("multipleOf", 0);
                    if (mul > 1 && (img.Width % mul != 0 || img.Height % mul != 0)) bad.Add(CoreText.F("{0}×{1} не кратно {2}", img.Width, img.Height, mul));
                    return bad.Count == 0 ? CheckResult.Pass($"{img.Width}×{img.Height}") : CheckResult.Fail(string.Join("; ", bad));
                }),
            new("opaquePixels", "Число непрозрачных пикселей в диапазоне min..max.", ["min:int", "max:int"], (img, a) =>
            {
                int n = img.OpaqueCount();
                if (a.Has("min") && n < a.Int("min", 0)) return CheckResult.Fail(CoreText.F("пикселей {0}, минимум {1}", n, a.Int("min", 0)));
                if (a.Has("max") && n > a.Int("max", 0)) return CheckResult.Fail(CoreText.F("пикселей {0}, максимум {1}", n, a.Int("max", 0)));
                return CheckResult.Pass(CoreText.F("пикселей {0}", n));
            }),
            new("noIslands", "Нет мусора: связных групп пикселей размером <= maxSize.", ["maxSize:int=1", "diagonal:bool=true"], (img, a) =>
            {
                int removed = img.OpaqueCount() - Cleanup.RemoveIslands(img, a.Int("maxSize", 1), a.Bool("diagonal", true)).OpaqueCount();
                return removed == 0 ? CheckResult.Pass(CoreText.T("мусора нет")) : CheckResult.Fail(CoreText.F("мусорных пикселей: {0}", removed));
            }),
            new("noHoles", "Нет замкнутых пустых дырок размером <= maxSize.", ["maxSize:int=без ограничения"], (img, a) =>
            {
                int filled = Cleanup.FillHoles(img, a.Int("maxSize", int.MaxValue)).OpaqueCount() - img.OpaqueCount();
                return filled == 0 ? CheckResult.Pass(CoreText.T("дырок нет")) : CheckResult.Fail(CoreText.F("пикселей в дырках: {0}", filled));
            }),
            new("noRareColors", "Нет цветов, у которых меньше minCount пикселей.", ["minCount:int=3"], (img, a) =>
            {
                int min = a.Int("minCount", 3);
                var rare = ColorStats.Count(img).Where(c => c.Count < min).ToList();
                return rare.Count == 0
                    ? CheckResult.Pass(CoreText.T("редких цветов нет"))
                    : CheckResult.Fail(CoreText.F("редких цветов: {0} ({1})", rare.Count, string.Join(", ", rare.Take(4).Select(c => $"{Pixel.ToHex(c.Color)}×{c.Count}"))));
            }),
            new("onlyPalette", "Все цвета входят в палитру.", ["palette:string[] (hex)", "paletteFile:string"], (img, a) =>
            {
                var pal = a.Palette() ?? throw new ArgumentException(CoreText.T("Проверка 'onlyPalette': нужен palette или paletteFile"));
                var set = pal.Select(c => c | 0xFF000000u).ToHashSet();
                var bad = ColorStats.Count(img).Where(c => !set.Contains(c.Color)).ToList();
                return bad.Count == 0
                    ? CheckResult.Pass(CoreText.T("все цвета из палитры"))
                    : CheckResult.Fail(CoreText.F("вне палитры: {0} ({1})", bad.Count, string.Join(", ", bad.Take(4).Select(c => Pixel.ToHex(c.Color)))));
            }),
            new("binaryAlpha", "Прозрачность только 0 или 255 (нет полупрозрачных пикселей).", [], (img, _) =>
            {
                int n = img.Pixels.Count(p => p != 0 && Pixel.A(p) != 255);
                return n == 0 ? CheckResult.Pass(CoreText.T("альфа бинарная")) : CheckResult.Fail(CoreText.F("полупрозрачных пикселей: {0}", n));
            }),
            new("trimmed", "Нет пустых рядов и столбцов по краям.", [], (img, _) =>
            {
                var t = Transform.Trim(img);
                return t.Width == img.Width && t.Height == img.Height
                    ? CheckResult.Pass(CoreText.T("края плотные"))
                    : CheckResult.Fail(CoreText.F("можно обрезать до {0}×{1}", t.Width, t.Height));
            }),
            new("connected", "Спрайт состоит из одной связной части.", ["diagonal:bool=true"], (img, a) =>
            {
                int total = img.OpaqueCount();
                if (total == 0) return CheckResult.Fail(CoreText.T("картинка пуста"));
                // remove the largest part: if anything is left, there is more than one part
                int biggest = LargestComponent(img, a.Bool("diagonal", true));
                return biggest == total ? CheckResult.Pass(CoreText.T("одна часть")) : CheckResult.Fail(CoreText.F("несколько частей: в крупнейшей {0} из {1} пикселей", biggest, total));
            }),
        };
        return list.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
    }

    private static int LargestComponent(PixelImage img, bool diagonal)
    {
        var seen = new bool[img.Pixels.Length];
        var nb = Cleanup.Neighbors(diagonal);
        var stack = new Stack<int>();
        int best = 0;
        for (int s = 0; s < img.Pixels.Length; s++)
        {
            if (seen[s] || Pixel.IsEmpty(img.Pixels[s])) continue;
            int size = 0;
            seen[s] = true;
            stack.Push(s);
            while (stack.Count > 0)
            {
                int cur = stack.Pop();
                size++;
                int cx = cur % img.Width, cy = cur / img.Width;
                foreach (var (dx, dy) in nb)
                {
                    int nx = cx + dx, ny = cy + dy;
                    if (!img.InBounds(nx, ny)) continue;
                    int ni = ny * img.Width + nx;
                    if (seen[ni] || Pixel.IsEmpty(img.Pixels[ni])) continue;
                    seen[ni] = true;
                    stack.Push(ni);
                }
            }
            best = Math.Max(best, size);
        }
        return best;
    }
}
