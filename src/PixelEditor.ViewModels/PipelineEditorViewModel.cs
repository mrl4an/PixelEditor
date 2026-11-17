// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PixelEditor.Core;
using PixelEditor.Core.Ops;

namespace PixelEditor.ViewModels;

/// <summary>A step or check parameter: the name, type and default value come from the operation description ("keepAspect:bool=true (explanation)").</summary>
public sealed class ParamRow : ObservableObject
{
    private static readonly Regex Format = new(@"^\s*(?<name>[\w]+)\s*:\s*(?<type>[^\s=(]+)\s*(=\s*(?<def>[^\s(]+))?\s*(\((?<hint>.*)\))?\s*$", RegexOptions.Compiled | RegexOptions.Singleline);
    private string _value = "";

    public ParamRow(string name, string type, string defaultValue, string hint)
    {
        Name = name;
        Type = type;
        Default = defaultValue;
        Hint = hint;
    }

    public string Name { get; }
    public string Type { get; }
    public string Default { get; }
    public string Hint { get; }

    /// <summary>What to show as a tooltip: the type, the default value and the explanation.</summary>
    public string Tooltip
    {
        get
        {
            var sb = new StringBuilder(Type);
            if (Default.Length > 0) sb.Append(Loc.T("pipeline_editor.default", Default));
            if (Hint.Length > 0) sb.Append($". {Hint}");
            return sb.ToString();
        }
    }

    /// <summary>Empty: the parameter is not set, the default value is used.</summary>
    public string Value
    {
        get => _value;
        set
        {
            if (SetProperty(ref _value, value ?? "")) Changed?.Invoke();
        }
    }

    public event Action? Changed;

    public static ParamRow Parse(string spec)
    {
        var m = Format.Match(spec);
        if (!m.Success) return new ParamRow(spec.Split(':', ' ')[0], "string", "", spec);
        return new ParamRow(m.Groups["name"].Value, m.Groups["type"].Value, m.Groups["def"].Value, m.Groups["hint"].Value.Trim());
    }

    /// <summary>The value for JSON: a number, boolean, array or string depending on the type. null if the parameter is empty.</summary>
    public JsonNode? ToNode()
    {
        var text = _value.Trim();
        if (text.Length == 0) return null;
        if (text.StartsWith('[') || text.StartsWith('{'))
        {
            try { return JsonNode.Parse(text); } catch (JsonException) { return JsonValue.Create(text); }
        }
        if (Type.StartsWith("bool", StringComparison.OrdinalIgnoreCase) && bool.TryParse(text, out var b)) return b;
        if (Type.StartsWith("int", StringComparison.OrdinalIgnoreCase) && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) return i;
        if (Type.StartsWith("number", StringComparison.OrdinalIgnoreCase) &&
            double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
        // unknown type: try a number and a boolean, otherwise a string
        if (Type is "any" or "json")
        {
            if (bool.TryParse(text, out var b2)) return b2;
            if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i2)) return i2;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d2)) return d2;
        }
        return text;
    }

    public static string FromNode(JsonNode node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) ? s
        : node is JsonValue v2 && v2.TryGetValue<bool>(out var b) ? (b ? "true" : "false")
        : node.ToJsonString();
}

/// <summary>A step or check in the template builder.</summary>
public sealed class StepRow : ObservableObject
{
    private bool _required = true;

    public StepRow(bool isCheck, string name, string description, IEnumerable<ParamRow> parameters)
    {
        IsCheck = isCheck;
        Name = name;
        Description = description;
        foreach (var p in parameters) Parameters.Add(p);
    }

    public bool IsCheck { get; }
    public string Name { get; }
    public string Description { get; }
    public ObservableCollection<ParamRow> Parameters { get; } = new();

    /// <summary>An optional check only warns and does not count as a failure.</summary>
    public bool Required
    {
        get => _required;
        set { if (SetProperty(ref _required, value)) Changed?.Invoke(); }
    }

    public bool HasParameters => Parameters.Count > 0;

    /// <summary>The number in the list for the card title (set by the editor).</summary>
    private int _number;
    public int Number { get => _number; set => SetProperty(ref _number, value); }

    public event Action? Changed;

    public JsonObject ToJson()
    {
        var o = new JsonObject { [IsCheck ? "check" : "op"] = Name };
        foreach (var p in Parameters)
            if (p.ToNode() is { } node) o[p.Name] = node;
        if (IsCheck && !_required) o["required"] = false;
        return o;
    }
}

/// <summary>
/// The processing template builder: name, description, steps and checks from the operation catalog, parameters as fields, the ready JSON,
/// a trial run on the current layer and saving to the library. System templates open as a copy.
/// </summary>
public sealed class PipelineEditorViewModel : DialogViewModel
{
    private readonly PipelineLibrary _library;
    private readonly IDialogService _dialogs;
    private readonly Func<PixelImage?> _sample;
    private readonly string? _existingId;
    private string _name, _description;
    private string _error = "", _testResult = "", _json = "";
    private OperationInfo? _stepToAdd;
    private CheckInfo? _checkToAdd;
    private bool _loading;

    public PipelineEditorViewModel(PipelineLibrary library, IDialogService dialogs, Func<PixelImage?> sampleImage,
        TemplateInfo? source = null, bool asCopy = false)
    {
        _library = library;
        _dialogs = dialogs;
        _sample = sampleImage;
        _existingId = source != null && !source.IsSystem && !asCopy ? source.Id : null;
        _name = source == null ? "" : asCopy ? Unique(source.Name + Loc.T("palette_lib.copy")) : source.Name;
        _description = source?.Description ?? "";
        Title = _existingId != null ? Loc.T("pipeline_editor.template", source!.Name) : source == null ? Loc.T("pipeline_editor.new_template") : Loc.T("pipeline_editor.new_template_copy");

        OperationCatalog = Operations.All.OrderBy(o => o.Source != "builtin").ThenBy(o => o.Name).ToList();
        CheckCatalog = Checks.All.OrderBy(c => c.Source != "builtin").ThenBy(c => c.Name).ToList();
        _stepToAdd = OperationCatalog.FirstOrDefault();
        _checkToAdd = CheckCatalog.FirstOrDefault();

        AddStepCommand = new RelayCommand(AddStep, () => _stepToAdd != null);
        AddCheckCommand = new RelayCommand(AddCheck, () => _checkToAdd != null);
        RemoveCommand = new RelayCommand(p => Remove((StepRow)p!));
        MoveUpCommand = new RelayCommand(p => Move((StepRow)p!, -1));
        MoveDownCommand = new RelayCommand(p => Move((StepRow)p!, +1));
        TestCommand = new RelayCommand(Test);
        SaveCommand = new RelayCommand(Save);

        if (source != null) LoadFrom(source.Json);
        Rebuild();
    }

    public override string Title { get; }

    public IReadOnlyList<OperationInfo> OperationCatalog { get; }
    public IReadOnlyList<CheckInfo> CheckCatalog { get; }
    public ObservableCollection<StepRow> Steps { get; } = new();
    public ObservableCollection<StepRow> CheckRows { get; } = new();

    public string Name { get => _name; set { if (SetProperty(ref _name, value)) Rebuild(); } }
    public string Description { get => _description; set { if (SetProperty(ref _description, value)) Rebuild(); } }

    public OperationInfo? StepToAdd { get => _stepToAdd; set { if (SetProperty(ref _stepToAdd, value)) { OnPropertyChanged(nameof(StepToAddHint)); AddStepCommand.RaiseCanExecuteChanged(); } } }
    public CheckInfo? CheckToAdd { get => _checkToAdd; set { if (SetProperty(ref _checkToAdd, value)) { OnPropertyChanged(nameof(CheckToAddHint)); AddCheckCommand.RaiseCanExecuteChanged(); } } }
    public string StepToAddHint => _stepToAdd?.Description ?? "";
    public string CheckToAddHint => _checkToAdd?.Description ?? "";

    /// <summary>The ready template JSON: pixed and MCP understand it too.</summary>
    public string Json { get => _json; private set => SetProperty(ref _json, value); }

    public string Error { get => _error; private set => SetProperty(ref _error, value); }
    public string TestResult { get => _testResult; private set => SetProperty(ref _testResult, value); }
    public bool HasSteps => Steps.Count > 0;
    public string SavedId { get; private set; } = "";

    public RelayCommand AddStepCommand { get; }
    public RelayCommand AddCheckCommand { get; }
    public RelayCommand RemoveCommand { get; }
    public RelayCommand MoveUpCommand { get; }
    public RelayCommand MoveDownCommand { get; }
    public RelayCommand TestCommand { get; }
    public RelayCommand SaveCommand { get; }

    private string Unique(string name)
    {
        var result = name;
        for (int i = 2; _library.All().Any(t => t.Name.Equals(result, StringComparison.CurrentCultureIgnoreCase)); i++) result = $"{name} {i}";
        return result;
    }

    // ---- assembly from the catalog ----

    private static StepRow MakeStep(OperationInfo op) =>
        new(false, op.Name, op.Description, op.Parameters.Select(ParamRow.Parse));

    private static StepRow MakeCheck(CheckInfo c) =>
        new(true, c.Name, c.Description, c.Parameters.Select(ParamRow.Parse));

    private void Attach(StepRow row)
    {
        row.Changed += Rebuild;
        foreach (var p in row.Parameters) p.Changed += Rebuild;
    }

    private void AddStep()
    {
        if (_stepToAdd == null) return;
        var row = MakeStep(_stepToAdd);
        Attach(row);
        Steps.Add(row);
        Rebuild();
    }

    private void AddCheck()
    {
        if (_checkToAdd == null) return;
        var row = MakeCheck(_checkToAdd);
        Attach(row);
        CheckRows.Add(row);
        Rebuild();
    }

    private void Remove(StepRow row)
    {
        if (!Steps.Remove(row)) CheckRows.Remove(row);
        Rebuild();
    }

    private void Move(StepRow row, int delta)
    {
        var list = row.IsCheck ? CheckRows : Steps;
        int i = list.IndexOf(row), j = i + delta;
        if (i < 0 || j < 0 || j >= list.Count) return;
        list.Move(i, j);
        Rebuild();
    }

    /// <summary>Fills the builder from ready JSON (steps and checks, extra parameters are kept as they are).</summary>
    private void LoadFrom(string json)
    {
        _loading = true;
        var node = JsonNode.Parse(json);
        _paramsNode = (node as JsonObject)?["params"]?.DeepClone();   // template parameters are kept as they are (no visual editor for them yet)
        var steps = node is JsonArray a ? a : (node as JsonObject)?["steps"] as JsonArray;
        var checks = (node as JsonObject)?["checks"] as JsonArray;
        foreach (var s in steps?.OfType<JsonObject>() ?? []) Steps.Add(FromJson(s, isCheck: false));
        foreach (var c in checks?.OfType<JsonObject>() ?? []) CheckRows.Add(FromJson(c, isCheck: true));
        _loading = false;
    }

    private StepRow FromJson(JsonObject o, bool isCheck)
    {
        var key = isCheck ? "check" : "op";
        var name = o[key]?.GetValue<string>() ?? "?";
        string description;
        IEnumerable<string> specs;
        if (isCheck && Checks.All.FirstOrDefault(c => c.Name == name) is { } ci) (description, specs) = (ci.Description, ci.Parameters);
        else if (!isCheck && Operations.All.FirstOrDefault(op => op.Name == name) is { } oi) (description, specs) = (oi.Description, oi.Parameters);
        else (description, specs) = (Loc.T("pipeline_editor.unknown_operation_the_template_will"), []);

        var row = new StepRow(isCheck, name, description, specs.Select(ParamRow.Parse));
        foreach (var kv in o)
        {
            if (kv.Key == key || kv.Value == null) continue;
            if (kv.Key == "required" && isCheck) { row.Required = kv.Value.GetValue<bool>(); continue; }
            var p = row.Parameters.FirstOrDefault(x => x.Name == kv.Key);
            if (p == null) { p = new ParamRow(kv.Key, "any", "", Loc.T("pipeline_editor.parameter_from_the_template_file")); row.Parameters.Add(p); }
            p.Value = ParamRow.FromNode(kv.Value);
        }
        Attach(row);
        return row;
    }

    private void Rebuild()
    {
        if (_loading) return;
        for (int i = 0; i < Steps.Count; i++) Steps[i].Number = i + 1;
        for (int i = 0; i < CheckRows.Count; i++) CheckRows[i].Number = i + 1;
        OnPropertyChanged(nameof(HasSteps));
        Json = BuildJson().ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
        Error = "";
    }

    private JsonNode? _paramsNode;

    private JsonObject BuildJson()
    {
        var o = new JsonObject
        {
            ["name"] = _name.Trim(),
            ["description"] = _description.Trim(),
        };
        if (_paramsNode != null) o["params"] = _paramsNode.DeepClone();
        o["steps"] = new JsonArray(Steps.Select(s => (JsonNode?)s.ToJson()).ToArray());
        if (CheckRows.Count > 0) o["checks"] = new JsonArray(CheckRows.Select(c => (JsonNode?)c.ToJson()).ToArray());
        return o;
    }

    // ---- validation and saving ----

    /// <summary>A trial run on the current layer: the step log and the check results. The document does not change.</summary>
    private void Test()
    {
        TestResult = "";
        try
        {
            var image = _sample();
            if (image == null) { TestResult = Loc.T("pipeline_editor.no_picture_to_test_on"); return; }
            var pipeline = Pipeline.Parse(Json);
            var problems = pipeline.Validate();
            if (problems.Count > 0) { TestResult = string.Join(Environment.NewLine, problems); return; }

            var result = pipeline.Run(image);
            var sb = new StringBuilder();
            sb.AppendLine(Loc.T("pipeline_editor.source", image.Width, image.Height));
            foreach (var l in result.Log)
                sb.AppendLine(l.Error != null
                    ? $"✗ {l.Index}. {l.Op}: {l.Error}"
                    : Loc.T("pipeline_editor.colors", l.Index, l.Op, l.Width, l.Height, l.Colors));
            foreach (var c in result.Checks)
                sb.AppendLine($"{(c.Passed ? "✓" : c.Required ? "✗" : "!")} {c.Check}: {c.Message}");
            sb.Append(result.Success ? Loc.T("pipeline_editor.the_result_is_fine") : result.Ok ? Loc.T("pipeline_editor.the_steps_ran_but_the") : Loc.T("pipeline_editor.the_template_stopped_on_an"));
            TestResult = sb.ToString();
        }
        catch (Exception ex) { TestResult = ex.Message; }
    }

    private void Save()
    {
        Error = "";
        if (string.IsNullOrWhiteSpace(_name)) { Error = Loc.T("pipeline_editor.enter_a_template_name"); return; }
        if (Steps.Count == 0) { Error = Loc.T("pipeline_editor.add_at_least_one_step"); return; }
        try
        {
            var info = _library.Save(_name, _description, Json, _existingId);
            SavedId = info.Id;
            Close(true);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or JsonException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Error = ex.Message;
        }
    }
}
