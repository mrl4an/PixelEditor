// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PixelEditor.Core;
using PixelEditor.Core.Ops;
using PixelEditor.Core.Plugins;
using PixelEditor.Imaging;

// pixed-mcp: an MCP server (stdio, JSON-RPC by lines) on top of PixelEngine.
// Connecting, for example: claude mcp add pixel-editor -- "<path>\pixed-mcp.exe" [--plugins <folder>]
// Plugins: standard folders, --plugins <folder> and the PIXED_PLUGINS environment variable (paths separated by ;).
// Only the protocol goes to stdout, diagnostics go to stderr.

Log.Application = "pixed-mcp";
Log.Info("MCP server " + AppInfo.Version + " started");
var pluginDirs = new List<string>();
for (int i = 0; i < args.Length; i++)
    if (args[i] == "--plugins" && i + 1 < args.Length) pluginDirs.Add(args[++i]);
var env = Environment.GetEnvironmentVariable("PIXED_PLUGINS");
if (!string.IsNullOrWhiteSpace(env)) pluginDirs.AddRange(env.Split(';', StringSplitOptions.RemoveEmptyEntries));

PluginLoader.LoadDefaults();
foreach (var d in pluginDirs) PluginLoader.LoadDirectory(d);

var stdin = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
var jsonOut = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

void Send(JsonObject o) => stdout.WriteLine(o.ToJsonString());

void Reply(JsonNode? id, JsonNode result) =>
    Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result });

void Fail(JsonNode? id, int code, string message) =>
    Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = code, ["message"] = message } });

string? line;
while ((line = stdin.ReadLine()) != null)
{
    if (string.IsNullOrWhiteSpace(line)) continue;
    JsonObject msg;
    try { msg = JsonNode.Parse(line)!.AsObject(); }
    catch (Exception) { Fail(null, -32700, "Parse error"); continue; }

    var method = msg["method"]?.GetValue<string>();
    if (method == null) continue; // we do not need the client's responses
    bool isRequest = msg.ContainsKey("id");
    var id = msg["id"]?.DeepClone();

    try
    {
        JsonNode? result = method switch
        {
            "initialize" => Initialize(msg["params"]),
            "ping" => new JsonObject(),
            "tools/list" => new JsonObject { ["tools"] = Tools.Definitions() },
            "tools/call" => Tools.Call(msg["params"]),
            _ when method.StartsWith("notifications/") => null,
            _ => throw new RpcException(-32601, $"Method not found: {method}"),
        };
        if (isRequest && result != null) Reply(id, result);
    }
    catch (RpcException ex) { if (isRequest) Fail(id, ex.Code, ex.Message); }
    catch (Exception ex)
    {
        Log.Error(ex, "Unhandled error while serving MCP method " + method);
        Console.Error.WriteLine(ex);
        if (isRequest) Fail(id, -32603, ex.Message);
    }
}
return 0;

JsonNode Initialize(JsonNode? p)
{
    var version = p?["protocolVersion"]?.GetValue<string>() ?? "2025-06-18";
    return new JsonObject
    {
        ["protocolVersion"] = version,
        ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
        ["serverInfo"] = new JsonObject { ["name"] = "pixed-mcp", ["version"] = AppInfo.Version },
        ["instructions"] = CoreText.T("Инструменты для обработки пиксель-арта. Начни с list_operations и list_checks, чтобы увидеть доступные операции и проверки. ") +
                           CoreText.T("Правило пользователя превращай в шаблон {steps, checks} и гоняй через run_pipeline: ответ содержит лог шагов, результат проверок и картинку. ") +
                           CoreText.T("Проверки ловят только то, что в них заложено, поэтому смотри картинку результата. Для особых правил есть плагины (list_plugins)."),
    };
}

sealed class RpcException(int code, string message) : Exception(message)
{
    public int Code { get; } = code;
}

static class Tools
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // ---------- tool descriptions ----------

    private static JsonObject Prop(string type, string desc) => type switch
    {
        "string[]" => new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" }, ["description"] = desc },
        _ => new JsonObject { ["type"] = type, ["description"] = desc },
    };

    private static JsonObject Tool(string name, string description, string[] required, params (string Name, string Type, string Desc)[] props)
    {
        var properties = new JsonObject();
        foreach (var (n, t, d) in props) properties[n] = Prop(t, d);
        return new JsonObject
        {
            ["name"] = name,
            ["description"] = description,
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = new JsonArray(required.Select(r => (JsonNode)JsonValue.Create(r)!).ToArray()),
            },
        };
    }

    public static JsonArray Definitions() =>
    [
        Tool("list_operations", CoreText.T("Список операций обработки с описанием и параметрами (включая плагины). names_only=true: только имена; name: одна операция."), [], ("names_only", "boolean", CoreText.T("Только имена (дёшево)")), ("name", "string", CoreText.T("Показать одну операцию"))),
        Tool("list_checks", CoreText.T("Список проверок результата с параметрами (включая плагины). names_only=true: только имена; name: одна проверка."), [], ("names_only", "boolean", CoreText.T("Только имена (дёшево)")), ("name", "string", CoreText.T("Показать одну проверку"))),
        Tool("read_log", CoreText.T("Журнал ошибок и событий (английский): последние записи редактора, pixed и pixed-mcp. level: info|warning|error (по умолчанию error), tail: сколько записей (по умолчанию 20), stacks=true: со стеком, files=true: только список файлов журнала."), [],
            ("level", "string", CoreText.T("info, warning или error (по умолчанию error)")), ("tail", "integer", CoreText.T("Сколько последних записей вернуть (по умолчанию 20)")),
            ("stacks", "boolean", CoreText.T("С трассировкой стека")), ("files", "boolean", CoreText.T("Только список файлов журнала"))),
        Tool("list_plugins", CoreText.T("Загруженные плагины и ошибки загрузки."), []),
        Tool("list_palettes", CoreText.T("Библиотека палитр: системные (PICO-8, DawnBringer 16, ...) и свои. Имя палитры можно указать в шаге applyPalette и проверке onlyPalette как paletteName. По умолчанию без цветов (имя и число цветов); with_colors=true или name: с цветами."), [], ("with_colors", "boolean", CoreText.T("Вернуть списки цветов")), ("name", "string", CoreText.T("Одна палитра с цветами"))),
        Tool("save_palette", CoreText.T("Сохраняет свою палитру в библиотеку (потом доступна по имени и в редакторе)."),
            ["name", "colors"], ("name", "string", CoreText.T("Название (уникальное)")), ("description", "string", CoreText.T("Описание")),
            ("colors", "string[]", CoreText.T("Цвета в формате #RRGGBB"))),
        Tool("list_templates", CoreText.T("Библиотека шаблонов обработки: системные и свои. Имя шаблона можно передать в run_pipeline, check_image и batch_process вместо пути."), []),
        Tool("save_template", CoreText.T("Проверяет шаблон и сохраняет его в библиотеку, чтобы он появился в меню редактора и был доступен по имени."),
            ["name", "pipeline"], ("name", "string", CoreText.T("Название (уникальное)")), ("description", "string", CoreText.T("Описание")),
            ("pipeline", "string", CoreText.T("JSON шаблона строкой: {steps, checks}"))),
        Tool("draw_scene", CoreText.T("Рисует плейсхолдер из примитивов по короткому JSON, без пиксельной работы. Сцена: {\"size\":[w,h], \"palette\":{имя:\"#RRGGBB\"}, \"mirror\":\"x\", \"outline\":\"#000000\", \"items\":[...]}. ") +
                           CoreText.T("Фигуры: {\"rect\":[x,y,w,h]}, {\"ellipse\":[x,y,w,h]}, {\"line\":[x1,y1,x2,y2],\"thickness\":2}, {\"poly\":[[x,y],...]}, {\"pixels\":[[x,y],...]}, {\"fill\":[x,y]}; у каждой \"color\" (#RRGGBB, имя из palette, clear), \"fill\":false только контур, \"mirror\":\"x|y|xy\". Рисуются по порядку, поздние поверх. Возвращает сводку и картинку."),
            ["scene", "output"], ("scene", "string", CoreText.T("JSON сцены строкой или путь к .json")), ("output", "string", CoreText.T("Выходной PNG"))),
        Tool("draw_sheet", CoreText.T("Рисует ЛИСТ спрайтов из примитивов по одному JSON: строки это направления или анимации (S, N, E, W), столбцы кадры. Лист: {\"frameSize\":[w,h], \"palette\":{...}, \"outline\":\"#000000\", \"delay\":0.15, \"base\":[фигуры в каждом кадре], \"rows\":[{\"name\":\"S\", \"base\":[...], \"frames\":[{\"name\":\"stand\", \"delay\":0.5, \"items\":[...], \"offset\":[0,-1], \"copyOf\":\"stand\"}]}, {\"name\":\"W\", \"mirrorOf\":\"E\"}, {\"name\":\"N\", \"copyOf\":\"S\"}]}. ") +
                           CoreText.T("Кадр рисуется как base листа + base строки + items кадра, поэтому в кадрах перечисляют только отличия. Фигуры те же, что у draw_scene. Пишет PNG листа и описание name.sheet.json (размер кадра, строки, имена кадров, задержки); дальше export_animation или импорт в редактор."),
            ["sheet", "output"], ("sheet", "string", CoreText.T("JSON листа строкой или путь к .json")), ("output", "string", CoreText.T("Выходной PNG")), ("meta", "string", CoreText.T("Куда писать описание листа (по умолчанию рядом с PNG)"))),
        Tool("fetch_image", CoreText.T("Скачивает картинку по прямой http(s)-ссылке в файл (только image/*, до 15 МБ). Дальше обработай шаблоном, например run_pipeline с именем «Картинка из интернета в пиксель-арт 64 px». Выбирай свободные по лицензии изображения."),
            ["url", "output"], ("url", "string", CoreText.T("Прямая ссылка на файл картинки")), ("output", "string", CoreText.T("Куда сохранить"))),
        Tool("export_animation", CoreText.T("Экспорт анимации из листа PNG для Unity (Aseprite JSON), Godot 4 (.tres SpriteFrames) и GIF. Строки листа это анимации, столбцы кадры. Лист должен делиться на кадры без остатка."),
            ["sheet", "frame_width", "frame_height", "output_dir", "name"], ("sheet", "string", CoreText.T("Файл листа")), ("frame_width", "integer", CoreText.T("Ширина кадра")), ("frame_height", "integer", CoreText.T("Высота кадра")),
            ("output_dir", "string", CoreText.T("Папка результата")), ("name", "string", CoreText.T("Имя файлов")), ("rows", "string[]", CoreText.T("Названия строк сверху вниз (иначе S N E W для 4 строк)")),
            ("delay", "number", CoreText.T("Задержка кадра, секунды (0.1)")), ("godot_path", "string", CoreText.T("Путь к PNG внутри проекта Godot, например res://art/hero.png")),
            ("formats", "string[]", CoreText.T("aseprite, godot, gif, simple, unity (по умолчанию первые три; unity: скрипт PixelEditorSheetImporter.cs и простой JSON)")),
            ("no_loop", "string[]", CoreText.T("Названия строк, анимации которых не зацикливаются (Godot, простой JSON)"))),
        Tool("rsi_info", CoreText.T("RSI: метаданные папки .rsi (лицензия, авторские права, размер кадра) и состояния: направления, кадры, задержки, есть ли PNG и совпадает ли его размер с кадрами×направлениями."),
            ["folder"], ("folder", "string", CoreText.T("Папка .rsi"))),
        Tool("rsi_new", CoreText.T("Создаёт папку .rsi с meta.json без состояний. license и copyright можно не указывать. Существующий meta.json не перезаписывается."),
            ["folder", "width", "height"], ("folder", "string", CoreText.T("Папка .rsi")), ("width", "integer", CoreText.T("Ширина кадра")), ("height", "integer", CoreText.T("Высота кадра")),
            ("license", "string", CoreText.T("Лицензия, например CC-BY-SA-3.0 (можно пусто)")), ("copyright", "string", CoreText.T("Авторские права (можно пусто)"))),
        Tool("rsi_set_state", CoreText.T("Записывает или обновляет одно состояние RSI из листа PNG: строки направления (S, N, E, W, SE, SW, NE, NW), столбцы кадры. Число кадров берётся из ширины листа и размера кадра в meta.json. Копирует PNG как <name>.png и обновляет в meta.json только это состояние."),
            ["folder", "name", "sheet"], ("folder", "string", CoreText.T("Папка .rsi (после rsi_new)")), ("name", "string", CoreText.T("Имя состояния")), ("sheet", "string", CoreText.T("Лист PNG")),
            ("directions", "integer", CoreText.T("1, 4 или 8 (по умолчанию по высоте листа)")), ("delay", "number", CoreText.T("Задержка кадра, секунды (по умолчанию 0.1 для анимации)")),
            ("delays", "string", CoreText.T("Задержки по кадрам через запятую, например 0.1,0.2,0.1; для разных направлений группы через ; (вместо delay)"))),
        Tool("rsi_export_state", CoreText.T("Записывает состояние RSI листом строки×кадры (строки направления, столбцы кадры) и показывает его; лист годится для export_animation и редактора."),
            ["folder", "state", "output"], ("folder", "string", CoreText.T("Папка .rsi")), ("state", "string", CoreText.T("Имя состояния")), ("output", "string", CoreText.T("Выходной PNG"))),
        Tool("image_info", CoreText.T("Размер, число непрозрачных и прозрачных пикселей, число цветов, полупрозрачные и самые частые цвета (по умолчанию 8; top=0 для всех)."),
            ["path"], ("path", "string", CoreText.T("Путь к изображению")), ("top", "integer", CoreText.T("Сколько самых частых цветов вернуть (по умолчанию 8, 0: все)"))),
        Tool("view_image", CoreText.T("Показывает изображение (мелкие увеличиваются, прозрачность шахматкой)."),
            ["path"], ("path", "string", CoreText.T("Путь к изображению")), ("max_side", "integer", CoreText.T("Целевой размер большей стороны, по умолчанию 256"))),
        Tool("inspect_pixels", "Read exact pixel colors in a region up to 16×16, using zero-based coordinates.",
            ["path", "x", "y", "width", "height"], ("path", "string", "Source image path"),
            ("x", "integer", "Left"), ("y", "integer", "Top"),
            ("width", "integer", "Width, 1 to 16"), ("height", "integer", "Height, 1 to 16")),
        Tool("apply_operation", CoreText.T("Применяет одну операцию. Параметры операции в args (см. list_operations). Возвращает сводку и картинку."),
            ["op", "input", "output"], ("op", "string", CoreText.T("Имя операции")), ("input", "string", CoreText.T("Входной файл")),
            ("output", "string", CoreText.T("Выходной PNG")), ("args", "object", CoreText.T("Параметры операции, например {\"width\":32,\"method\":\"mode\"}"))),
        Tool("run_pipeline", CoreText.T("Выполняет шаблон {name, steps:[{op,...}], checks:[{check,...}]} над картинкой. Возвращает лог шагов, результат проверок и картинку. ") +
                             CoreText.T("success=false при проваленных обязательных проверках: картинка всё равно сохраняется, поправь шаблон и повтори."),
            ["pipeline", "input", "output"], ("pipeline", "string", CoreText.T("Путь к .json шаблону или сам JSON шаблона строкой")),
            ("input", "string", CoreText.T("Входной файл")), ("output", "string", CoreText.T("Выходной PNG")),
            ("protect_mask", "string", "Optional PNG matching the input and result size. Nontransparent pixels preserve the original."),
            ("params", "object", "Optional template parameter values, e.g. {\"size\":48,\"colors\":16}; list_templates shows each template's parameters. Omitted ones use defaults.")),
        Tool("compare_pipelines", "Preview one to four pipeline variants without writing files.",
            ["input", "pipelines"], ("input", "string", "Source image path"),
            ("pipelines", "string[]", "Pipeline names, paths or JSON strings in display order"),
            ("protect_mask", "string", "Optional PNG matching the input and result size. Nontransparent pixels preserve the original.")),
        Tool("build_shared_palette", "Build a single palette from several images, optionally reserving exact accent colors.",
            ["paths", "colors"], ("paths", "string[]", "Input paths, from one to 64 images"),
            ("colors", "integer", "Maximum palette size, 1 to 256"),
            ("fixed_colors", "string[]", "Optional opaque colors in #RRGGBB format")),
        Tool("extract_style", "Extract an editable profile from a finished sprite: canvas, content box, palette and a candidate flat outline.",
            ["reference"], ("reference", "string", "Reference sprite path, preferably with a transparent background"),
            ("max_colors", "integer", "Maximum colors to keep, 1 to 256 (default 16)")),
        Tool("apply_style", "Apply a style profile to a photo, sprite or JSON scene. Returns a side-by-side preview. Omit output for preview only.",
            ["profile"], ("profile", "object", "Profile returned by extract_style, optionally edited"),
            ("input", "string", "Source image path"), ("scene", "string", "Scene JSON or path; use instead of input"),
            ("output", "string", "Optional output PNG path"), ("reference", "string", "Optional reference path for comparison"),
            ("mask", "string", "Optional PNG matching input size; opaque pixels keep the subject"),
            ("cutout", "boolean", "Use automatic background removal instead of a mask; default false")),
        Tool("check_image", CoreText.T("Прогоняет только блок checks шаблона на готовой картинке."),
            ["pipeline", "image"], ("pipeline", "string", CoreText.T("Путь к .json или JSON строкой")), ("image", "string", CoreText.T("Проверяемая картинка")),
            ("params", "object", "Optional template parameter values, e.g. {\"size\":48,\"colors\":16}; list_templates shows each template's parameters. Omitted ones use defaults.")),
        Tool("validate_pipeline", CoreText.T("Проверяет шаблон без запуска: корректность JSON и существование всех операций и проверок."),
            ["pipeline"], ("pipeline", "string", CoreText.T("Путь к .json или JSON строкой"))),
        Tool("save_pipeline", CoreText.T("Проверяет шаблон и сохраняет его в файл (например в папку pipelines, чтобы он появился в меню редактора)."),
            ["path", "pipeline"], ("path", "string", CoreText.T("Куда сохранить .json")), ("pipeline", "string", CoreText.T("JSON шаблона строкой"))),
        Tool("batch_process", CoreText.T("Обрабатывает все картинки папки одним шаблоном и возвращает отчёт по каждому файлу (ok, checks_failed, error)."),
            ["pipeline", "input_dir", "output_dir"], ("pipeline", "string", CoreText.T("Путь к .json или JSON строкой")), ("input_dir", "string", CoreText.T("Папка с картинками")),
            ("output_dir", "string", CoreText.T("Папка для результатов (PNG)")), ("recurse", "boolean", CoreText.T("Включая подпапки")),
            ("pattern", "string", CoreText.T("Расширения через ;, например *.png;*.jpg")),
            ("params", "object", "Optional template parameter values, e.g. {\"size\":48,\"colors\":16}; list_templates shows each template's parameters. Omitted ones use defaults.")),
        Tool("slice_sheet", CoreText.T("Режет спрайт-лист на ячейки cell_RR_CC.png."),
            ["sheet", "cell_width", "cell_height", "output_dir"], ("sheet", "string", CoreText.T("Файл листа")), ("cell_width", "integer", CoreText.T("Ширина ячейки")),
            ("cell_height", "integer", CoreText.T("Высота ячейки")), ("output_dir", "string", CoreText.T("Папка для ячеек")), ("skip_empty", "boolean", CoreText.T("Пропускать пустые ячейки"))),
        Tool("compose_sheet", CoreText.T("Собирает картинки в спрайт-лист."),
            ["inputs", "columns", "output"], ("inputs", "string[]", CoreText.T("Файлы кадров по порядку")), ("columns", "integer", CoreText.T("Число столбцов (0 = одна строка)")),
            ("output", "string", CoreText.T("Выходной PNG"))),
        Tool("make_gif", CoreText.T("Делает GIF-анимацию из списка кадров (frames) или из ячеек листа (sheet + cell_width + cell_height)."),
            ["output", "delay_ms"], ("output", "string", CoreText.T("Выходной .gif")), ("delay_ms", "integer", CoreText.T("Задержка кадра, мс")),
            ("frames", "string[]", CoreText.T("Файлы кадров одного размера")), ("sheet", "string", CoreText.T("Файл листа (вместо frames)")),
            ("cell_width", "integer", CoreText.T("Ширина ячейки")), ("cell_height", "integer", CoreText.T("Высота ячейки")), ("loop", "boolean", CoreText.T("Зацикливать, по умолчанию true"))),
    ];

    // ---------- call ----------

    public static JsonNode Call(JsonNode? p)
    {
        var name = p?["name"]?.GetValue<string>() ?? throw new RpcException(-32602, CoreText.T("Не указано имя инструмента"));
        var a = p?["arguments"] as JsonObject ?? new JsonObject();
        try
        {
            var (text, preview) = Run(name, a);
            var content = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = text } };
            if (preview != null)
                content.Add(new JsonObject { ["type"] = "image", ["data"] = Convert.ToBase64String(preview), ["mimeType"] = "image/png" });
            return new JsonObject { ["content"] = content };
        }
        catch (RpcException) { throw; }
        catch (Exception ex)
        {
            Log.Error(ex, "Tool call failed: " + name);
            return new JsonObject
            {
                ["isError"] = true,
                ["content"] = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = CoreText.F("Ошибка: {0}", ex.Message) } },
            };
        }
    }

    private static string S(JsonObject a, string key) =>
        a[key]?.GetValue<string>() ?? throw new ArgumentException(CoreText.F("Не указан параметр '{0}'", key));

    private static int I(JsonObject a, string key) =>
        a[key] != null ? (int)a[key]!.GetValue<double>() : throw new ArgumentException(CoreText.F("Не указан параметр '{0}'", key));

    /// <summary>Optional "params" object: template parameter overrides (values may be JSON numbers, booleans or strings).</summary>
    private static IEnumerable<KeyValuePair<string, object?>>? ParamOverrides(JsonObject a) =>
        a["params"] is JsonObject o
            ? o.Select(kv => new KeyValuePair<string, object?>(kv.Key, kv.Value is null ? null : System.Text.Json.JsonSerializer.SerializeToElement(kv.Value))).ToList()
            : null;

    private static bool B(JsonObject a, string key, bool def) => a[key]?.GetValue<bool>() ?? def;

    private static string Ser(object o) => JsonSerializer.Serialize(o, Json);

    private static byte[]? PreviewOf(string path)
    {
        try { return PixelEngine.RenderPreview(ImageIO.Load(path)); }
        catch { return null; }
    }

    private static List<string> Problems(Pipeline p)
    {
        var errors = new List<string>();
        for (int i = 0; i < p.Steps.Count; i++)
        {
            var op = p.Steps[i].GetProperty("op").GetString()!;
            try { Operations.Get(op); } catch (Exception ex) { errors.Add(CoreText.F("шаг {0}: {1}", i + 1, ex.Message)); }
        }
        for (int i = 0; i < p.CheckSteps.Count; i++)
        {
            var c = p.CheckSteps[i].GetProperty("check").GetString()!;
            try { Checks.Get(c); } catch (Exception ex) { errors.Add(CoreText.F("проверка {0}: {1}", i + 1, ex.Message)); }
        }
        return errors;
    }

    private static (string Text, byte[]? Preview) Run(string name, JsonObject a)
    {
        switch (name)
        {
            case "list_operations":
                if (B(a, "names_only", false)) return (Ser(Operations.All.Select(o => o.Name)), null);
                return (Ser(Operations.All.Where(o => a["name"] == null || string.Equals(o.Name, S(a, "name"), StringComparison.OrdinalIgnoreCase)).Select(o => new { o.Name, o.Description, o.Parameters, o.Source })), null);
            case "list_checks":
                if (B(a, "names_only", false)) return (Ser(Checks.All.Select(c => c.Name)), null);
                return (Ser(Checks.All.Where(c => a["name"] == null || string.Equals(c.Name, S(a, "name"), StringComparison.OrdinalIgnoreCase)).Select(c => new { c.Name, c.Description, c.Parameters, c.Source })), null);
            case "read_log":
            {
                if (B(a, "files", false)) return (Ser(new { Folder = Log.Directory, Files = Log.Files() }), null);
                var level = Enum.TryParse<LogLevel>(a["level"]?.GetValue<string>() ?? "error", true, out var lv) ? lv : LogLevel.Error;
                var entries = Log.Tail(a["tail"] != null ? I(a, "tail") : 20, level, B(a, "stacks", false));
                return (Ser(new { Folder = Log.Directory, Level = level.ToString(), Count = entries.Count, Entries = entries }), null);
            }
            case "list_plugins":
                return (Ser(PluginLoader.Loaded), null);
            case "list_palettes":
                return (Ser(PixelEngine.Palettes()
                    .Where(p => a["name"] == null || string.Equals(p.Name, S(a, "name"), StringComparison.OrdinalIgnoreCase))
                    .Select(p => new
                    {
                        p.Name, p.Description, p.IsSystem, ColorCount = p.Colors.Count,
                        Colors = a["name"] != null || B(a, "with_colors", false) ? p.Colors.Select(Pixel.ToHex).ToList() : null,
                    })), null);
            case "save_palette":
            {
                var colors = (a["colors"] as JsonArray ?? throw new ArgumentException(CoreText.T("Не указан параметр 'colors'")))
                    .Select(n => Pixel.TryParseHex(n!.GetValue<string>(), out var c) ? c : throw new ArgumentException(CoreText.F("Неверный цвет: {0}", n)))
                    .ToList();
                var saved = PaletteLibrary.Default.Save(S(a, "name"), a["description"]?.GetValue<string>() ?? "", colors);
                return (Ser(new { Saved = saved.Name, saved.Id, ColorCount = saved.Colors.Count, saved.FilePath }), null);
            }
            case "list_templates":
                return (Ser(PixelEngine.Templates().Select(t => new { t.Name, t.Description, t.IsSystem, t.StepCount, t.CheckCount, Params = PixelEngine.ParamsOf(t.Params) })), null);
            case "save_template":
            {
                var saved = PipelineLibrary.Default.Save(S(a, "name"), a["description"]?.GetValue<string>() ?? "", S(a, "pipeline"));
                return (Ser(new { Saved = saved.Name, saved.Id, saved.StepCount, saved.CheckCount, saved.FilePath }), null);
            }
            case "draw_scene":
            {
                var s = PixelEngine.Draw(S(a, "scene"), S(a, "output"));
                return (Ser(s), PreviewOf(s.File));
            }
            case "draw_sheet":
            {
                var s = PixelEngine.DrawSheet(S(a, "sheet"), S(a, "output"), a["meta"]?.GetValue<string>());
                return (Ser(s), PreviewOf(s.File));
            }
            case "fetch_image":
            {
                var f = PixelEngine.Fetch(S(a, "url"), S(a, "output"));
                return (Ser(f), PreviewOf(f.File));
            }
            case "export_animation":
            {
                var formats = (a["formats"] as JsonArray)?.Select(n => n!.GetValue<string>()).ToList() ?? ["aseprite", "godot", "gif"];
                var rows = (a["rows"] as JsonArray)?.Select(n => n!.GetValue<string>()).ToList();
                var report = PixelEngine.ExportAnimation(S(a, "sheet"), I(a, "frame_width"), I(a, "frame_height"), rows, a["delay"]?.GetValue<double>() ?? 0.1,
                    S(a, "output_dir"), S(a, "name"), a["godot_path"]?.GetValue<string>() ?? "res://" + S(a, "name") + ".png",
                    formats.Contains("aseprite"), formats.Contains("godot"), formats.Contains("gif"), formats.Contains("simple"),
                    (a["no_loop"] as JsonArray)?.Select(n => n!.GetValue<string>()).ToList(), formats.Contains("unity"));
                return (Ser(report), null);
            }
            case "rsi_info":
                return (Ser(PixelEngine.RsiInfo(S(a, "folder"))), null);
            case "rsi_new":
                return (Ser(PixelEngine.RsiNew(S(a, "folder"), I(a, "width"), I(a, "height"), a["license"]?.GetValue<string>(), a["copyright"]?.GetValue<string>())), null);
            case "rsi_set_state":
                return (Ser(PixelEngine.RsiSetState(S(a, "folder"), S(a, "name"), S(a, "sheet"),
                    a["directions"] != null ? I(a, "directions") : null, a["delay"]?.GetValue<double>(), a["delays"]?.GetValue<string>())), null);
            case "rsi_export_state":
            {
                var r = PixelEngine.RsiExportState(S(a, "folder"), S(a, "state"), S(a, "output"));
                return (Ser(r), PreviewOf(r.Output));
            }
            case "image_info":
                return (Ser(PixelEngine.Inspect(S(a, "path"), a["top"] != null ? I(a, "top") : 8)), null);
            case "view_image":
            {
                var path = S(a, "path");
                int side = a["max_side"] != null ? I(a, "max_side") : 256;
                var img = ImageIO.Load(path);
                return ($"{img.Width}×{img.Height}", PixelEngine.RenderPreview(img, side));
            }
            case "inspect_pixels":
            {
                var img = ImageIO.Load(S(a, "path"));
                var x = I(a, "x");
                var y = I(a, "y");
                var width = I(a, "width");
                var height = I(a, "height");
                if (width is < 1 or > 16 || height is < 1 or > 16 ||
                    x < 0 || y < 0 || x > img.Width - width || y > img.Height - height)
                    throw new ArgumentException("The region must be inside the image and at most 16×16.");

                var region = new PixelImage(width, height);
                var rows = new string[height][];
                for (int row = 0; row < height; row++)
                {
                    rows[row] = new string[width];
                    for (int col = 0; col < width; col++)
                    {
                        uint pixel = img.Get(x + col, y + row);
                        region.Set(col, row, pixel);
                        rows[row][col] = Pixel.A(pixel) is 0 or 255 ? Pixel.ToHex(pixel) : $"#{pixel:X8}";
                    }
                }
                return (Ser(new { X = x, Y = y, Rows = rows, PartialAlphaFormat = "#AARRGGBB" }),
                    PixelEngine.RenderPreview(region));
            }
            case "apply_operation":
            {
                var s = PixelEngine.Apply(S(a, "op"), S(a, "input"), S(a, "output"), a["args"] as JsonObject);
                return (Ser(s), PreviewOf(s.File));
            }
            case "run_pipeline":
            {
                var report = PixelEngine.Run(PixelEngine.LoadPipeline(S(a, "pipeline")), S(a, "input"), S(a, "output"),
                    a["protect_mask"]?.GetValue<string>(), ParamOverrides(a));
                return (Ser(report), report.Output != null ? PreviewOf(report.Output) : null);
            }
            case "compare_pipelines":
            {
                var pipelines = a["pipelines"] as JsonArray ?? throw new ArgumentException("Expected a pipelines array.");
                var (variants, preview) = PixelEngine.ComparePipelines(S(a, "input"),
                    pipelines.Select(p => p?.GetValue<string>() ?? throw new ArgumentException("Expected a pipeline string.")).ToArray(),
                    a["protect_mask"]?.GetValue<string>());
                return (Ser(new
                {
                    Columns = new[] { "original", "result", "difference" },
                    Difference = "Same-size images only. Green: added, red: removed, orange: color or alpha changed.",
                    Variants = variants,
                }), preview);
            }
            case "build_shared_palette":
            {
                var paths = a["paths"] as JsonArray;
                if (paths == null || paths.Count is < 1 or > 64)
                    throw new ArgumentException("Supply one to 64 image paths.");
                var fixedColors = new List<uint>();
                if (a["fixed_colors"] is JsonArray values)
                    foreach (var value in values)
                    {
                        if (!Pixel.TryParseHex(value?.GetValue<string>(), out uint color) || Pixel.IsEmpty(color))
                            throw new ArgumentException("Fixed palette colors must be opaque hex colors.");
                        fixedColors.Add(color);
                    }
                var images = paths.Select(p => ImageIO.Load(p?.GetValue<string>() ?? throw new ArgumentException("Expected an image path.")));
                var palette = Quantizer.BuildSharedPalette(images, I(a, "colors"), fixedColors);
                return (Ser(new { Palette = palette.Select(Pixel.ToHex).ToArray() }), null);
            }
            case "extract_style":
            {
                var reference = ImageIO.Load(S(a, "reference"));
                var profile = SpriteStyle.Extract(reference, a["max_colors"] == null ? 16 : I(a, "max_colors"));
                return (SpriteStyle.ToJson(profile), PixelEngine.RenderPreview(reference));
            }
            case "apply_style":
            {
                var profile = SpriteStyle.Parse((a["profile"] as JsonObject ??
                    throw new ArgumentException("Expected a style profile object.")).ToJsonString());
                var input = a["input"]?.GetValue<string>();
                var scene = a["scene"]?.GetValue<string>();
                var mask = a["mask"]?.GetValue<string>();
                var reference = a["reference"]?.GetValue<string>();
                bool cutout = B(a, "cutout", false);
                var (report, preview) = PixelEngine.ApplyStyle(profile, input, scene, a["output"]?.GetValue<string>(), mask, cutout, reference);
                var columns = new List<string> { "source" };
                if (mask != null || cutout) columns.Add("masked subject");
                columns.Add("styled result");
                if (reference != null) columns.Add("reference");
                return (Ser(new { Result = report, Columns = columns }), preview);
            }
            case "check_image":
                return (Ser(PixelEngine.Check(PixelEngine.LoadPipeline(S(a, "pipeline")), S(a, "image"), ParamOverrides(a))), null);
            case "validate_pipeline":
            {
                var p = PixelEngine.LoadPipeline(S(a, "pipeline"));
                var problems = Problems(p);
                return (Ser(new { Valid = problems.Count == 0, p.Name, Steps = p.Steps.Count, Checks = p.CheckSteps.Count, Problems = problems }), null);
            }
            case "save_pipeline":
            {
                var p = PixelEngine.LoadPipeline(S(a, "pipeline"));
                var problems = Problems(p);
                if (problems.Count > 0) throw new ArgumentException(CoreText.T("Шаблон не сохранён: ") + string.Join("; ", problems));
                var path = Path.GetFullPath(S(a, "path"));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, S(a, "pipeline").TrimStart().StartsWith('{') || S(a, "pipeline").TrimStart().StartsWith('[')
                    ? S(a, "pipeline") : File.ReadAllText(S(a, "pipeline")));
                return (Ser(new { Saved = path, p.Name }), null);
            }
            case "batch_process":
                return (Ser(PixelEngine.Batch(PixelEngine.LoadPipeline(S(a, "pipeline")), S(a, "input_dir"), S(a, "output_dir"),
                    B(a, "recurse", false), a["pattern"]?.GetValue<string>(), ParamOverrides(a))), null);
            case "slice_sheet":
            {
                var files = PixelEngine.Slice(S(a, "sheet"), I(a, "cell_width"), I(a, "cell_height"), S(a, "output_dir"), B(a, "skip_empty", false));
                return (Ser(new { Count = files.Count, Files = files }), null);
            }
            case "compose_sheet":
            {
                var inputs = (a["inputs"] as JsonArray ?? throw new ArgumentException(CoreText.T("Не указан параметр 'inputs'")))
                    .Select(n => n!.GetValue<string>()).ToList();
                var s = PixelEngine.Compose(inputs, I(a, "columns"), S(a, "output"));
                return (Ser(s), PreviewOf(s.File));
            }
            case "make_gif":
            {
                bool loop = B(a, "loop", true);
                int delay = I(a, "delay_ms");
                var gif = a["sheet"] != null
                    ? PixelEngine.MakeGifFromSheet(S(a, "sheet"), I(a, "cell_width"), I(a, "cell_height"), delay, S(a, "output"), loop)
                    : PixelEngine.MakeGif((a["frames"] as JsonArray ?? throw new ArgumentException(CoreText.T("Нужны frames или sheet")))
                        .Select(n => n!.GetValue<string>()).ToList(), delay, S(a, "output"), loop);
                return (Ser(gif), null);
            }
            default:
                throw new RpcException(-32602, CoreText.F("Неизвестный инструмент: {0}", name));
        }
    }
}
