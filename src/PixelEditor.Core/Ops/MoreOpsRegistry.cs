// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
namespace PixelEditor.Core.Ops;

/// <summary>Additional operations: color (invert, gray, hue, tone, posterize), shape (trim, symmetry, tile, grow and shrink, shadow) and noise cleanup.</summary>
internal static class MoreOpsRegistry
{
    public static IEnumerable<OperationInfo> All() =>
    [
        new("invert", "Инвертировать цвета (прозрачность не меняется).", [], (img, _) => ColorOps.Invert(img)),
        new("grayscale", "Перевести в оттенки серого.", [], (img, _) => ColorOps.Grayscale(img)),
        new("hueShift", "Повернуть оттенок цветов на degrees градусов (-180..180); saturation и value множители (1 = без изменений).", ["degrees:number=30", "saturation:number=1", "value:number=1"], (img, a) => ColorOps.HueShift(img, a.Double("degrees", 30), a.Double("saturation", 1), a.Double("value", 1))),
        new("tint", "Карта градиента: яркость каждого пикселя переводится в цвет между dark и light (с mid получается три тона). Годится для сепии, монохрома и палитры «под цвет».", ["dark:color=#000000", "light:color=#FFFFFF", "mid:color=нет"], (img, a) => ColorOps.Tint(img, a.Color("dark", 0xFF000000), a.Color("light", 0xFFFFFFFF), a.Has("mid") ? a.Color("mid", 0xFF808080) : null)),
        new("posterize", "Постеризация: каждый канал сводится к levels ступеням (2..64). Мало цветов, плоские области.", ["levels:int=4"], (img, a) => ColorOps.Posterize(img, a.Int("levels", 4))),
        new("threshold", "Два тона: яркость >= value даёт light, остальное dark (чёрно-белый вид).", ["value:int=128", "dark:color=#000000", "light:color=#FFFFFF"], (img, a) => ColorOps.Threshold(img, a.Int("value", 128), a.Color("dark", 0xFF000000), a.Color("light", 0xFFFFFFFF))),
        new("autoLevels", "Автоуровни: растягивает диапазон яркости до 0..255. Вялые, блёклые картинки становятся контрастнее. cutoff: сколько процентов самых тёмных и самых светлых пикселей не учитывать (1-2 для фото со случайными бликами).", ["cutoff:number=0"], (img, a) => ColorOps.AutoLevels(img, a.Double("cutoff", 0))),
        new("median", "Медианный фильтр (как MedianFilter в Pillow): каждый канал пикселя становится медианой окна (2r+1)×(2r+1). Убирает JPEG-шум и зерно фото, но сохраняет резкие границы. Делай перед уменьшением фото.", ["radius:int=1"], (img, a) => Filters.Median(img, a.Int("radius", 1))),
        new("unsharp", "Нерезкая маска (как UnsharpMask в Pillow): добавляет amount × (картинка минус размытие радиуса radius). Возвращает чёткость после уменьшения, чтобы тонкие линии пережили квантование.", ["radius:int=1", "amount:number=0.8"], (img, a) => Filters.UnsharpMask(img, a.Int("radius", 1), a.Double("amount", 0.8))),
        new("edges", "Линии границ по яркости (оператор Собеля, как FIND_EDGES в Pillow). threshold 0..255: чем меньше, тем больше линий; thin даёт линии в один пиксель; onlyLines рисует только линии на прозрачном фоне (контурный рисунок фото).", ["threshold:int=40", "color:color=#000000", "thin:bool=true", "onlyLines:bool=false"], (img, a) => Filters.EdgeLines(img, a.Int("threshold", 40), a.Color("color", 0xFF000000), a.Bool("thin", true), a.Bool("onlyLines", false))),
        new("swapColors", "Поменять местами два цвета.", ["a:hex", "b:hex"], (img, a) => a.Has("a") && a.Has("b") ? ColorOps.SwapColors(img, a.Color("a", 0), a.Color("b", 0)) : throw new ArgumentException(CoreText.T("swapColors: нужны a и b"))),
        new("background", "Залить прозрачные пиксели цветом (картинка «на фоне»).", ["color:hex=#FFFFFF"], (img, a) => ColorOps.Background(img, a.Color("color", 0xFFFFFFFF))),
        new("crop", "Вырезать прямоугольник x, y, width, height (за пределами картинки обрезается).", ["x:int=0", "y:int=0", "width:int", "height:int"], (img, a) => ShapeOps.Crop(img, a.Int("x", 0), a.Int("y", 0), a.Int("width", img.Width), a.Int("height", img.Height))),
        new("mirror", "Симметрия: mode left|right|top|bottom копирует одну половину зеркально на другую, quad копирует левый верхний угол на все четыре.", ["mode:string=left"], (img, a) => ShapeOps.Mirror(img, a.String("mode", "left")!)),
        new("tile", "Повторить картинку cols × rows раз (плитка).", ["cols:int=2", "rows:int=2"], (img, a) => ShapeOps.Tile(img, a.Int("cols", 2), a.Int("rows", 2))),
        new("grow", "Расширить фигуру на amount пикселей: новые пиксели берут цвет соседа или заданный color.", ["amount:int=1", "color:hex=цвет соседа", "diagonal:bool=false"], (img, a) => ShapeOps.Grow(img, a.Int("amount", 1), a.Has("color") ? a.Color("color", 0) : null, a.Bool("diagonal", false))),
        new("shrink", "Сузить фигуру на amount пикселей (убрать крайние пиксели).", ["amount:int=1", "diagonal:bool=false"], (img, a) => ShapeOps.Shrink(img, a.Int("amount", 1), a.Bool("diagonal", false))),
        new("dropShadow", "Тень под фигурой: копия, сдвинутая на dx, dy. autoPad расширяет холст, чтобы тень не обрезалась.", ["dx:int=1", "dy:int=1", "color:hex=#000000", "autoPad:bool=true"], (img, a) => ShapeOps.DropShadow(img, a.Int("dx", 1), a.Int("dy", 1), a.Color("color", 0xFF000000), a.Bool("autoPad", true))),
        new("smooth", "Убрать шум: пиксель заменяется самым частым цветом среди 3×3 соседей, если тот встречается чаще. Хорошо чистит JPEG-крошку.", ["passes:int=1"], (img, a) => ShapeOps.Smooth(img, a.Int("passes", 1))),
        new("pixelate", "Мозаика: блоки size×size заливаются самым частым цветом блока. Размер картинки не меняется.", ["size:int=2"], (img, a) => ShapeOps.Pixelate(img, a.Int("size", 2))),
        new("despeckle", "Убрать цветовую крошку: мелкие области одного цвета (до maxArea пикселей) перекрашиваются в цвет соседней области с самой длинной общей границей. Для фото после уменьшения и квантования; прозрачность не затрагивается.", ["maxArea:int=3", "diagonal:bool=true", "passes:int=3"], (img, a) => Despeckle.Apply(img, a.Int("maxArea", 3), a.Bool("diagonal", true), a.Int("passes", 3))),
    ];
}
