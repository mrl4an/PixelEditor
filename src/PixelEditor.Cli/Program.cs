// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using PixelEditor.Core;
using PixelEditor.Core.Ops;
using PixelEditor.Core.Plugins;
using PixelEditor.Imaging;

// pixed: the console "hands" of the editor. All stdout output is JSON, errors go to stderr.
// Exit codes: 0 success, 1 error (including a failed step), 2 everything ran but checks did not pass.
// Full command list: pixed help. The logic lives in PixelEngine (Imaging) and Core, only argument parsing is here.

var json = new JsonSerializerOptions { WriteIndented = false, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

Log.Application = "pixed";
var inputArgs = new InputArgs();   // @file and - (stdin) arguments, see the help
try
{
    var rest = new List<string>(args);
    var langOpt = TakeOptions(rest, "--lang").LastOrDefault();
    if (langOpt != null) CoreText.Language = langOpt;
    var pluginDirs = TakeOptions(rest, "--plugins");
    var pattern = TakeOptions(rest, "--pattern").LastOrDefault();
    bool recurse = TakeFlag(rest, "--recurse");
    bool skipEmpty = TakeFlag(rest, "--skip-empty");
    bool noLoop = TakeFlag(rest, "--no-loop");
    var previewOpt = TakeOptions(rest, "--preview").LastOrDefault();
    var maskOpt = TakeOptions(rest, "--mask").LastOrDefault();
    var colorsOpt = TakeOptions(rest, "--max-colors").LastOrDefault();
    var fixedOpts = TakeOptions(rest, "--fixed");
    bool styleCutout = TakeFlag(rest, "--cutout");
    var tailOpt = TakeOptions(rest, "--tail").LastOrDefault();
    var levelOpt = TakeOptions(rest, "--level").LastOrDefault();
    bool stacksFlag = TakeFlag(rest, "--stacks");
    bool filesFlag = TakeFlag(rest, "--files");
    var chatImage = TakeOptions(rest, "--image").LastOrDefault();
    bool chatCompact = TakeFlag(rest, "--compact");
    var kindOpt = TakeOptions(rest, "--kind").LastOrDefault();
    var layoutTemplate = TakeOptions(rest, "--template").LastOrDefault();
    var layoutRows = TakeOptions(rest, "--rows").LastOrDefault();
    var layoutFrames = TakeOptions(rest, "--frames").LastOrDefault();
    var layoutSize = TakeOptions(rest, "--size").LastOrDefault();
    var layoutDelay = TakeOptions(rest, "--delay").LastOrDefault();
    var metaOpt = TakeOptions(rest, "--meta").LastOrDefault();
    var outOpt = TakeOptions(rest, "--out").LastOrDefault();
    bool pretty = TakeFlag(rest, "--pretty");
    bool allColors = TakeFlag(rest, "--all-colors");
    bool withColors = TakeFlag(rest, "--colors");
    bool namesOnly = TakeFlag(rest, "--names");
    var topArg = TakeOptions(rest, "--top").LastOrDefault();
    if (pretty) json = new JsonSerializerOptions(json) { WriteIndented = true };
    var a = rest.ToArray();

    if (a.Length > 0 && a[0] is "--version" or "-v" or "version")
    {
        Print(new { Name = AppInfo.Name, Version = AppInfo.Version, License = AppInfo.License, Author = AppInfo.Author });
        return 0;
    }

    PluginLoader.LoadDefaults();
    foreach (var d in pluginDirs) PluginLoader.LoadDirectory(d);

    if (a.Length == 0 || a[0] is "-h" or "--help" or "help")
    {
        Console.WriteLine(CoreText.T("""
            Справка и каталоги:
              pixed ops | checks [имя|--names]      операции, проверки (с параметрами; --names только имена, имя одной); pixed plugins загруженные плагины
              pixed palettes [имя|--colors] | templates   библиотеки: палитры (цвета по имени или --colors) и шаблоны (системные и свои); имя подходит вместо пути в run/check/batch
              pixed draw <scene.json|JSON> <out.png>   нарисовать картинку из примитивов (rect, ellipse, line, poly, pixels, fill): плейсхолдеры без пиксельной работы
              pixed draw-sheet <sheet.json|JSON> <out.png> [--meta файл]   нарисовать лист спрайтов из примитивов (rows, frames, base, mirrorOf, copyOf): направления и кадры одним JSON, рядом пишется описание листа
              pixed fetch <http(s)-ссылка на картинку> <out.png|jpg>   скачать картинку (только image/*, до 15 МБ) для обработки в пиксель-арт
              pixed anim-export <sheet.png> <frameW> <frameH> <outDir> <name> [rows=a,b] [delay=0.1] [godotPath=res://x.png] [formats=aseprite,godot,gif,simple,unity] [noloop=a,b]
                                                         экспорт анимации из листа для Unity (Aseprite JSON; formats=unity: скрипт PixelEditorSheetImporter.cs + простой JSON), Godot (.tres), GIF; noloop: строки без цикла
              pixed info <image> [--top N|--all-colors]   размер, число цветов, прозрачность и 8 самых частых цветов (все по --all-colors)
            Обработка:
              pixed apply <op> <in> <out.png> [key=value ...]
              pixed run <pipeline.json|имя шаблона> <in> <out.png> [--protect-mask mask.png] [параметр=значение ...]   коды: 0 успех, 1 ошибка, 2 проверки не пройдены
              pixed check <pipeline.json> <image>               только проверки на готовой картинке
              pixed batch <pipeline.json> <inDir> <outDir> [--recurse] [--pattern *.png;*.jpg] [параметр=значение ...]
            Шаблоны, палитры, стиль:
              pixed validate <pipeline.json|имя|JSON>   проверить шаблон без запуска (код 0 если без проблем)
              pixed save-template <имя> <pipeline.json|JSON> [описание]   сохранить шаблон в библиотеку (виден по имени и в редакторе)
              pixed save-palette <имя> <#RRGGBB> <#RRGGBB> ...   сохранить палитру в библиотеку
              pixed pixels <image> <x> <y> <w> <h>      точные цвета области до 16×16
              pixed compare <image> <шаблон1> [<шаблон2> ...] [--preview out.png]   варианты шаблонов рядом, без записи результата
              pixed palette-build <цветов> <img1> <img2> ... [--fixed #hex]   одна палитра на серию кадров
              pixed style-extract <reference.png> [--max-colors N]   профиль стиля спрайта (JSON)
              pixed style-apply <profile.json> <in.png> <out.png> [--mask m.png | --cutout] [--preview p.png]   привести картинку к стилю
              pixed log [--tail N] [--level info|warning|error] [--stacks] [--files]   журнал ошибок (английский): последние записи, со стеком или список файлов
              pixed chat-prompt [--kind image|sprite|sheet] [--image file] [--compact] [--out file]   запрос для обычного чата с ИИ: image обрабатывает картинку, sprite и sheet рисуют с нуля из примитивов; ответ-JSON запускается через pixed run, draw или draw-sheet
              pixed pipeline-check <answer.txt|JSON>   достать шаблон, сцену или лист из ответа чата и проверить (в ответе Kind: Pipeline, Sprite или Sheet; код 0: годен, 2: есть ошибки)
            Спрайт-листы и анимация:
              pixed slice <sheet.png> <cellW> <cellH> <outDir> [--skip-empty]
              pixed sheet <columns> <out.png> <frame1.png> <frame2.png> ...
              pixed gif <out.gif> <delayMs> <frame1.png> <frame2.png> ... [--no-loop]
              pixed gif <out.gif> <delayMs> --sheet <sheet.png> <cellW> <cellH> [--no-loop]
            Аргументы из файла и stdin, шаблоны имён файлов:
              @путь вместо JSON, шаблона, профиля или списка цветов читает аргумент из файла, - читает его из stdin (UTF-8). В PowerShell и cmd это надёжнее кавычек внутри JSON: pixed run @pipeline.json in.png out.png
              Работает в draw, draw-sheet, run, check, batch, validate, compare, save-template, style-apply, pipeline-check, save-palette (цвета), а в apply значение вида key=@файл. Обычные пути, JSON и имена шаблонов работают как раньше
              * и ? в имени файла раскрываются в sheet, gif, palette-build (frames\*.png; порядок естественный: frame2 раньше frame10; нет совпадений: ошибка)
            RSI (папка name.rsi с meta.json):
              pixed rsi info <папка.rsi>   лицензия, размер кадра, состояния (направления, кадры, задержки, есть ли PNG и совпадает ли его размер)
              pixed rsi new <папка.rsi> <ширина> <высота> [license=...] [copyright=...]   создать папку и meta.json (license и copyright можно не указывать; существующий meta.json не перезаписывается)
              pixed rsi state <папка.rsi> <имя> <лист.png> [directions=1|4|8] [delay=0.1 | delays=0.1,0.2]   записать состояние из листа (строки направления S,N,E,W,SE,SW,NE,NW, столбцы кадры; delays: по кадрам, для разных направлений группы через ;)
              pixed rsi export <папка.rsi> <состояние> <out.png>   записать состояние листом строки×кадры (для anim-export или редактора)
            Опция --plugins <папка> (можно несколько раз) подключает DLL с плагинами. Все ответы JSON в одну строку (--pretty: с отступами).
            Язык сообщений: --lang ru|en или переменная PIXED_LANG (по умолчанию en). pixed --version: версия и лицензия. Журнал ошибок: папка logs в папке данных.
            """));
        return 0;
    }

    switch (a[0])
    {
        case "ops":
            if (namesOnly) { Print(Operations.All.Select(o => o.Name)); return 0; }
            Print(Operations.All.Where(o => a.Length < 2 || string.Equals(o.Name, a[1], StringComparison.OrdinalIgnoreCase))
                .Select(o => new { o.Name, o.Description, o.Parameters, o.Source }));
            return 0;

        case "checks":
            if (namesOnly) { Print(Checks.All.Select(c => c.Name)); return 0; }
            Print(Checks.All.Where(c => a.Length < 2 || string.Equals(c.Name, a[1], StringComparison.OrdinalIgnoreCase))
                .Select(c => new { c.Name, c.Description, c.Parameters, c.Source }));
            return 0;

        case "log":
        {
            // pixed log [--tail N] [--level info|warning|error] [--stacks] [--files]: the error log (always English)
            if (filesFlag) { Print(new { Folder = Log.Directory, Files = Log.Files() }); return 0; }
            var level = Enum.TryParse<LogLevel>(levelOpt ?? "error", true, out var lv) ? lv : LogLevel.Error;
            var entries = Log.Tail(tailOpt != null ? int.Parse(tailOpt) : 20, level, stacksFlag);
            Print(new { Folder = Log.Directory, Level = level.ToString(), Count = entries.Count, Entries = entries });
            return 0;
        }

        case "plugins":
            Print(PluginLoader.Loaded);
            return 0;

        case "palettes":
            // no color list by default (it is rarely needed): --colors for all or a palette name for one
            Print(PixelEngine.Palettes().Where(p => a.Length < 2 || string.Equals(p.Name, a[1], StringComparison.OrdinalIgnoreCase)).Select(p => (object)(
                withColors || a.Length >= 2
                    ? new { p.Name, p.Description, p.IsSystem, ColorCount = p.Colors.Count, Colors = p.Colors.Select(Pixel.ToHex).ToList() }
                    : new { p.Name, p.Description, p.IsSystem, ColorCount = p.Colors.Count, Colors = (List<string>?)null })));
            return 0;

        case "templates":
            Print(PixelEngine.Templates().Select(t => new { t.Name, t.Description, t.IsSystem, t.StepCount, t.CheckCount, Params = PixelEngine.ParamsOf(t.Params), File = t.FilePath }));
            return 0;

        case "draw":
            Need(a, 3);
            Print(PixelEngine.Draw(TextArg(a[1]), a[2]));
            return 0;

        case "draw-sheet":
            Need(a, 3);
            Print(PixelEngine.DrawSheet(TextArg(a[1]), a[2], metaOpt));
            return 0;

        case "rsi":
        {
            Need(a, 2);
            switch (a[1].ToLowerInvariant())
            {
                case "info":
                    Need(a, 3);
                    Print(PixelEngine.RsiInfo(a[2]));
                    return 0;
                case "new":
                {
                    // pixed rsi new <folder.rsi> <width> <height> [license=...] [copyright=...]
                    Need(a, 5);
                    var opt = KeyValues(a.Skip(5), "license", "copyright");
                    Print(PixelEngine.RsiNew(a[2], IntArg(a[3], "width"), IntArg(a[4], "height"), opt.GetValueOrDefault("license"), opt.GetValueOrDefault("copyright")));
                    return 0;
                }
                case "state":
                {
                    // pixed rsi state <folder.rsi> <name> <sheet.png> [directions=1|4|8] [delay=0.1 | delays=...]
                    Need(a, 5);
                    var opt = KeyValues(a.Skip(5), "directions", "delay", "delays");
                    Print(PixelEngine.RsiSetState(a[2], a[3], a[4],
                        opt.TryGetValue("directions", out var dv) ? IntArg(dv, "directions") : null,
                        opt.TryGetValue("delay", out var lv) ? DoubleArg(lv, "delay") : null,
                        opt.GetValueOrDefault("delays")));
                    return 0;
                }
                case "export":
                    Need(a, 5);
                    Print(PixelEngine.RsiExportState(a[2], a[3], a[4]));
                    return 0;
                default:
                    throw new ArgumentException(CoreText.F("Неизвестная подкоманда rsi '{0}': info, new, state или export", a[1]));
            }
        }

        case "fetch":
            Need(a, 3);
            Print(ImageFetcher.Fetch(a[1], a[2]));
            return 0;

        case "anim-export":
        {
            // pixed anim-export <sheet.png> <frameW> <frameH> <outDir> <name> [rows=a,b,c] [delay=0.1] [godotPath=res://x.png] [formats=aseprite,godot,gif,simple,unity] [noloop=a,b]
            Need(a, 6);
            var opt = a.Skip(6).Select(x => x.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0], p => p[1], StringComparer.OrdinalIgnoreCase);
            var formats = (opt.GetValueOrDefault("formats") ?? "aseprite,godot,gif").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            Print(PixelEngine.ExportAnimation(a[1], int.Parse(a[2]), int.Parse(a[3]), opt.TryGetValue("rows", out var r) ? r.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : null,
                double.Parse(opt.GetValueOrDefault("delay") ?? "0.1", System.Globalization.CultureInfo.InvariantCulture), a[4], a[5],
                opt.GetValueOrDefault("godotPath") ?? "res://" + a[5] + ".png",
                formats.Contains("aseprite"), formats.Contains("godot"), formats.Contains("gif"), formats.Contains("simple"),
                opt.TryGetValue("noloop", out var nl) ? nl.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : null, formats.Contains("unity")));
            return 0;
        }

        case "info":
            Need(a, 2);
            Print(PixelEngine.Inspect(a[1], allColors ? 0 : int.TryParse(topArg, out var tn) ? tn : 8));
            return 0;

        case "apply":
        {
            Need(a, 4);
            var stepArgs = new JsonObject();
            foreach (var kv in a.Skip(4))
            {
                int eq = kv.IndexOf('=');
                if (eq <= 0) throw new ArgumentException(CoreText.F("Аргумент '{0}': ожидалось key=value", kv));
                var value = kv[(eq + 1)..];
                // key=@file: the value (usually JSON such as points=[[1,2]]) comes from a file, safe from shell quoting
                if (value.Length > 1 && value[0] == '@') value = inputArgs.Resolve(value).Value.Trim();
                stepArgs[kv[..eq]] = ParseValue(value);
            }
            Print(PixelEngine.Apply(a[1], a[2], a[3], stepArgs));
            return 0;
        }

        case "run":
        {
            var runArgs = a.ToList();
            var mask = TakeOptions(runArgs, "--protect-mask").SingleOrDefault();
            Need(runArgs.ToArray(), 4);
            // anything after the output path is a template parameter override: size=48 colors=16
            var report = PixelEngine.Run(LoadPipelineArg(runArgs[1]), runArgs[2], runArgs[3], mask, PipelineParams.ParseAssignments(runArgs.Skip(4)));
            Print(report);
            return !report.StepsOk ? 1 : report.ChecksPassed == true ? 0 : 2;
        }

        case "check":
        {
            Need(a, 3);
            var report = PixelEngine.Check(LoadPipelineArg(a[1]), a[2], PipelineParams.ParseAssignments(a.Skip(3)));
            Print(report);
            return report.ChecksPassed ? 0 : 2;
        }

        case "validate":
        {
            Need(a, 2);
            var problems = LoadPipelineArg(a[1]).Validate();
            Print(new { Valid = problems.Count == 0, Problems = problems });
            return problems.Count == 0 ? 0 : 2;
        }

        case "save-template":
        {
            Need(a, 3);
            // pixed save-template <name> <pipeline.json|JSON> [description]
            var source = inputArgs.Resolve(a[2]);
            var pipeline = LoadPipelineFrom(source);
            var body = source.Expanded
                ? (source.Value.TrimStart().StartsWith('{') || source.Value.TrimStart().StartsWith('[') ? source.Value : PipelineText.Extract(source.Value))
                : File.Exists(a[2]) ? File.ReadAllText(a[2]) : a[2];
            var saved = PipelineLibrary.Default.Save(a[1], a.Length > 3 ? a[3] : pipeline.Description ?? "", body);
            Print(new { saved.Name, saved.Id, saved.StepCount, saved.CheckCount, File = saved.FilePath });
            return 0;
        }

        case "save-palette":
        {
            Need(a, 3);
            // pixed save-palette <name> <#RRGGBB> <#RRGGBB> ...
            var colors = ColorArgs(a.Skip(2)).Select(h =>Pixel.TryParseHex(h, out var c) ? c : throw new ArgumentException(CoreText.F("Неверный цвет: {0}", h))).ToList();
            var saved = PaletteLibrary.Default.Save(a[1], "", colors);
            Print(new { saved.Name, saved.Id, ColorCount = saved.Colors.Count, File = saved.FilePath });
            return 0;
        }

        case "pixels":
        {
            Need(a, 6);
            // pixed pixels <img> <x> <y> <w> <h>: exact colors of an area up to 16×16
            var img = ImageIO.Load(a[1]);
            int x = int.Parse(a[2]), y = int.Parse(a[3]), w = int.Parse(a[4]), h = int.Parse(a[5]);
            if (w is < 1 or > 16 || h is < 1 or > 16 || x < 0 || y < 0 || x > img.Width - w || y > img.Height - h)
                throw new ArgumentException(CoreText.T("Область должна лежать внутри картинки и быть не больше 16×16"));
            var rows = Enumerable.Range(0, h).Select(r => Enumerable.Range(0, w).Select(c => Pixel.ToHex(img.Get(x + c, y + r))).ToArray()).ToArray();
            Print(new { X = x, Y = y, Rows = rows });
            return 0;
        }

        case "compare":
        {
            Need(a, 3);
            // pixed compare <input> <template1> [template2 ...] [--preview out.png]: variants side by side, without writing the result
            var (variants, preview) = PixelEngine.ComparePipelines(a[1], a.Skip(2).Select(TextArg).ToArray(), null);
            if (previewOpt != null) File.WriteAllBytes(previewOpt, preview);
            Print(new { Variants = variants, Preview = previewOpt });
            return 0;
        }

        case "palette-build":
        {
            Need(a, 3);
            // pixed palette-build <colors> <img1> <img2> ... [--fixed #hex]: one palette for a series of frames
            var fixedColors = fixedOpts.Select(h => Pixel.TryParseHex(h, out var c) ? c : throw new ArgumentException(CoreText.F("Неверный цвет: {0}", h))).ToList();
            var images = FileGlob.Expand(a.Skip(2)).Select(ImageIO.Load).ToList();
            var palette = Quantizer.BuildSharedPalette(images, int.Parse(a[1]), fixedColors);
            Print(new { Palette = palette.Select(Pixel.ToHex).ToArray() });
            return 0;
        }

        case "style-extract":
        {
            Need(a, 2);
            // pixed style-extract <reference.png> [--max-colors N]: sprite style profile (JSON)
            var profile = SpriteStyle.Extract(ImageIO.Load(a[1]), colorsOpt != null ? int.Parse(colorsOpt) : 16);
            Console.WriteLine(SpriteStyle.ToJson(profile));
            return 0;
        }

        case "style-apply":
        {
            Need(a, 4);
            // pixed style-apply <profile.json> <input.png> <out.png> [--mask m.png | --cutout] [--preview p.png]
            var profileSource = inputArgs.Resolve(a[1]);
            var profile = SpriteStyle.Parse(profileSource.Expanded || !File.Exists(a[1]) ? profileSource.Value : File.ReadAllText(a[1]));
            var (report, preview) = PixelEngine.ApplyStyle(profile, a[2], null, a[3], maskOpt, styleCutout, null);
            if (previewOpt != null) File.WriteAllBytes(previewOpt, preview);
            Print(report);
            return 0;
        }

        case "chat-prompt":
        {
            // pixed chat-prompt [--image file] [--compact] [--out file]: a prompt for an ordinary AI chat (no agents)
            ChatPromptImage? info = null;
            if (chatImage != null)
            {
                var img = ImageIO.Load(chatImage);
                info = new ChatPromptImage(img.Width, img.Height, img.DistinctColors(), img.Pixels.Any(p => Pixel.IsEmpty(p)));
            }
            var kind = (kindOpt ?? "image").ToLowerInvariant() switch
            {
                "image" or "pipeline" => ChatKind.Pipeline,
                "sprite" or "scene" => ChatKind.Sprite,
                "sheet" => ChatKind.Sheet,
                _ => throw new ArgumentException(CoreText.T("--kind: image, sprite или sheet")),
            };
            // sheet only: --template <name> | --rows a,b | --frames stand:0.5,step:0.15 | --size 32x32 | --delay 0.2 make the prompt require that layout
            var sheetLayout = SheetLayoutSpec.Parse(null, layoutTemplate, layoutRows, layoutFrames, layoutSize, layoutDelay);
            var text = ChatPrompt.Build(kind, info, chatCompact, sheetLayout);
            if (outOpt != null) { File.WriteAllText(outOpt, text); Print(new { Written = outOpt, Characters = text.Length }); }
            else Console.Write(text);
            return 0;
        }

        case "pipeline-check":
        {
            Need(a, 2);
            // pixed pipeline-check <answer.txt|JSON>: extract a template from a chat answer and check it (code 0: valid, 2: has errors)
            var rawSource = inputArgs.Resolve(a[1]);
            var raw = rawSource.Expanded || !File.Exists(a[1]) ? rawSource.Value : File.ReadAllText(a[1]);
            var extracted = PipelineText.Extract(raw);
            var problems = PipelineText.Check(extracted);
            Print(new { Valid = problems.IsValid, Kind = PipelineText.Detect(extracted).ToString(), problems.Errors, problems.Warnings, Json = extracted });
            return problems.IsValid ? 0 : 2;
        }

        case "batch":
        {
            Need(a, 4);
            var report = PixelEngine.Batch(LoadPipelineArg(a[1]), a[2], a[3], recurse, pattern, PipelineParams.ParseAssignments(a.Skip(4)));
            Print(report);
            return report.ExitCode;
        }

        case "slice":
        {
            Need(a, 5);
            var files = PixelEngine.Slice(a[1], int.Parse(a[2]), int.Parse(a[3]), a[4], skipEmpty);
            Print(new { Count = files.Count, Files = files });
            return 0;
        }

        case "sheet":
        {
            Need(a, 4);
            Print(PixelEngine.Compose(FileGlob.Expand(a.Skip(3)), int.Parse(a[1]), a[2]));
            return 0;
        }

        case "gif":
        {
            Need(a, 4);
            int delay = int.Parse(a[2]);
            if (a[3] == "--sheet")
            {
                Need(a, 7);
                Print(PixelEngine.MakeGifFromSheet(a[4], int.Parse(a[5]), int.Parse(a[6]), delay, a[1], !noLoop));
            }
            else
                Print(PixelEngine.MakeGif(FileGlob.Expand(a.Skip(3)), delay, a[1], !noLoop));
            return 0;
        }

        default:
            throw new ArgumentException(CoreText.F("Неизвестная команда '{0}'. См. pixed help", a[0]));
    }
}
catch (Exception ex)
{
    // command failure: JSON for the agent in stderr, details with the stack go to the log (the logs folder in the data folder)
    Log.Error(ex, "Command failed: pixed " + (args.Length > 0 ? args[0] : "(no command)"));
    Console.Error.WriteLine(JsonSerializer.Serialize(new { Error = ex.Message }, json));
    return 1;
}

void Print(object o) => Console.WriteLine(JsonSerializer.Serialize(o, json));

// @file and - (stdin) forms for arguments that carry JSON or text; a plain argument is returned untouched
string TextArg(string arg) => inputArgs.Resolve(arg).Value;

Pipeline LoadPipelineArg(string arg) => LoadPipelineFrom(inputArgs.Resolve(arg));

Pipeline LoadPipelineFrom(ResolvedArg r) => r.Expanded ? PixelEngine.LoadPipeline(r.Value, r.BaseDir) : PixelEngine.LoadPipeline(r.Value);

// colors for save-palette: #RRGGBB arguments, or @file / - with a JSON array or colors separated by spaces, commas, semicolons or lines
IEnumerable<string> ColorArgs(IEnumerable<string> args)
{
    foreach (var arg in args)
    {
        var r = inputArgs.Resolve(arg);
        if (!r.Expanded) { yield return arg; continue; }
        var text = r.Value.Trim();
        if (text.StartsWith('['))
        {
            foreach (var n in JsonNode.Parse(text)!.AsArray()) yield return n!.GetValue<string>();
        }
        else
            foreach (var t in text.Split([' ', ',', ';', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)) yield return t;
    }
}

// key=value options after positional arguments (rsi commands); unknown keys are an error
static Dictionary<string, string> KeyValues(IEnumerable<string> items, params string[] allowed)
{
    var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var kv in items)
    {
        int eq = kv.IndexOf('=');
        if (eq <= 0) throw new ArgumentException(CoreText.F("Аргумент '{0}': ожидалось key=value", kv));
        var key = kv[..eq];
        if (!allowed.Contains(key, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException(CoreText.F("Неизвестный параметр '{0}'. Допустимые: {1}", key, string.Join(", ", allowed)));
        map[key] = kv[(eq + 1)..];
    }
    return map;
}

static int IntArg(string s, string name) =>
    int.TryParse(s, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v
        : throw new ArgumentException(CoreText.F("{0}: ожидалось целое число, получено '{1}'", name, s));

static double DoubleArg(string s, string name) =>
    double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v
        : throw new ArgumentException(CoreText.F("{0}: ожидалось число, получено '{1}'", name, s));

static void Need(string[] a, int n)
{
    if (a.Length < n) throw new ArgumentException(CoreText.T("Не хватает аргументов. См. pixed help"));
}

static bool TakeFlag(List<string> list, string flag) => list.RemoveAll(x => x == flag) > 0;

static List<string> TakeOptions(List<string> list, string name)
{
    var values = new List<string>();
    for (int i = 0; i < list.Count; i++)
    {
        if (list[i] != name) continue;
        if (i + 1 >= list.Count) throw new ArgumentException(CoreText.F("{0}: нужно значение", name));
        values.Add(list[i + 1]);
        list.RemoveRange(i, 2);
        i--;
    }
    return values;
}

static JsonNode? ParseValue(string s)
{
    if (bool.TryParse(s, out var b)) return b;
    if (double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d)) return d;
    if (s.StartsWith('[') || s.StartsWith('{')) return JsonNode.Parse(s);
    return s;
}
