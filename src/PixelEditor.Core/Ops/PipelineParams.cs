// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace PixelEditor.Core.Ops;

/// <summary>The type of a template parameter.</summary>
public enum ParamType { Int, Number, Bool, String, Color, Palette, Choice }

/// <summary>One allowed value of a <see cref="ParamType.Choice"/> parameter and its caption for people.</summary>
public sealed record ParamChoice(string Value, string Label);

/// <summary>
/// A template parameter: {"name":"size","type":"int","default":64,"min":8,"max":512,"label":"Size","hint":"..."}.
/// Steps and checks refer to it with a string "$size" (a whole value; numbers may add "+2", "-1", "*2", "/2": "$size+2").
/// A step or check may carry "when": "$outline==black" (also !=, &gt;, &lt;, &gt;=, &lt;=, "a|b" alternatives, a bare "$flag" or "!$flag").
/// </summary>
public sealed class PipelineParam
{
    public required string Name { get; init; }
    public ParamType Type { get; init; }
    public object Default { get; init; } = 0;
    public double? Min { get; init; }
    public double? Max { get; init; }
    public IReadOnlyList<ParamChoice> Choices { get; init; } = [];
    public string Label { get; init; } = "";
    public string Hint { get; init; } = "";

    /// <summary>The caption for people: the label, or the name when no label is given.</summary>
    public string Caption => string.IsNullOrWhiteSpace(Label) ? Name : Label;

    /// <summary>The type as written in JSON (int, number, bool, string, color, palette, choice).</summary>
    public string TypeName => Type.ToString().ToLowerInvariant();

    public bool IsNumeric => Type is ParamType.Int or ParamType.Number;

    /// <summary>The value as text (the invariant culture; booleans as true/false).</summary>
    public static string Text(object value) => value switch
    {
        bool b => b ? "true" : "false",
        double d => d.ToString("0.###", CultureInfo.InvariantCulture),
        int i => i.ToString(CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
    };

    /// <summary>
    /// Checks a raw value (a JsonElement, a string from the command line, a number or a bool) against the type, range and choices and returns the typed value
    /// (int, double, bool or string). Throws ArgumentException with a message that names the parameter.
    /// </summary>
    public object Coerce(object? raw)
    {
        if (raw is JsonElement el) raw = el.ValueKind switch
        {
            JsonValueKind.Number => el.GetDouble(),
            JsonValueKind.String => el.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
        if (raw == null) throw Bad(CoreText.T("значение не задано"));
        if (raw is int or long or float or decimal) raw = Convert.ToDouble(raw, CultureInfo.InvariantCulture);

        switch (Type)
        {
            case ParamType.Int:
            case ParamType.Number:
            {
                double d;
                if (raw is double rd) d = rd;
                else if (raw is string s && double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) d = parsed;
                else throw Bad(CoreText.T("нужно число"));
                if (double.IsNaN(d) || double.IsInfinity(d)) throw Bad(CoreText.T("нужно число"));
                if (Type == ParamType.Int && Math.Abs(d - Math.Round(d)) > 1e-9) throw Bad(CoreText.T("нужно целое число"));
                if (Min is { } lo && d < lo - 1e-9 || Max is { } hi && d > hi + 1e-9)
                    throw Bad(CoreText.F("допустимо {0}..{1}", Min is { } a ? Text(a) : "…", Max is { } b ? Text(b) : "…"));
                return Type == ParamType.Int ? (object)(int)Math.Round(d) : d;
            }
            case ParamType.Bool:
                if (raw is bool bv) return bv;
                if (raw is string bs)
                    switch (bs.Trim().ToLowerInvariant())
                    {
                        case "true" or "yes" or "on" or "1" or "да": return true;
                        case "false" or "no" or "off" or "0" or "нет": return false;
                    }
                if (raw is double bd) return bd != 0;
                throw Bad(CoreText.T("нужно true или false"));
            case ParamType.Color:
            {
                var text = (raw as string)?.Trim();
                if (text == null || !Pixel.TryParseHex(text, out _)) throw Bad(CoreText.T("нужен цвет #RRGGBB"));
                return text;
            }
            case ParamType.Palette:
            {
                var text = (raw as string)?.Trim();
                if (string.IsNullOrEmpty(text)) throw Bad(CoreText.T("нужно название палитры"));
                var found = PaletteLibrary.Default.Find(text) ?? throw Bad(CoreText.F("палитра «{0}» не найдена в библиотеке", text));
                return found.Name;
            }
            case ParamType.Choice:
            {
                var text = Text(raw).Trim();
                var match = Choices.FirstOrDefault(c => c.Value.Equals(text, StringComparison.OrdinalIgnoreCase))
                            ?? Choices.FirstOrDefault(c => c.Label.Equals(text, StringComparison.CurrentCultureIgnoreCase));
                if (match == null) throw Bad(CoreText.F("допустимо: {0}", string.Join(", ", Choices.Select(c => c.Value))));
                return match.Value;
            }
            default:
                return raw is string str ? str : Text(raw);
        }
    }

    private ArgumentException Bad(string why) => new(CoreText.F("Параметр шаблона «{0}»: {1}", Name, why));

    internal static ParamType? ParseType(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        "int" or "integer" => ParamType.Int,
        "number" or "float" or "double" => ParamType.Number,
        "bool" or "boolean" => ParamType.Bool,
        "string" or "text" => ParamType.String,
        "color" or "colour" => ParamType.Color,
        "palette" or "palette-name" or "palettename" => ParamType.Palette,
        "choice" or "enum" => ParamType.Choice,
        _ => null,
    };
}

/// <summary>Reading, checking and applying template parameters (see <see cref="PipelineParam"/>).</summary>
public static class PipelineParams
{
    private static readonly Regex NameRule = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);
    private static readonly Regex Reference = new(@"^\$([A-Za-z_][A-Za-z0-9_]*)(?:\s*([+\-*/])\s*(-?\d+(?:\.\d+)?))?$", RegexOptions.Compiled);

    /// <summary>
    /// Reads the "params" array. Problems with the definitions are collected in <paramref name="errors"/> (they are reported by Pipeline.Validate);
    /// a broken parameter is skipped so that the rest of the template still loads.
    /// </summary>
    public static List<PipelineParam> Parse(JsonElement list, List<string> errors)
    {
        var result = new List<PipelineParam>();
        if (list.ValueKind != JsonValueKind.Array)
        {
            errors.Add(CoreText.T("«params» должен быть массивом"));
            return result;
        }
        int n = 0;
        foreach (var item in list.EnumerateArray())
        {
            n++;
            if (item.ValueKind != JsonValueKind.Object) { errors.Add(CoreText.F("параметр {0}: нужен объект", n)); continue; }
            string? name = Str(item, "name");
            if (name == null || !NameRule.IsMatch(name)) { errors.Add(CoreText.F("параметр {0}: нужно имя из букв, цифр и _ (не с цифры)", n)); continue; }
            if (result.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) { errors.Add(CoreText.F("параметр «{0}» объявлен дважды", name)); continue; }
            var type = PipelineParam.ParseType(Str(item, "type"));
            if (type == null) { errors.Add(CoreText.F("параметр «{0}»: тип должен быть int, number, bool, string, color, palette или choice", name)); continue; }

            var choices = new List<ParamChoice>();
            if (item.TryGetProperty("choices", out var ch) && ch.ValueKind == JsonValueKind.Array)
                foreach (var c in ch.EnumerateArray())
                {
                    if (c.ValueKind == JsonValueKind.String) choices.Add(new(c.GetString()!, c.GetString()!));
                    else if (c.ValueKind == JsonValueKind.Object && Str(c, "value") is { } v) choices.Add(new(v, Str(c, "label") ?? v));
                }
            double? min = Num(item, "min"), max = Num(item, "max");
            string label = Str(item, "label") ?? "", hint = Str(item, "hint") ?? "";

            var draft = new PipelineParam { Name = name, Type = type.Value, Min = min, Max = max, Choices = choices, Label = label, Hint = hint };
            if (type == ParamType.Choice && choices.Count == 0) { errors.Add(CoreText.F("параметр «{0}»: у choice нужен список choices", name)); continue; }
            if (min > max) { errors.Add(CoreText.F("параметр «{0}»: min больше max", name)); continue; }
            if (!item.TryGetProperty("default", out var def) || def.ValueKind == JsonValueKind.Null)
            {
                errors.Add(CoreText.F("параметр «{0}»: нужно значение default", name));
                continue;
            }
            try
            {
                result.Add(new PipelineParam
                {
                    Name = name, Type = type.Value, Min = min, Max = max, Choices = choices, Label = label, Hint = hint,
                    Default = draft.Coerce(def),
                });
            }
            catch (ArgumentException ex) { errors.Add(CoreText.F("default: {0}", ex.Message)); }
        }
        return result;
    }

    private static string? Str(JsonElement o, string key) =>
        o.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double? Num(JsonElement o, string key) =>
        o.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    /// <summary>
    /// Defaults merged with presets and overrides: later sources win. Keys are case-insensitive; an unknown key or a value of the wrong type throws ArgumentException.
    /// </summary>
    public static Dictionary<string, object> Resolve(IReadOnlyList<PipelineParam> declared, params IEnumerable<KeyValuePair<string, object?>>?[] sources)
    {
        var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in declared) values[p.Name] = p.Default;
        foreach (var source in sources)
        {
            if (source == null) continue;
            foreach (var (key, raw) in source)
            {
                var p = declared.FirstOrDefault(d => d.Name.Equals(key, StringComparison.OrdinalIgnoreCase));
                if (p == null)
                    throw new ArgumentException(declared.Count == 0
                        ? CoreText.F("У шаблона нет параметра «{0}» (параметров нет вообще)", key)
                        : CoreText.F("У шаблона нет параметра «{0}». Есть: {1}", key, string.Join(", ", declared.Select(d => d.Name))));
                values[p.Name] = p.Coerce(raw);
            }
        }
        return values;
    }

    /// <summary>Command-line style overrides "name=value" (a missing "=" is an error).</summary>
    public static Dictionary<string, object?> ParseAssignments(IEnumerable<string> assignments)
    {
        var map = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in assignments)
        {
            int eq = a.IndexOf('=');
            if (eq <= 0) throw new ArgumentException(CoreText.F("Параметр шаблона нужно задать как имя=значение, получено «{0}»", a));
            map[a[..eq].Trim()] = a[(eq + 1)..].Trim();
        }
        return map;
    }

    /// <summary>Overrides from a JSON object ({"size":48,"colors":16}); null or an empty object: none.</summary>
    public static Dictionary<string, object?>? FromJson(JsonElement? obj)
    {
        if (obj is not { ValueKind: JsonValueKind.Object } o) return null;
        var map = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in o.EnumerateObject()) map[p.Name] = p.Value.Clone();
        return map.Count == 0 ? null : map;
    }

    /// <summary>Copies a step or check with every "$name" replaced by its value and without the "when" key. Without parameters the element is returned as it is.</summary>
    public static JsonElement Substitute(JsonElement element, IReadOnlyList<PipelineParam> declared, IReadOnlyDictionary<string, object> values)
    {
        if (declared.Count == 0) return element;
        var node = JsonNode.Parse(element.GetRawText())!;
        if (node is JsonObject o) o.Remove("when");
        node = Walk(node, declared, values);
        return JsonSerializer.SerializeToElement(node);
    }

    private static JsonNode? Walk(JsonNode? node, IReadOnlyList<PipelineParam> declared, IReadOnlyDictionary<string, object> values)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var key in o.Select(p => p.Key).ToList()) o[key] = Walk(o[key], declared, values);
                return o;
            case JsonArray a:
                for (int i = 0; i < a.Count; i++) a[i] = Walk(a[i], declared, values);
                return a;
            case JsonValue v when v.TryGetValue<string>(out var s) && Reference.Match(s) is { Success: true } m:
            {
                var p = declared.FirstOrDefault(d => d.Name.Equals(m.Groups[1].Value, StringComparison.OrdinalIgnoreCase));
                if (p == null) return v;
                var value = values[p.Name];
                if (m.Groups[2].Success)
                {
                    double x = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                    double y = double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
                    double r = m.Groups[2].Value switch { "+" => x + y, "-" => x - y, "*" => x * y, _ => y == 0 ? x : x / y };
                    return p.Type == ParamType.Int && Math.Abs(r - Math.Round(r)) < 1e-9 ? JsonValue.Create((int)Math.Round(r)) : JsonValue.Create(r);
                }
                return value switch
                {
                    int i => JsonValue.Create(i),
                    double d => JsonValue.Create(d),
                    bool b => JsonValue.Create(b),
                    _ => JsonValue.Create(Convert.ToString(value, CultureInfo.InvariantCulture)),
                };
            }
            default:
                return node;
        }
    }

    /// <summary>Evaluates "when" of a step or check. No "when": true.</summary>
    public static bool IsActive(JsonElement element, IReadOnlyList<PipelineParam> declared, IReadOnlyDictionary<string, object> values)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty("when", out var w)) return true;
        if (w.ValueKind == JsonValueKind.True) return true;
        if (w.ValueKind == JsonValueKind.False) return false;
        if (w.ValueKind != JsonValueKind.String) throw new ArgumentException(CoreText.T("«when» должен быть строкой вида \"$имя==значение\""));
        return Evaluate(w.GetString()!, declared, values, out var error) ? true : error == null ? false : throw new ArgumentException(error);
    }

    /// <summary>Syntax and references of a "when" expression; null when it is fine.</summary>
    public static string? CheckWhen(string expression, IReadOnlyList<PipelineParam> declared)
    {
        var values = declared.ToDictionary(p => p.Name, p => p.Default, StringComparer.OrdinalIgnoreCase);
        Evaluate(expression, declared, values, out var error);
        return error;
    }

    private static bool Evaluate(string expression, IReadOnlyList<PipelineParam> declared, IReadOnlyDictionary<string, object> values, out string? error)
    {
        error = null;
        var e = expression.Trim();
        bool negate = false;
        if (e.StartsWith('!')) { negate = true; e = e[1..].TrimStart(); }
        if (!e.StartsWith('$')) { error = CoreText.F("«when»: ожидается $имя, получено «{0}»", expression); return false; }
        int end = 1;
        while (end < e.Length && (char.IsLetterOrDigit(e[end]) || e[end] == '_')) end++;
        var name = e[1..end];
        var p = declared.FirstOrDefault(d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (p == null) { error = CoreText.F("«when»: параметр «{0}» не объявлен", name); return false; }
        var value = values[p.Name];
        var rest = e[end..].Trim();
        bool result;
        if (rest.Length == 0)
            result = value switch
            {
                bool b => b,
                int i => i != 0,
                double d => d != 0,
                string s => s.Length > 0 && !s.Equals("none", StringComparison.OrdinalIgnoreCase) && !s.Equals("false", StringComparison.OrdinalIgnoreCase) && !s.Equals("off", StringComparison.OrdinalIgnoreCase),
                _ => true,
            };
        else
        {
            string op = rest.StartsWith("==") || rest.StartsWith("!=") || rest.StartsWith(">=") || rest.StartsWith("<=") ? rest[..2]
                : rest.StartsWith('>') || rest.StartsWith('<') ? rest[..1] : "";
            if (op == "") { error = CoreText.F("«when»: после имени ожидается ==, !=, >, <, >= или <=, получено «{0}»", rest); return false; }
            var operand = rest[op.Length..].Trim();
            if (op is "==" or "!=")
            {
                var current = PipelineParam.Text(value);
                bool any = operand.Split('|').Any(a => a.Trim().Equals(current, StringComparison.OrdinalIgnoreCase));
                result = op == "==" ? any : !any;
            }
            else
            {
                if (!p.IsNumeric || !double.TryParse(operand, NumberStyles.Float, CultureInfo.InvariantCulture, out var rhs))
                { error = CoreText.F("«when»: сравнение {0} работает только с числовым параметром и числом", op); return false; }
                double lhs = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                result = op switch { ">" => lhs > rhs, "<" => lhs < rhs, ">=" => lhs >= rhs, _ => lhs <= rhs };
            }
        }
        return negate ? !result : result;
    }

    /// <summary>
    /// Every "$name" inside a step or check must be declared and its type must fit the operation or check parameter it is written to.
    /// <paramref name="knownParameters"/> are the registry strings "name:type=default (...)" of the operation or check.
    /// </summary>
    public static void ValidateReferences(JsonElement element, IReadOnlyList<PipelineParam> declared, string[] knownParameters, string where, List<string> problems)
    {
        if (element.ValueKind != JsonValueKind.Object) return;
        foreach (var prop in element.EnumerateObject())
        {
            if (prop.NameEquals("when"))
            {
                if (prop.Value.ValueKind == JsonValueKind.String && CheckWhen(prop.Value.GetString()!, declared) is { } err) problems.Add($"{where}: {err}");
                continue;
            }
            string? expected = ExpectedType(knownParameters, prop.Name);
            Scan(prop.Value, prop.Name, expected, topLevel: true, declared, where, problems);
        }
    }

    private static void Scan(JsonElement v, string key, string? expectedType, bool topLevel, IReadOnlyList<PipelineParam> declared, string where, List<string> problems)
    {
        switch (v.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in v.EnumerateObject()) Scan(p.Value, p.Name, null, false, declared, where, problems);
                break;
            case JsonValueKind.Array:
                foreach (var item in v.EnumerateArray()) Scan(item, key, null, false, declared, where, problems);
                break;
            case JsonValueKind.String:
            {
                var s = v.GetString()!;
                if (s.Length < 2 || s[0] != '$' || !(char.IsLetter(s[1]) || s[1] == '_')) break;
                var m = Reference.Match(s);
                if (!m.Success) { problems.Add(CoreText.F("{0}: неверная ссылка на параметр «{1}»", where, s)); break; }
                var p = declared.FirstOrDefault(d => d.Name.Equals(m.Groups[1].Value, StringComparison.OrdinalIgnoreCase));
                if (p == null) { problems.Add(CoreText.F("{0}: параметр «{1}» не объявлен в params", where, m.Groups[1].Value)); break; }
                if (m.Groups[2].Success && !p.IsNumeric)
                { problems.Add(CoreText.F("{0}: «{1}» нельзя использовать в арифметике: тип {2}", where, s, p.TypeName)); break; }
                if (topLevel && expected(expectedType, p) is { } mismatch)
                    problems.Add(CoreText.F("{0}: параметр «{1}» ожидает {2}, а ${3} имеет тип {4}", where, key, mismatch, p.Name, p.TypeName));
                break;
            }
        }

        static string? expected(string? type, PipelineParam p) => type switch
        {
            "int" or "number" => p.IsNumeric ? null : "number",
            "bool" => p.Type == ParamType.Bool ? null : "bool",
            "color" or "hex" => p.Type == ParamType.Color ? null : "color",
            "string" => p.Type is ParamType.String or ParamType.Choice or ParamType.Palette or ParamType.Color ? null : "string",
            _ => null,
        };
    }

    private static string? ExpectedType(string[] parameters, string name)
    {
        foreach (var raw in parameters)
        {
            int colon = raw.IndexOf(':');
            if (colon <= 0 || !raw[..colon].Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            int end = colon + 1;
            while (end < raw.Length && char.IsLetter(raw[end])) end++;
            var type = raw[(colon + 1)..end];
            // arrays and nested shapes (string[], [[x,y],...]) are not checked
            return end < raw.Length && raw[end] == '[' ? null : type;
        }
        return null;
    }
}
