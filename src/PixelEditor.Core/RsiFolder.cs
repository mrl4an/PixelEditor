// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Globalization;

namespace PixelEditor.Core;

/// <summary>Image file access for <see cref="RsiFolder"/> (Core has no PNG codec; PixelEngine passes ImageIO, tests pass a fake).</summary>
public sealed record RsiImageCodec(Func<string, PixelImage> Load, Action<PixelImage, string> SavePng);

/// <summary>One state of an .rsi folder. Layout: "sheet" (rows = directions, columns = frames), "wrapped" (right cell count, wrapped by width, as in foreign RSIs), "mismatch", "unreadable" or "missing".</summary>
public sealed record RsiStateReport(string Name, int Directions, int Frames, double[][] Delays, bool PngExists, int? PngWidth, int? PngHeight,
    int ExpectedWidth, int ExpectedHeight, string Layout, bool SizeConsistent);

public sealed record RsiReport(string Folder, int Version, string License, string Copyright, int FrameWidth, int FrameHeight, IReadOnlyList<RsiStateReport> States);

public sealed record RsiExportReport(string Output, string State, int Width, int Height, int FrameWidth, int FrameHeight, int Directions, int Frames,
    IReadOnlyList<string> Rows, double[][] Delays, bool Repacked);

/// <summary>
/// Command-line RSI workflow (the same steps as the editor's RsiViewModel, without any UI): info, new, write a state from a sheet PNG, export a state as a sheet.
/// meta.json is only touched through RsiMeta.Parse, Upsert and ToJson.
/// </summary>
public static class RsiFolder
{
    private const int MaxFrameSide = 4096;

    public static string MetaPath(string folder) => Path.Combine(folder, "meta.json");

    private static RsiMeta ReadMeta(string folder)
    {
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException(CoreText.F("Папка RSI не найдена: {0}", folder));
        var path = MetaPath(folder);
        if (!File.Exists(path)) throw new FileNotFoundException(CoreText.F("В папке {0} нет meta.json: сначала создайте RSI (pixed rsi new)", folder), path);
        return RsiMeta.Parse(InputArgs.ReadTextFile(path));
    }

    /// <summary>Classifies a PNG size against a state: exact sheet, wrapped by width (same cell count) or mismatch.</summary>
    public static string ClassifyLayout(int width, int height, int fw, int fh, int directions, int frames)
    {
        if (width == fw * frames && height == fh * directions) return "sheet";
        if (width >= fw && height >= fh && width % fw == 0 && height % fh == 0 && (width / fw) * (height / fh) == directions * frames) return "wrapped";
        return "mismatch";
    }

    private static RsiStateReport Describe(string folder, RsiMeta meta, RsiState s, RsiImageCodec codec)
    {
        int ew = meta.FrameWidth * s.Frames, eh = meta.FrameHeight * s.Directions;
        var png = Path.Combine(folder, s.Name + ".png");
        if (!File.Exists(png)) return new(s.Name, s.Directions, s.Frames, s.Delays, false, null, null, ew, eh, "missing", false);
        try
        {
            var img = codec.Load(png);
            var layout = ClassifyLayout(img.Width, img.Height, meta.FrameWidth, meta.FrameHeight, s.Directions, s.Frames);
            return new(s.Name, s.Directions, s.Frames, s.Delays, true, img.Width, img.Height, ew, eh, layout, layout != "mismatch");
        }
        catch (Exception)
        {
            return new(s.Name, s.Directions, s.Frames, s.Delays, true, null, null, ew, eh, "unreadable", false);
        }
    }

    public static RsiReport Info(string folder, RsiImageCodec codec)
    {
        var meta = ReadMeta(folder);
        return new(Path.GetFullPath(folder), meta.Version, meta.License, meta.Copyright, meta.FrameWidth, meta.FrameHeight,
            meta.States.Select(s => Describe(folder, meta, s, codec)).ToList());
    }

    /// <summary>Creates the folder and meta.json (no states). License and copyright may be empty. Refuses to overwrite an existing meta.json.</summary>
    public static RsiReport Create(string folder, int width, int height, string? license, string? copyright)
    {
        if (width < 1 || height < 1 || width > MaxFrameSide || height > MaxFrameSide)
            throw new ArgumentException(CoreText.F("Размер кадра должен быть от 1 до {0}", MaxFrameSide));
        var path = MetaPath(folder);
        if (File.Exists(path)) throw new InvalidOperationException(CoreText.F("В папке {0} уже есть RSI (meta.json): он не перезаписывается", folder));
        Directory.CreateDirectory(folder);
        var meta = new RsiMeta { License = license ?? "", Copyright = copyright ?? "", FrameWidth = width, FrameHeight = height };
        File.WriteAllText(path, meta.ToJson());
        return new(Path.GetFullPath(folder), meta.Version, meta.License, meta.Copyright, width, height, []);
    }

    /// <summary>
    /// Parses delays in seconds: "0.1,0.2,0.1" (one value per frame, the same for every direction) or "a,b;c,d" (one group per direction, separated by ;).
    /// </summary>
    public static double[][] ParseDelays(string text, int directions, int frames)
    {
        var rows = text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(r => r.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries).Select(ParseDelay).ToArray()).ToList();
        if (rows.Count == 0) throw new ArgumentException(CoreText.T("delays: пустой список задержек"));
        if (rows.Count != 1 && rows.Count != directions)
            throw new ArgumentException(CoreText.F("delays: групп {0}, а направлений {1} (нужна одна группа для всех или по одной на направление)", rows.Count, directions));
        foreach (var r in rows)
            if (r.Length != frames) throw new ArgumentException(CoreText.F("delays: значений {0}, а кадров {1}", r.Length, frames));
        return Enumerable.Range(0, directions).Select(d => (double[])rows[rows.Count == 1 ? 0 : d].Clone()).ToArray();
    }

    private static double ParseDelay(string s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d > 0 && double.IsFinite(d)
            ? d : throw new ArgumentException(CoreText.F("Задержка «{0}» должна быть положительным числом секунд", s));

    private static void CheckStateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.EndsWith('.') || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException(CoreText.F("Недопустимое имя состояния: «{0}»", name));
    }

    /// <summary>
    /// Writes or updates one state from a sheet PNG (rows = directions in RSI order, columns = frames): copies the PNG as name.png and updates only that state in meta.json.
    /// The frame count comes from the sheet width and the meta frame size; directions omitted: taken from the sheet height when it is 1, 4 or 8 rows.
    /// </summary>
    public static RsiStateReport SetState(string folder, string name, string sheetPath, int? directions, double? delay, string? delays, RsiImageCodec codec)
    {
        CheckStateName(name);
        if (delay != null && delays != null) throw new ArgumentException(CoreText.T("Укажите либо delay, либо delays, но не оба"));
        var meta = ReadMeta(folder);
        if (!File.Exists(sheetPath)) throw new FileNotFoundException(CoreText.F("Файл листа не найден: {0}", sheetPath), sheetPath);
        var sheet = codec.Load(sheetPath);
        int fw = meta.FrameWidth, fh = meta.FrameHeight;

        if (sheet.Width < fw || sheet.Width % fw != 0)
            throw new ArgumentException(CoreText.F("Ширина листа {0} не делится на ширину кадра {1} из meta.json", sheet.Width, fw));
        int frames = sheet.Width / fw;

        int dirs;
        if (directions is { } d)
        {
            if (d is not (1 or 4 or 8)) throw new ArgumentException(CoreText.T("directions должно быть 1, 4 или 8"));
            dirs = d;
        }
        else
        {
            int rows = sheet.Height % fh == 0 ? sheet.Height / fh : 0;
            if (rows is not (1 or 4 or 8))
                throw new ArgumentException(CoreText.F("Высота листа {0} не соответствует 1, 4 или 8 строкам по {1} px (кадр из meta.json); задайте directions", sheet.Height, fh));
            dirs = rows;
        }
        if (sheet.Height != dirs * fh)
            throw new ArgumentException(CoreText.F("Лист {0}×{1} не подходит: для {2} направлений и {3} кадров размером {4}×{5} нужен {6}×{7}",
                sheet.Width, sheet.Height, dirs, frames, fw, fh, frames * fw, dirs * fh));

        double[][] delayTable;
        if (delays != null) delayTable = ParseDelays(delays, dirs, frames);
        else
        {
            double uniform = delay ?? (frames > 1 ? 0.1 : 1.0);
            if (!(uniform > 0) || !double.IsFinite(uniform)) throw new ArgumentException(CoreText.F("Задержка «{0}» должна быть положительным числом секунд", uniform.ToString(CultureInfo.InvariantCulture)));
            delayTable = RsiState.UniformDelays(dirs, frames, uniform);
        }

        var dest = Path.Combine(folder, name + ".png");
        bool isPng = string.Equals(Path.GetExtension(sheetPath), ".png", StringComparison.OrdinalIgnoreCase);
        if (isPng)
        {
            if (!string.Equals(Path.GetFullPath(sheetPath), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase)) File.Copy(sheetPath, dest, overwrite: true);
        }
        else codec.SavePng(sheet, dest);

        meta.Upsert(new RsiState { Name = name, Directions = dirs, Frames = frames, Delays = delayTable });
        File.WriteAllText(MetaPath(folder), meta.ToJson());
        return Describe(folder, meta, meta.Find(name)!, codec);
    }

    /// <summary>Writes a state as a sheet PNG (rows = directions, columns = frames); a wrapped foreign PNG is repacked with RsiLayout.Repack.</summary>
    public static RsiExportReport ExportState(string folder, string state, string output, RsiImageCodec codec)
    {
        var meta = ReadMeta(folder);
        var s = meta.Find(state) ?? throw new ArgumentException(CoreText.F("Состояния «{0}» нет в RSI. Есть: {1}", state, string.Join(", ", meta.States.Select(x => x.Name))));
        var png = Path.Combine(folder, s.Name + ".png");
        if (!File.Exists(png)) throw new FileNotFoundException(CoreText.F("Для состояния «{0}» нет файла {1}", s.Name, s.Name + ".png"), png);
        var img = codec.Load(png);
        var layout = ClassifyLayout(img.Width, img.Height, meta.FrameWidth, meta.FrameHeight, s.Directions, s.Frames);
        if (layout == "mismatch")
            throw new ArgumentException(CoreText.F("Размер {0}×{1} файла {2} не соответствует состоянию: {3} направлений, {4} кадров по {5}×{6}",
                img.Width, img.Height, s.Name + ".png", s.Directions, s.Frames, meta.FrameWidth, meta.FrameHeight));
        bool repack = layout == "wrapped";
        var sheet = repack ? RsiLayout.Repack(img, meta.FrameWidth, meta.FrameHeight, s.Directions, s.Frames) : img;
        var dir = Path.GetDirectoryName(Path.GetFullPath(output));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        codec.SavePng(sheet, output);
        return new(Path.GetFullPath(output), s.Name, sheet.Width, sheet.Height, meta.FrameWidth, meta.FrameHeight, s.Directions, s.Frames,
            RsiLayout.DirectionNames(s.Directions), s.Delays, repack);
    }
}
