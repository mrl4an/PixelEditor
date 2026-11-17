// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PixelEditor.Core;
using PixelEditor.Core.Ops;

namespace PixelEditor.ViewModels;

/// <summary>One entry of the operation list: a short localized title with the technical name after it, for example "Убрать фон (removeBackground)".</summary>
public sealed record OperationChoice(OperationInfo Info)
{
    private const int MaxTitle = 48;

    public string Name => Info.Name;
    public string Title => TitleOf(Info.Description, Info.Name);
    public string Display => $"{Title} ({Name})";

    /// <summary>
    /// A short title from a description: the text up to the first period (followed by a space or the end) or colon, trimmed to a reasonable length.
    /// An empty description gives the technical name, so operations from plugins work too.
    /// </summary>
    public static string TitleOf(string? description, string name)
    {
        var text = (description ?? "").Trim();
        if (text.Length == 0) return name;
        int cut = text.Length;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == ':' || (c == '.' && (i + 1 == text.Length || char.IsWhiteSpace(text[i + 1])))) { cut = i; break; }
        }
        var title = text[..cut].Trim();
        // a bracketed aside is dropped from the title ("Убрать фон (сделать прозрачным)" -> "Убрать фон"): the technical name goes in brackets instead
        var plain = System.Text.RegularExpressions.Regex.Replace(title, @"\s*\([^)]*\)", "").Trim();
        if (plain.Length > 0) title = plain;
        if (title.Length > MaxTitle)
        {
            int space = title.LastIndexOf(' ', MaxTitle);
            title = title[..(space > MaxTitle / 2 ? space : MaxTitle)].TrimEnd(' ', ',', ';', '(') + "…";
        }
        return title.Length == 0 ? name : title;
    }
}

/// <summary>
/// "Tune an operation": any operation with parameter fields and a live Before/After preview on the current layer.
/// The result of automatic tools depends on the picture, so it can be tuned by eye instead of guessing.
/// </summary>
public sealed partial class OperationTunerViewModel : DialogViewModel
{
    [GeneratedRegex(@"^(-?\d+(\.\d+)?|true|false|#[0-9A-Fa-f]{6,8})$")]
    private static partial Regex PlainDefault();

    private readonly Func<PixelImage> _source;
    private readonly IUiTimer? _debounce;
    private OperationInfo? _operation;
    private PixelImage? _before, _after;
    private string _summary = "", _error = "";
    private bool _stale;

    /// <param name="initialArgs">The parameter values the tuning starts with (for example follow for a gradient background).</param>
    public OperationTunerViewModel(Func<PixelImage> source, string? initialOperation, string? initialArgs, IUiTimerFactory? timers = null)
    {
        _source = source;
        Catalog = Core.Ops.Operations.All.OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase).ToList();
        if (timers != null)
        {
            _debounce = timers.Create(TimeSpan.FromMilliseconds(350));
            _debounce.Tick += () => { _debounce.Stop(); RunPreview(); };
        }
        ApplyCommand = new RelayCommand(Apply, () => CanApply);
        ResetCommand = new RelayCommand(() => Load(_operation, null), () => _operation != null);
        Choices = Catalog.Select(o => new OperationChoice(o)).ToList();
        // no operation given: the first one of the list, so the dialog is never empty
        Load(Catalog.FirstOrDefault(o => o.Name == initialOperation) ?? Catalog.First(), initialArgs);
    }

    public override string Title => Loc.T("tuner.title");

    public IReadOnlyList<OperationInfo> Catalog { get; }

    /// <summary>The list as shown: a localized title with the technical name in brackets.</summary>
    public IReadOnlyList<OperationChoice> Choices { get; }

    public OperationChoice? SelectedChoice
    {
        get => Choices.FirstOrDefault(c => c.Info == _operation);
        set { if (value != null) SelectedOperation = value.Info; }
    }

    public OperationInfo? SelectedOperation
    {
        get => _operation;
        set { if (value != null && value != _operation) Load(value, null); }
    }

    public ObservableCollection<ParamRow> Parameters { get; } = new();
    public string Description => _operation?.Description ?? "";
    public PixelImage? Before => _before;
    public PixelImage? After => _after;
    public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }
    public string Error { get => _error; private set { if (SetProperty(ref _error, value)) OnPropertyChanged(nameof(HasError)); } }
    public bool HasError => _error.Length > 0;

    /// <summary>There is something to apply: the operation ran and the result differs from the layer.</summary>
    public bool CanApply => _after != null && !HasError && (_stale || Changed);
    public bool Changed { get; private set; }

    /// <summary>The result to apply to the layer.</summary>
    public PixelImage? Result { get; private set; }

    /// <summary>The parameters as JSON: the operation is written into a template the same way.</summary>
    public string ArgsJson => BuildArgs().ToJsonString();

    public string OperationName => _operation?.Name ?? "";

    public RelayCommand ApplyCommand { get; }
    public RelayCommand ResetCommand { get; }

    private void Load(OperationInfo? op, string? argsJson)
    {
        if (op == null) return;
        _operation = op;
        foreach (var row in Parameters) row.Changed -= OnParamChanged;
        Parameters.Clear();
        JsonObject? initial = null;
        if (!string.IsNullOrWhiteSpace(argsJson)) { try { initial = JsonNode.Parse(argsJson) as JsonObject; } catch (JsonException) { } }
        foreach (var spec in op.Parameters)
        {
            var row = ParamRow.Parse(spec);
            if (initial != null && initial.TryGetPropertyValue(row.Name, out var v) && v != null) row.Value = ParamRow.FromNode(v);
            else if (PlainDefault().IsMatch(row.Default)) row.Value = row.Default;     // the current value is visible and can be tweaked
            row.Changed += OnParamChanged;
            Parameters.Add(row);
        }
        OnPropertyChanged(nameof(SelectedOperation));
        OnPropertyChanged(nameof(SelectedChoice));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(OperationName));
        ResetCommand.RaiseCanExecuteChanged();
        RunPreview();
    }

    private void OnParamChanged()
    {
        _stale = true;
        if (_debounce == null) { RunPreview(); return; }
        _debounce.Stop();
        _debounce.Start();
        ApplyCommand.RaiseCanExecuteChanged();
    }

    private JsonObject BuildArgs()
    {
        var o = new JsonObject();
        foreach (var row in Parameters)
        {
            var node = row.ToNode();
            if (node == null) continue;
            if (row.Default.Length > 0 && ParamRow.FromNode(node) == row.Default) continue;   // the default value is not written into the JSON
            o[row.Name] = node;
        }
        return o;
    }

    /// <summary>Runs the operation with the current parameters on a copy of the layer; the layer does not change.</summary>
    public void RunPreview()
    {
        _stale = false;
        _debounce?.Stop();
        Error = "";
        Result = null;
        if (_operation == null) return;
        try
        {
            var before = _source();
            using var doc = JsonDocument.Parse(ArgsJson);
            var after = _operation.Run(before, new OpArgs(doc.RootElement.Clone(), null));
            _before = before;
            _after = after;
            Result = after;
            int cb = ColorStats.Count(before).Count, ca = ColorStats.Count(after).Count;
            if (after.Width == before.Width && after.Height == before.Height)
            {
                int diff = 0;
                for (int i = 0; i < after.Pixels.Length; i++) if (after.Pixels[i] != before.Pixels[i]) diff++;
                Changed = diff > 0;
                Summary = diff == 0 ? Loc.T("tuner.nothing_changed") : Loc.T("tuner.changed_pixels", diff, after.Pixels.Length, cb, ca);
            }
            else
            {
                Changed = true;
                Summary = Loc.T("tuner.size_changed", before.Width, before.Height, after.Width, after.Height, cb, ca);
            }
        }
        catch (Exception ex)
        {
            _after = null;
            Changed = false;
            Error = ex.Message;
            Summary = "";
        }
        OnPropertyChanged(nameof(Before));
        OnPropertyChanged(nameof(After));
        OnPropertyChanged(nameof(Changed));
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(ArgsJson));
        ApplyCommand.RaiseCanExecuteChanged();
    }

    private void Apply()
    {
        if (_stale) RunPreview();                      // pressed right after an edit: apply exactly what is visible
        if (!CanApply) return;
        Close(true);
    }

    protected override void OnClosed() => _debounce?.Stop();
}
