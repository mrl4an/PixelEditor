// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Text;
using System.Text.Json;

namespace PixelEditor.Core.Ops;

/// <summary>What a chat answer contains: a processing pipeline for the current picture, a sprite drawn from primitives (a scene), or a whole sheet of frames.</summary>
public enum ChatKind { Pipeline, Sprite, Sheet }

/// <summary>Remarks on a chat template: errors prevent running, warnings do not (for example an unknown parameter).</summary>
public sealed record PipelineProblems(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Working with a template the user got from an AI in an ordinary chat (no agents): pulls the JSON out of an answer with explanations and markdown blocks,
/// fixes typical small things (smart quotes, a trailing comma, comments), checks steps and checks and suggests the right names.
/// </summary>
public static class PipelineText
{
    private static readonly JsonDocumentOptions Tolerant = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

    /// <summary>
    /// Finds the template JSON in the text: a ```json block, then any ``` block, then the first paired { } or [ ] in the whole text.
    /// Returns the first variant that parses as JSON and looks like a template. If none: a FormatException with a clear message.
    /// </summary>
    public static string Extract(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new FormatException(CoreText.T("Текст пуст. Вставьте ответ чата целиком."));
        text = Normalize(text);

        string? lastError = null;
        foreach (var source in Candidates(text))
            foreach (var json in BalancedBlocks(source))
            {
                try
                {
                    using var doc = JsonDocument.Parse(json, Tolerant);
                    if (LooksLikeAnswer(doc.RootElement)) return json;
                }
                catch (JsonException ex) { lastError = ex.Message; }
            }

        throw new FormatException(lastError != null
            ? CoreText.F("JSON найден, но он не разбирается: {0}", lastError)
            : CoreText.T("В тексте не найден JSON ответа: шаблон (steps и checks), спрайт (size и items) или лист (frameSize и rows). Вставьте ответ чата целиком."));
    }

    /// <summary>What the JSON is: a scene has "items", a sheet has "rows", everything else is a pipeline.</summary>
    public static ChatKind Detect(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, Tolerant);
            return KindOf(doc.RootElement);
        }
        catch (JsonException) { return ChatKind.Pipeline; }
    }

    private static ChatKind KindOf(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return ChatKind.Pipeline;
        if (root.TryGetProperty("rows", out var r) && r.ValueKind == JsonValueKind.Array) return ChatKind.Sheet;
        if (root.TryGetProperty("items", out var i) && i.ValueKind == JsonValueKind.Array && !root.TryGetProperty("steps", out _)) return ChatKind.Sprite;
        return ChatKind.Pipeline;
    }

    /// <summary>Validates a template: syntax, the list of steps and checks, operation names (with a "did you mean" hint), unknown parameters.</summary>
    public static PipelineProblems Check(string json)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var kind = Detect(json);
        if (kind != ChatKind.Pipeline) return CheckScene(json, kind);
        Pipeline pipeline;
        try { pipeline = Pipeline.Parse(json); }
        catch (JsonException ex) { return new([CoreText.F("Ошибка JSON: {0}", ex.Message)], warnings); }
        catch (FormatException ex) { return new([ex.Message], warnings); }

        if (pipeline.Steps.Count == 0 && pipeline.CheckSteps.Count == 0)
            errors.Add(CoreText.T("В шаблоне нет ни шагов, ни проверок"));

        var ops = Operations.All.ToDictionary(o => o.Name, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < pipeline.Steps.Count; i++)
        {
            var step = pipeline.Steps[i];
            string name = step.GetProperty("op").GetString() ?? "";
            if (!ops.TryGetValue(name, out var info))
            {
                errors.Add(CoreText.F("Шаг {0}: неизвестная операция «{1}».{2}", i + 1, name, Hint(name, ops.Keys)));
                continue;
            }
            WarnUnknownParameters(step, "op", info.Parameters, CoreText.F("Шаг {0} ({1})", i + 1, info.Name), warnings, "when");
        }

        var checks = Checks.All.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < pipeline.CheckSteps.Count; i++)
        {
            var step = pipeline.CheckSteps[i];
            string name = step.GetProperty("check").GetString() ?? "";
            if (!checks.TryGetValue(name, out var info))
            {
                errors.Add(CoreText.F("Проверка {0}: неизвестная проверка «{1}».{2}", i + 1, name, Hint(name, checks.Keys)));
                continue;
            }
            WarnUnknownParameters(step, "check", info.Parameters, CoreText.F("Проверка {0} ({1})", i + 1, info.Name), warnings, "required", "when");
        }
        errors.AddRange(pipeline.ValidateParams());   // "$name" references, "when" and the params list
        return new(errors, warnings);
    }

    /// <summary>
    /// The same JSON laid out for editing by hand: short objects and arrays stay on one line ({"rect": [2, 2, 8, 8], "color": "skin"}),
    /// longer ones are split one item per line. Not JSON: returns the text as it is.
    /// </summary>
    public static string Pretty(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, Tolerant);
            var sb = new StringBuilder();
            WritePretty(doc.RootElement, sb, 0);
            return sb.ToString();
        }
        catch (JsonException) { return json; }
    }

    private const int LineWidth = 100;

    private static void WritePretty(JsonElement e, StringBuilder sb, int level)
    {
        var compact = Compact(e);
        if (e.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array) || compact.Length + level * 2 <= LineWidth || compact.Length <= 2)
        {
            sb.Append(compact);
            return;
        }
        string pad = new(' ', (level + 1) * 2);
        bool obj = e.ValueKind == JsonValueKind.Object;
        sb.Append(obj ? "{\n" : "[\n");
        bool first = true;
        if (obj)
            foreach (var p in e.EnumerateObject())
            {
                if (!first) sb.Append(",\n");
                first = false;
                sb.Append(pad).Append(Quote(p.Name)).Append(": ");
                WritePretty(p.Value, sb, level + 1);
            }
        else
            foreach (var item in e.EnumerateArray())
            {
                if (!first) sb.Append(",\n");
                first = false;
                sb.Append(pad);
                WritePretty(item, sb, level + 1);
            }
        sb.Append('\n').Append(new string(' ', level * 2)).Append(obj ? '}' : ']');
    }

    private static string Compact(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Object => "{" + string.Join(", ", e.EnumerateObject().Select(p => Quote(p.Name) + ": " + Compact(p.Value))) + "}",
        JsonValueKind.Array => "[" + string.Join(", ", e.EnumerateArray().Select(Compact)) + "]",
        _ => e.GetRawText(),
    };

    private static string Quote(string name) =>
        JsonSerializer.Serialize(name, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    /// <summary>A ready message that can be sent back to the chat so the AI fixes its answer.</summary>
    public static string FixRequest(PipelineProblems problems)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Your pipeline JSON did not pass validation in Pixel Editor. Fix these problems and reply with the corrected JSON only (one ```json block):");
        foreach (var e in problems.Errors) sb.AppendLine("- " + e);
        foreach (var w in problems.Warnings) sb.AppendLine("- (warning) " + w);
        sb.AppendLine("Use only the operations, checks and parameters from the catalog I gave you.");
        return sb.ToString().TrimEnd();
    }

    private static readonly string[] ShapeKeys = ["rect", "ellipse", "line", "poly", "pixels", "fill", "group"];
    private static readonly string[] SpriteKeys = ["name", "description", "size", "width", "height", "palette", "mirror", "outline", "background", "items"];
    private static readonly string[] SheetKeys = ["name", "description", "frameSize", "palette", "mirror", "outline", "background", "delay", "base", "rows"];

    /// <summary>Checks a sprite scene or a sheet: item keys with "did you mean" hints first, then a trial render that reports the first real error.</summary>
    private static PipelineProblems CheckScene(string json, ChatKind kind)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(json, Tolerant);
            var root = doc.RootElement;
            foreach (var prop in root.EnumerateObject())
                if (!(kind == ChatKind.Sprite ? SpriteKeys : SheetKeys).Contains(prop.Name, StringComparer.OrdinalIgnoreCase))
                    warnings.Add(CoreText.F("Неизвестное поле «{0}»{1}", prop.Name, Hint(prop.Name, kind == ChatKind.Sprite ? SpriteKeys : SheetKeys, brief: true)));

            if (kind == ChatKind.Sprite)
                CheckItems(root.GetProperty("items"), CoreText.T("Фигура"), errors);
            else
            {
                if (root.TryGetProperty("base", out var b) && b.ValueKind == JsonValueKind.Array) CheckItems(b, CoreText.T("base: фигура"), errors);
                foreach (var row in root.GetProperty("rows").EnumerateArray())
                {
                    string rn = row.ValueKind == JsonValueKind.Object && row.TryGetProperty("name", out var n) ? n.ToString() : "?";
                    if (row.ValueKind != JsonValueKind.Object) continue;
                    if (row.TryGetProperty("base", out var rb) && rb.ValueKind == JsonValueKind.Array) CheckItems(rb, CoreText.F("Строка «{0}», base: фигура", rn), errors);
                    if (row.TryGetProperty("frames", out var frames) && frames.ValueKind == JsonValueKind.Array)
                    {
                        int fi = 0;
                        foreach (var f in frames.EnumerateArray())
                        {
                            fi++;
                            if (f.ValueKind == JsonValueKind.Object && f.TryGetProperty("items", out var it) && it.ValueKind == JsonValueKind.Array)
                                CheckItems(it, CoreText.F("Строка «{0}», кадр {1}: фигура", rn, fi), errors);
                        }
                    }
                }
            }
            if (errors.Count == 0)
            {
                try
                {
                    if (kind == ChatKind.Sprite) Scene.Render(root);
                    else SheetScene.Render(root);
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException or FormatException or JsonException)
                {
                    errors.Add(ex.Message);
                }
            }
        }
        catch (JsonException ex) { errors.Add(CoreText.F("Ошибка JSON: {0}", ex.Message)); }
        return new(errors, warnings);
    }

    private static void CheckItems(JsonElement items, string label, List<string> errors)
    {
        int n = 0;
        foreach (var item in items.EnumerateArray())
        {
            n++;
            if (item.ValueKind != JsonValueKind.Object) { errors.Add(CoreText.F("{0} {1}: нужен объект", label, n)); continue; }
            var keys = item.EnumerateObject().Select(p => p.Name).ToList();
            if (keys.Any(k => ShapeKeys.Contains(k, StringComparer.OrdinalIgnoreCase)))
            {
                if (item.TryGetProperty("group", out var g) && g.ValueKind == JsonValueKind.Array) CheckItems(g, label + " (group)", errors);
                continue;
            }
            string first = keys.FirstOrDefault(k => k != "color") ?? "";
            errors.Add(CoreText.F("{0} {1}: нет ни одного из {2}.{3}", label, n, string.Join(", ", ShapeKeys), first.Length > 0 ? Hint(first, ShapeKeys, brief: true) : ""));
        }
    }

    // ---------- internals ----------

    private static void WarnUnknownParameters(JsonElement step, string keyName, string[] parameters, string where, List<string> warnings, params string[] extra)
    {
        var known = parameters.Select(p => ParamName(p)).Concat(extra).Append(keyName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in step.EnumerateObject())
            if (!known.Contains(prop.Name))
                warnings.Add(CoreText.F("{0}: неизвестный параметр «{1}»{2}", where, prop.Name, Hint(prop.Name, known, brief: true)));
    }

    private static string ParamName(string p)
    {
        int cut = p.IndexOfAny([':', '=', ' ']);
        return cut > 0 ? p[..cut] : p;
    }

    private static string Hint(string name, IEnumerable<string> known, bool brief = false)
    {
        string? best = null;
        int bestDist = int.MaxValue;
        foreach (var k in known)
        {
            int d = Levenshtein(name.ToLowerInvariant(), k.ToLowerInvariant());
            if (d < bestDist) { bestDist = d; best = k; }
        }
        bool close = best != null && bestDist <= Math.Max(2, name.Length / 3);
        if (close) return " " + CoreText.F("Возможно, вы имели в виду «{0}».", best!);
        return brief ? "" : " " + CoreText.T("Используйте только операции и проверки из каталога.");
    }

    private static int Levenshtein(string a, string b)
    {
        var prev = Enumerable.Range(0, b.Length + 1).ToArray();
        for (int i = 1; i <= a.Length; i++)
        {
            var cur = new int[b.Length + 1];
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++)
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            prev = cur;
        }
        return prev[b.Length];
    }

    private static string Normalize(string text) =>
        text.Replace('“', '"').Replace('”', '"').Replace('„', '"').Replace('«', '"').Replace('»', '"')
            .Replace('‘', '\'').Replace('’', '\'').Replace(' ', ' ').Replace("\r\n", "\n");

    /// <summary>Sources by priority: ```json blocks, other ``` blocks, the whole text.</summary>
    private static IEnumerable<string> Candidates(string text)
    {
        var fenced = new List<(string Lang, string Body)>();
        int pos = 0;
        while (true)
        {
            int open = text.IndexOf("```", pos, StringComparison.Ordinal);
            if (open < 0) break;
            int lineEnd = text.IndexOf('\n', open);
            if (lineEnd < 0) break;
            int close = text.IndexOf("```", lineEnd, StringComparison.Ordinal);
            string lang = text[(open + 3)..lineEnd].Trim().ToLowerInvariant();
            if (close < 0) { fenced.Add((lang, text[(lineEnd + 1)..])); break; }   // the answer was cut off: the block is not closed
            fenced.Add((lang, text[(lineEnd + 1)..close]));
            pos = close + 3;
        }
        foreach (var f in fenced.Where(f => f.Lang == "json")) yield return f.Body;
        foreach (var f in fenced.Where(f => f.Lang != "json")) yield return f.Body;
        yield return text;
    }

    /// <summary>Top-level paired { } and [ ] in the text (quotes and escaping are taken into account), left to right.</summary>
    private static IEnumerable<string> BalancedBlocks(string source)
    {
        for (int i = 0; i < source.Length; i++)
        {
            if (source[i] is not ('{' or '[')) continue;
            int end = MatchEnd(source, i);
            if (end < 0) continue;
            yield return source[i..(end + 1)];
            i = end;
        }
    }

    private static int MatchEnd(string s, int start)
    {
        var stack = new Stack<char>();
        bool inString = false;
        for (int i = start; i < s.Length; i++)
        {
            char c = s[i];
            if (inString)
            {
                if (c == '\\') i++;
                else if (c == '"') inString = false;
                continue;
            }
            if (c == '"') inString = true;
            else if (c is '{' or '[') stack.Push(c == '{' ? '}' : ']');
            else if (c is '}' or ']')
            {
                if (stack.Count == 0 || stack.Pop() != c) return -1;
                if (stack.Count == 0) return i;
            }
        }
        return -1;
    }

    private static bool LooksLikeAnswer(JsonElement root) => KindOf(root) != ChatKind.Pipeline || LooksLikePipeline(root);

    private static bool LooksLikePipeline(JsonElement root) => root.ValueKind switch
    {
        JsonValueKind.Object => root.TryGetProperty("steps", out var s) && s.ValueKind == JsonValueKind.Array
                                || root.TryGetProperty("checks", out var c) && c.ValueKind == JsonValueKind.Array,
        JsonValueKind.Array => root.GetArrayLength() > 0 && root.EnumerateArray().All(e => e.ValueKind == JsonValueKind.Object && e.TryGetProperty("op", out _)),
        _ => false,
    };
}
