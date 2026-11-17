// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using PixelEditor.Core;
using PixelEditor.Core.Ops;

namespace PixelEditor.Imaging;

public sealed record ImageSummary(string File, int Width, int Height, int OpaquePixels, int Colors);
public sealed record SheetSummary(string File, int Width, int Height, int FrameWidth, int FrameHeight, int Columns, IReadOnlyList<string> Rows, IReadOnlyList<string> FrameNames, string MetaFile);
public sealed record GifSummary(string File, int Width, int Height, int Frames, int DelayMs);
public sealed record ColorEntry(string Hex, int Count);
/// <summary>Colors: the most frequent colors (8 by default); the full list on request (<paramref name="ColorsShown"/> is less than ColorCount if the list is truncated).</summary>
public sealed record ImageInfo(string File, int Width, int Height, int OpaquePixels, int ColorCount, int PartialAlphaPixels, IReadOnlyList<ColorEntry> Colors,
    int TransparentPixels = 0, int ColorsShown = 0);
public sealed record RunReport(bool Success, bool StepsOk, bool? ChecksPassed, string Pipeline, string? Output,
    IReadOnlyList<StepLog> Steps, IReadOnlyList<CheckLog> Checks, ImageSummary Result, IReadOnlyDictionary<string, object>? Params = null);
/// <summary>A template parameter for catalogs (list_templates, pixed templates).</summary>
public sealed record ParamView(string Name, string Type, object Default, double? Min, double? Max, IReadOnlyList<string>? Choices, string Label, string Hint);
public sealed record CheckReport(bool ChecksPassed, string Image, IReadOnlyList<CheckLog> Checks);
public sealed record VariantDifference(int Recolored, int Added, int Removed);
public sealed record StyleReport(string? Output, int Width, int Height, int Colors, int OpaquePixels);
public sealed record VariantReport(int Row, string Pipeline, int Width, int Height, int OpaquePixels, int Colors,
    bool StepsOk, bool? ChecksPassed, VariantDifference? Difference, IReadOnlyList<StepLog> Steps, IReadOnlyList<CheckLog> Checks);
public sealed record BatchItem(string Input, string? Output, string Status, IReadOnlyList<string> Problems);
public sealed record BatchReport(string Pipeline, int Total, int Ok, int ChecksFailed, int Errors, IReadOnlyList<BatchItem> Items)
{
    /// <summary>0 all good, 1 there are errors, 2 only failed checks.</summary>
    public int ExitCode => Errors > 0 ? 1 : ChecksFailed > 0 ? 2 : 0;
}

/// <summary>
/// A high-level API on top of Core and file I/O. It is used by the CLI (pixed) and the MCP server
/// so the agent has the same set of actions. All input paths are ordinary file paths.
/// </summary>
public static class PixelEngine
{
    public static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp"];

    private static ImageSummary Summary(string path, PixelImage img) =>
        new(Path.GetFullPath(path), img.Width, img.Height, img.OpaqueCount(), img.DistinctColors());

    private static void EnsureDir(string file)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(file));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    }

    /// <summary>
    /// A PNG for viewing by eye (including by a model): a small picture is enlarged by a whole number of times up to ~maxSide px,
    /// transparency is shown as a checkerboard.
    /// </summary>
    public static byte[] RenderPreview(PixelImage img, int maxSide = 256)
    {
        int s = Math.Clamp(maxSide / Math.Max(img.Width, img.Height), 1, 16);
        var big = new PixelImage(img.Width * s, img.Height * s);
        for (int y = 0; y < img.Height; y++)
            for (int x = 0; x < img.Width; x++)
            {
                uint p = img.Pixels[y * img.Width + x];
                if (Pixel.IsEmpty(p)) p = ((x + y) & 1) == 0 ? Pixel.Rgb(255, 255, 255) : Pixel.Rgb(204, 204, 204);
                else p |= 0xFF000000u;
                for (int dy = 0; dy < s; dy++)
                    for (int dx = 0; dx < s; dx++)
                        big.Pixels[(y * s + dy) * big.Width + x * s + dx] = p;
            }
        return ImageIO.EncodePng(big);
    }

    /// <summary>A template from a file, from JSON text (a string starting with { or [) or from the library by name/id.</summary>
    public static Pipeline LoadPipeline(string fileOrJson, string? baseDir = null)
    {
        var s = fileOrJson.TrimStart();
        if (s.StartsWith('{') || s.StartsWith('['))
            return Pipeline.Parse(fileOrJson, baseDir ?? Directory.GetCurrentDirectory());
        if (File.Exists(fileOrJson))
        {
            // a file with a chat answer (explanations + a ```json block): extract the template from the text
            var text = File.ReadAllText(fileOrJson);
            var t = text.TrimStart();
            if (!t.StartsWith('{') && !t.StartsWith('['))
            {
                try { return Pipeline.Parse(PipelineText.Extract(text), baseDir ?? Path.GetDirectoryName(Path.GetFullPath(fileOrJson))); }
                catch (FormatException) { /* does not look like a template: the ordinary load below shows a clear error */ }
            }
            return Pipeline.Load(fileOrJson);
        }
        if (fileOrJson.Contains('\n') && fileOrJson.Contains('{'))   // a whole chat answer passed as text
            return Pipeline.Parse(PipelineText.Extract(fileOrJson), baseDir ?? Directory.GetCurrentDirectory());

        var template = PipelineLibrary.Default.FindWithPreset(fileOrJson);
        if (template != null) return Pipeline.Parse(template.Value.Template.Json, baseDir ?? Directory.GetCurrentDirectory()).WithPreset(template.Value.Preset);

        throw new FileNotFoundException(
            CoreText.F("Шаблон «{0}» не найден ни как файл, ни в библиотеке. Доступные: ", fileOrJson) +
            string.Join(", ", PipelineLibrary.Default.All().Select(t => t.Name)));
    }

    /// <summary>Library templates (system and your own) for lists and AI.</summary>
    public static IReadOnlyList<TemplateInfo> Templates() => PipelineLibrary.Default.All();

    /// <summary>The parameters of a template for a catalog: name, type, default, range or choices, label and hint.</summary>
    public static IReadOnlyList<ParamView> ParamsOf(IEnumerable<PipelineParam> ps) => ps.Select(p => new ParamView(p.Name, p.TypeName, p.Default, p.Min, p.Max,
        p.Type == ParamType.Choice ? p.Choices.Select(c => c.Value).ToList() : null, p.Label, p.Hint)).ToList();

    /// <summary>Library palettes (system and your own) for lists and AI.</summary>
    public static IReadOnlyList<PaletteInfo> Palettes() => PaletteLibrary.Default.All();

    /// <param name="topColors">how many of the most frequent colors to return; 0 or less: all</param>
    public static ImageInfo Inspect(string path, int topColors = 8)
    {
        var img = ImageIO.Load(path);
        int partial = img.Pixels.Count(p => p != 0 && Pixel.A(p) != 255);
        var bin = Transform.BinarizeAlpha(img, 128);
        var colors = ColorStats.Count(bin);
        var shown = (topColors > 0 ? colors.OrderByDescending(c => c.Count).Take(topColors) : colors)
            .Select(c => new ColorEntry(Pixel.ToHex(c.Color), c.Count)).ToList();
        return new ImageInfo(Path.GetFullPath(path), img.Width, img.Height, bin.OpaqueCount(), colors.Count, partial, shown,
            img.Pixels.Count(p => Pixel.A(p) == 0), shown.Count);
    }

    public static ImageSummary Apply(string op, string input, string output, JsonObject? args, string? baseDir = null)
    {
        var img = ImageIO.Load(input);
        using var doc = JsonDocument.Parse((args ?? new JsonObject()).ToJsonString());
        var result = Operations.Get(op).Run(img, new OpArgs(doc.RootElement, baseDir ?? Directory.GetCurrentDirectory()));
        EnsureDir(output);
        ImageIO.SavePng(result, output);
        return Summary(output, result);
    }

    /// <summary>Draws a scene of primitives (see Scene): sceneJsonOrFile is a JSON string or a path to .json. The result is a PNG.</summary>
    public static ImageSummary Draw(string sceneJsonOrFile, string output)
    {
        var image = Scene.Render(ReadScene(sceneJsonOrFile));
        EnsureDir(output);
        ImageIO.SavePng(image, output);
        return Summary(output, image);
    }

    /// <summary>Draws a sprite sheet from primitives (see SheetScene): the PNG plus a description (frame size, rows, frame names, delays) in metaOutput (default: next to the PNG as name.sheet.json).</summary>
    public static SheetSummary DrawSheet(string sheetJsonOrFile, string output, string? metaOutput = null)
    {
        var sheet = SheetScene.Render(ReadScene(sheetJsonOrFile));
        EnsureDir(output);
        ImageIO.SavePng(sheet.Image, output);
        var meta = metaOutput ?? Path.ChangeExtension(output, ".sheet.json");
        EnsureDir(meta);
        File.WriteAllText(meta, sheet.ToMetaJson());
        return new SheetSummary(Path.GetFullPath(output), sheet.Image.Width, sheet.Image.Height, sheet.FrameWidth, sheet.FrameHeight, sheet.Columns,
            sheet.RowNames, sheet.FrameNames.Select(n => n ?? "").ToList(), Path.GetFullPath(meta));
    }

    private static string ReadScene(string scene) =>
        scene.TrimStart().StartsWith('{') ? scene : File.ReadAllText(scene);

    /// <summary>Downloads a picture by a link (see ImageFetcher).</summary>
    public static ImageFetcher.FetchResult Fetch(string url, string output) => ImageFetcher.Fetch(url, output);

    public static RunReport Run(Pipeline pipeline, string input, string output) => Run(pipeline, input, output, null);

    public static RunReport Run(Pipeline pipeline, string input, string output, string? protectMask,
        IEnumerable<KeyValuePair<string, object?>>? paramOverrides = null)
    {
        var values = pipeline.ResolveParams(paramOverrides);   // a bad name or value throws before anything is read
        var res = pipeline.Run(ImageIO.Load(input), protectMask == null ? null : ImageIO.Load(protectMask), paramOverrides);
        // the picture is written even when checks fail: it shows what to fix in the template
        if (res.Ok)
        {
            EnsureDir(output);
            ImageIO.SavePng(res.Image, output);
        }
        return new RunReport(res.Success, res.Ok, res.Ok ? res.ChecksPassed : null, pipeline.Name,
            res.Ok ? Path.GetFullPath(output) : null, res.Log, res.Checks, Summary(output, res.Image),
            pipeline.Params.Count == 0 ? null : values);
    }

    public static CheckReport Check(Pipeline pipeline, string image, IEnumerable<KeyValuePair<string, object?>>? paramOverrides = null)
    {
        var checks = pipeline.RunChecks(ImageIO.Load(image), paramOverrides);
        return new CheckReport(checks.All(c => c.Passed || !c.Required), Path.GetFullPath(image), checks);
    }

    public static (IReadOnlyList<VariantReport> Variants, byte[] Preview) ComparePipelines(
        string input, IReadOnlyList<string> pipelines, string? protectMask = null)
    {
        if (pipelines.Count is < 1 or > 4)
            throw new ArgumentException("Supply one to four pipeline variants.");

        var original = ImageIO.Load(input);
        var mask = protectMask == null ? null : ImageIO.Load(protectMask);
        const int cell = 144, gap = 4;
        var sheet = new PixelImage(cell * 3 + gap * 2, (cell + gap) * pipelines.Count - gap);
        Array.Fill(sheet.Pixels, Pixel.Rgb(32, 32, 32));
        var reports = new List<VariantReport>(pipelines.Count);
        for (int i = 0; i < pipelines.Count; i++)
        {
            var pipeline = LoadPipeline(pipelines[i]);
            var run = pipeline.Run(original.Clone(), mask);
            var image = run.Image;
            int y = i * (cell + gap);
            DrawPreviewTile(sheet, original, cell, 0, y);
            VariantDifference? difference = null;
            if (run.Ok)
            {
                DrawPreviewTile(sheet, image, cell, cell + gap, y);
                if (original.Width == image.Width && original.Height == image.Height)
                {
                    var diff = ImageComparison.Compare(original, image);
                    difference = new(diff.Recolored, diff.Added, diff.Removed);
                    DrawPreviewTile(sheet, diff.Map, cell, (cell + gap) * 2, y);
                }
            }
            reports.Add(new(i + 1, pipeline.Name, image.Width, image.Height, image.OpaqueCount(),
                image.DistinctColors(), run.Ok, run.Ok ? run.ChecksPassed : null, difference, run.Log, run.Checks));
        }
        return (reports, ImageIO.EncodePng(sheet));
    }

    private static void DrawPreviewTile(PixelImage sheet, PixelImage image, int cell, int left, int top)
    {
        double scale = (double)cell / Math.Max(image.Width, image.Height);
        if (scale >= 1) scale = Math.Floor(scale);
        int w = Math.Max(1, (int)Math.Round(image.Width * scale));
        int h = Math.Max(1, (int)Math.Round(image.Height * scale));
        left += (cell - w) / 2;
        top += (cell - h) / 2;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                uint p = image.Get((int)((long)x * image.Width / w), (int)((long)y * image.Height / h));
                byte background = ((x / 8 + y / 8) & 1) == 0 ? (byte)235 : (byte)190;
                int alpha = Pixel.A(p);
                byte Blend(byte channel) => (byte)((channel * alpha + background * (255 - alpha)) / 255);
                sheet.Set(left + x, top + y, Pixel.Rgb(Blend(Pixel.R(p)), Blend(Pixel.G(p)), Blend(Pixel.B(p))));
            }
    }

    public static (StyleReport Report, byte[] Preview) ApplyStyle(SpriteStyleProfile profile,
        string? input, string? scene, string? output, string? mask = null, bool cutout = false, string? reference = null)
    {
        if ((input == null) == (scene == null))
            throw new ArgumentException("Supply exactly one of input or scene.");
        if ((cutout && mask != null) || (scene != null && (cutout || mask != null)))
            throw new ArgumentException("Choose either cutout or a mask, and use it only with an input image.");

        var source = input != null ? ImageIO.Load(input) : Scene.Render(ReadScene(scene!));
        var subject = source;
        if (mask != null)
        {
            var keep = ImageIO.Load(mask);
            if (keep.Width != source.Width || keep.Height != source.Height)
                throw new ArgumentException("The subject mask must match the input image.");
            subject = source.Clone();
            for (int i = 0; i < subject.Pixels.Length; i++)
                if (Pixel.A(keep.Pixels[i]) < 128) subject.Pixels[i] = Pixel.Empty;
        }
        else if (cutout) subject = Cutout.RemoveByDistance(source, 4, 0, 0, keepLargest: true);

        var result = SpriteStyle.Apply(subject, profile, scene != null ? ResizeMethod.Mode : ResizeMethod.Smart);
        const int cell = 192, gap = 4;
        var tiles = new List<PixelImage> { source };
        if (mask != null || cutout) tiles.Add(subject);
        tiles.Add(result);
        if (reference != null) tiles.Add(ImageIO.Load(reference));
        var sheet = new PixelImage((cell + gap) * tiles.Count - gap, cell);
        Array.Fill(sheet.Pixels, Pixel.Rgb(32, 32, 32));
        for (int i = 0; i < tiles.Count; i++) DrawPreviewTile(sheet, tiles[i], cell, i * (cell + gap), 0);
        byte[] preview = ImageIO.EncodePng(sheet);
        if (output != null)
        {
            EnsureDir(output);
            ImageIO.SavePng(result, output);
        }
        return (new(output == null ? null : Path.GetFullPath(output), result.Width, result.Height,
            result.DistinctColors(), result.OpaqueCount()), preview);
    }

    /// <summary>Processes all pictures of a folder with one template. PNG results go to outDir (with the same subfolder structure).</summary>
    public static BatchReport Batch(Pipeline pipeline, string inDir, string outDir, bool recurse = false, string? pattern = null,
        IEnumerable<KeyValuePair<string, object?>>? paramOverrides = null)
    {
        pipeline.ResolveParams(paramOverrides);   // fail once, before the first file
        if (!Directory.Exists(inDir)) throw new DirectoryNotFoundException(CoreText.F("Папка не найдена: {0}", inDir));
        var exts = pattern == null
            ? ImageExtensions.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : pattern.Split(';', ',').Select(p => p.Trim().TrimStart('*')).Where(p => p.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var inFull = Path.GetFullPath(inDir);
        var outFull = Path.GetFullPath(outDir);
        var files = Directory.EnumerateFiles(inFull, "*", recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
            .Where(f => exts.Contains(Path.GetExtension(f)))
            // do not process our own output if outDir lies inside inDir
            .Where(f => !f.StartsWith(outFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var items = new List<BatchItem>();
        foreach (var file in files)
        {
            var rel = Path.GetRelativePath(inFull, file);
            var target = Path.Combine(outFull, Path.ChangeExtension(rel, ".png"));
            try
            {
                var res = pipeline.Run(ImageIO.Load(file), null, paramOverrides);
                if (!res.Ok)
                {
                    var bad = res.Log.First(l => l.Error != null);
                    items.Add(new BatchItem(rel, null, "error", [CoreText.F("шаг {0} ({1}): {2}", bad.Index, bad.Op, bad.Error)]));
                    continue;
                }
                EnsureDir(target);
                ImageIO.SavePng(res.Image, target);
                var failed = res.Checks.Where(c => !c.Passed && c.Required).Select(c => $"{c.Check}: {c.Message}").ToList();
                var warn = res.Checks.Where(c => !c.Passed && !c.Required).Select(c => CoreText.F("(предупреждение) {0}: {1}", c.Check, c.Message));
                items.Add(new BatchItem(rel, Path.GetRelativePath(outFull, target), failed.Count == 0 ? "ok" : "checks_failed",
                    failed.Concat(warn).ToList()));
            }
            catch (Exception ex)
            {
                items.Add(new BatchItem(rel, null, "error", [ex.Message]));
            }
        }
        return new BatchReport(pipeline.Name, items.Count, items.Count(i => i.Status == "ok"),
            items.Count(i => i.Status == "checks_failed"), items.Count(i => i.Status == "error"), items);
    }

    /// <summary>Cuts a sheet into cw×ch cells and saves them as cell_RR_CC.png. Returns the file paths.</summary>
    public static IReadOnlyList<string> Slice(string sheet, int cw, int ch, string outDir, bool skipEmpty = false)
    {
        var img = Transform.BinarizeAlpha(ImageIO.Load(sheet), 128);
        Directory.CreateDirectory(outDir);
        var files = new List<string>();
        foreach (var (col, row, cell) in Sheet.Slice(img, cw, ch, skipEmpty))
        {
            var path = Path.GetFullPath(Path.Combine(outDir, $"cell_{row:00}_{col:00}.png"));
            ImageIO.SavePng(cell, path);
            files.Add(path);
        }
        return files;
    }

    /// <summary>Assembles pictures into a sheet; the order is as passed.</summary>
    public sealed record AnimExportReport(string Folder, int Frames, int Rows, IReadOnlyList<string> RowNames, IReadOnlyList<string> Files);

    /// <summary>
    /// Animation export from a PNG sheet for Unity, Godot and GIF without the editor. Sheet rows are animations (rows: names separated by commas,
    /// if not given: S N E W for 4 rows, S N E W SE SW NE NW for 8, otherwise row1, row2…), columns are frames. A copy of the sheet name.png is always written.
    /// noLoopRows: row names (or full clip names) whose animation does not loop (Godot, simple JSON). unity: also writes PixelEditorSheetImporter.cs (and the simple JSON it reads).
    /// </summary>
    public static AnimExportReport ExportAnimation(string sheetPath, int frameWidth, int frameHeight, IReadOnlyList<string>? rows, double delaySeconds,
        string outputFolder, string name, string godotTexturePath, bool aseprite = true, bool godot = true, bool gif = true, bool simple = false,
        IReadOnlyList<string>? noLoopRows = null, bool unity = false)
    {
        var sheet = ImageIO.Load(sheetPath);
        if (frameWidth < 1 || frameHeight < 1 || sheet.Width % frameWidth != 0 || sheet.Height % frameHeight != 0)
            throw new ArgumentException(CoreText.F("Лист {0}×{1} не делится на кадры {2}×{3}", sheet.Width, sheet.Height, frameWidth, frameHeight));
        int frames = sheet.Width / frameWidth, dirs = sheet.Height / frameHeight;
        var names = rows is { Count: > 0 } ? rows.ToList()
            : dirs is 1 or 4 or 8 && dirs > 1 ? RsiLayout.DirectionNames(dirs).ToList()
            : Enumerable.Range(1, dirs).Select(i => $"row{i}").ToList();
        if (names.Count != dirs) throw new ArgumentException(CoreText.F("Названий строк {0}, а строк в листе {1}", names.Count, dirs));
        var delays = RsiState.UniformDelays(dirs, frames, delaySeconds);
        var clips = AnimationExport.Clips(name, dirs, frames, delays, names, noLoopRows);

        Directory.CreateDirectory(outputFolder);
        var files = new List<string>();
        string Write(string file, string text) { var p = Path.Combine(outputFolder, file); File.WriteAllText(p, text); files.Add(p); return p; }
        var png = Path.Combine(outputFolder, name + ".png");
        ImageIO.SavePng(sheet, png);
        files.Add(png);
        if (aseprite) Write(name + ".aseprite.json", AnimationExport.AsepriteJson(name + ".png", sheet.Width, sheet.Height, frameWidth, frameHeight, frames, clips));
        if (godot) Write(name + ".tres", AnimationExport.GodotSpriteFrames(godotTexturePath, frameWidth, frameHeight, frames, clips));
        if (simple || unity) Write(name + ".json", AnimationExport.SimpleJson(name + ".png", frameWidth, frameHeight, frames, dirs, clips));
        if (unity) Write(AnimationExport.UnityScriptFileName, AnimationExport.UnityImporterScript);
        if (gif && frames > 1)
            foreach (var clip in clips)
            {
                var cells = Sheet.Slice(sheet, frameWidth, frameHeight).Where(c => c.Row == clip.Row).OrderBy(c => c.Col).Select(c => Transform.BinarizeAlpha(c.Image, 128)).ToList();
                var p = Path.Combine(outputFolder, $"{clip.Name}.gif".Replace(' ', '_'));
                File.WriteAllBytes(p, GifWriter.Encode(cells, Math.Max(10, (int)Math.Round(delaySeconds * 1000))));
                files.Add(p);
            }
        return new AnimExportReport(Path.GetFullPath(outputFolder), frames, dirs, names, files);
    }

    // ---------- RSI folders (logic in Core/RsiFolder.cs) ----------

    private static readonly RsiImageCodec RsiCodec = new(ImageIO.Load, ImageIO.SavePng);

    /// <summary>Meta (license, copyright, frame size) and the states of an .rsi folder with a PNG size check.</summary>
    public static RsiReport RsiInfo(string folder) => RsiFolder.Info(folder, RsiCodec);

    /// <summary>Creates an .rsi folder with meta.json (license and copyright may be empty; an existing meta.json is never overwritten).</summary>
    public static RsiReport RsiNew(string folder, int width, int height, string? license = null, string? copyright = null) =>
        RsiFolder.Create(folder, width, height, license, copyright);

    /// <summary>Writes or updates one state from a sheet PNG (rows = directions, columns = frames).</summary>
    public static RsiStateReport RsiSetState(string folder, string name, string sheet, int? directions = null, double? delay = null, string? delays = null) =>
        RsiFolder.SetState(folder, name, sheet, directions, delay, delays, RsiCodec);

    /// <summary>Writes a state as a rows×frames sheet PNG.</summary>
    public static RsiExportReport RsiExportState(string folder, string state, string output) => RsiFolder.ExportState(folder, state, output, RsiCodec);

    public static ImageSummary Compose(IReadOnlyList<string> inputs, int columns, string output)
    {
        if (inputs.Count == 0) throw new ArgumentException(CoreText.T("Нет входных картинок"));
        var frames = inputs.Select(p => Transform.BinarizeAlpha(ImageIO.Load(p), 128)).ToList();
        var sheet = Sheet.Compose(frames, columns);
        EnsureDir(output);
        ImageIO.SavePng(sheet, output);
        return Summary(output, sheet);
    }

    /// <summary>A GIF from separate frames (all the same size).</summary>
    public static GifSummary MakeGif(IReadOnlyList<string> frames, int delayMs, string output, bool loop = true)
    {
        var imgs = frames.Select(p => Transform.BinarizeAlpha(ImageIO.Load(p), 128)).ToList();
        EnsureDir(output);
        ImageIO.SaveGif(imgs, delayMs, output, loop);
        return new GifSummary(Path.GetFullPath(output), imgs[0].Width, imgs[0].Height, imgs.Count, delayMs);
    }

    /// <summary>A GIF from sheet cells (a cw×ch grid, a tail of empty cells is dropped).</summary>
    public static GifSummary MakeGifFromSheet(string sheet, int cw, int ch, int delayMs, string output, bool loop = true)
    {
        var cells = Sheet.Slice(Transform.BinarizeAlpha(ImageIO.Load(sheet), 128), cw, ch).Select(c => c.Image).ToList();
        if (cells.Count == 0) throw new ArgumentException(CoreText.T("В листе нет ни одной целой ячейки такого размера"));
        while (cells.Count > 1 && cells[^1].OpaqueCount() == 0) cells.RemoveAt(cells.Count - 1);
        EnsureDir(output);
        ImageIO.SaveGif(cells, delayMs, output, loop);
        return new GifSummary(Path.GetFullPath(output), cw, ch, cells.Count, delayMs);
    }
}
