// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Text.Json;

namespace PixelEditor.Core.Ops;

public sealed record StepLog(int Index, string Op, int Width, int Height, int OpaquePixels, int Colors, string? Error);

/// <summary>The result of one check. Required=false: the check warns but does not fail the template.</summary>
public sealed record CheckLog(int Index, string Check, bool Passed, bool Required, string Message);

public sealed record PipelineResult(PixelImage Image, IReadOnlyList<StepLog> Log, IReadOnlyList<CheckLog> Checks)
{
    /// <summary>All steps ran without errors.</summary>
    public bool Ok => Log.All(l => l.Error == null);

    /// <summary>All required checks passed (or there are no checks).</summary>
    public bool ChecksPassed => Checks.All(c => c.Passed || !c.Required);

    /// <summary>The steps ran and the result passed the checks.</summary>
    public bool Success => Ok && ChecksPassed;
}

/// <summary>
/// A processing template: JSON
/// {"name":..., "description":..., "steps":[{"op":"resize","width":32}, ...], "checks":[{"check":"maxColors","value":16}, ...]}.
/// Steps run in order, then the checks are applied to the final picture.
/// </summary>
public sealed class Pipeline
{
    public string Name { get; init; } = "pipeline";
    public string? Description { get; init; }
    public IReadOnlyList<JsonElement> Steps { get; init; } = [];
    public IReadOnlyList<JsonElement> CheckSteps { get; init; } = [];
    public string? BaseDir { get; init; }

    /// <summary>Declared parameters (the optional top-level "params" list); empty for old templates.</summary>
    public IReadOnlyList<PipelineParam> Params { get; init; } = [];

    /// <summary>Values that win over defaults but lose to explicit overrides (set when a template is found by an old alias name).</summary>
    public IReadOnlyDictionary<string, object?>? Preset { get; init; }

    /// <summary>Problems found while reading "params"; reported by <see cref="Validate"/>.</summary>
    private IReadOnlyList<string> ParamErrors { get; init; } = [];

    /// <summary>A copy that uses <paramref name="preset"/> as its base values (see <see cref="Preset"/>).</summary>
    public Pipeline WithPreset(IReadOnlyDictionary<string, object?>? preset) => new()
    {
        Name = Name, Description = Description, Steps = Steps, CheckSteps = CheckSteps, BaseDir = BaseDir, Params = Params, ParamErrors = ParamErrors, Preset = preset,
    };

    public static Pipeline Parse(string json, string? baseDir = null)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        var root = doc.RootElement;
        string name = "pipeline";
        string? desc = null;
        List<JsonElement> steps = [], checks = [];
        List<PipelineParam> ps = [];
        List<string> paramErrors = [];

        if (root.ValueKind == JsonValueKind.Array)
        {
            steps = root.EnumerateArray().Select(e => e.Clone()).ToList();
        }
        else if (root.ValueKind == JsonValueKind.Object)
        {
            bool hasSteps = root.TryGetProperty("steps", out var stepsEl) && stepsEl.ValueKind == JsonValueKind.Array;
            bool hasChecks = root.TryGetProperty("checks", out var checksEl) && checksEl.ValueKind == JsonValueKind.Array;
            if (!hasSteps && !hasChecks)
                throw new FormatException(CoreText.T("Шаблон должен содержать массив 'steps' и/или 'checks'"));
            if (hasSteps) steps = stepsEl.EnumerateArray().Select(e => e.Clone()).ToList();
            if (hasChecks) checks = checksEl.EnumerateArray().Select(e => e.Clone()).ToList();
            if (root.TryGetProperty("params", out var pl) && pl.ValueKind != JsonValueKind.Null) ps = PipelineParams.Parse(pl, paramErrors);
            if (root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String) name = n.GetString()!;
            if (root.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String) desc = d.GetString();
        }
        else
            throw new FormatException(CoreText.T("Шаблон должен быть объектом или массивом шагов"));

        for (int i = 0; i < steps.Count; i++)
            if (steps[i].ValueKind != JsonValueKind.Object || !steps[i].TryGetProperty("op", out var op) || op.ValueKind != JsonValueKind.String)
                throw new FormatException(CoreText.F("Шаг {0}: нужен объект со строковым полем 'op'", i + 1));
        for (int i = 0; i < checks.Count; i++)
            if (checks[i].ValueKind != JsonValueKind.Object || !checks[i].TryGetProperty("check", out var c) || c.ValueKind != JsonValueKind.String)
                throw new FormatException(CoreText.F("Проверка {0}: нужен объект со строковым полем 'check'", i + 1));

        return new Pipeline { Name = name, Description = desc, Steps = steps, CheckSteps = checks, BaseDir = baseDir, Params = ps, ParamErrors = paramErrors };
    }

    public static Pipeline Load(string path) =>
        Parse(File.ReadAllText(path), Path.GetDirectoryName(Path.GetFullPath(path)));

    /// <summary>Runs the steps, then the checks. If a step fails it stops and does not run the checks.</summary>
    public PipelineResult Run(PixelImage input) => Run(input, null, null);

    /// <summary>Restores the protected pixels before checking the result.</summary>
    public PipelineResult Run(PixelImage input, PixelImage? protectMask) => Run(input, protectMask, null);

    /// <summary>The parameter values a run would use: defaults, then the preset of an alias, then <paramref name="overrides"/>. Throws ArgumentException for an unknown name or a bad value.</summary>
    public IReadOnlyDictionary<string, object> ResolveParams(IEnumerable<KeyValuePair<string, object?>>? overrides = null) =>
        PipelineParams.Resolve(Params, Preset, overrides);

    /// <summary>Runs with parameter overrides (name to value; null: defaults).</summary>
    public PipelineResult Run(PixelImage input, PixelImage? protectMask, IEnumerable<KeyValuePair<string, object?>>? overrides)
    {
        var values = ResolveParams(overrides);
        if (protectMask != null &&
            (protectMask.Width != input.Width || protectMask.Height != input.Height))
            throw new ArgumentException(CoreText.T("Размер маски защиты должен совпадать с размером исходника."));

        var log = new List<StepLog>();
        var cur = input;
        for (int i = 0; i < Steps.Count; i++)
        {
            var step = Steps[i];
            string opName = step.GetProperty("op").GetString()!;
            try
            {
                if (!PipelineParams.IsActive(step, Params, values)) continue;
                var op = Operations.Get(opName);
                cur = op.Run(cur, new OpArgs(PipelineParams.Substitute(step, Params, values), BaseDir));
                log.Add(new StepLog(i + 1, opName, cur.Width, cur.Height, cur.OpaqueCount(), cur.DistinctColors(), null));
            }
            catch (Exception ex)
            {
                log.Add(new StepLog(i + 1, opName, cur.Width, cur.Height, cur.OpaqueCount(), cur.DistinctColors(), ex.Message));
                return new PipelineResult(cur, log, []);
            }
        }
        if (protectMask != null)
        {
            if (cur.Width != input.Width || cur.Height != input.Height)
            {
                log.Add(new StepLog(log.Count + 1, "protectMask", cur.Width, cur.Height,
                    cur.OpaqueCount(), cur.DistinctColors(), CoreText.T("Обработка с маской защиты требует сохранить размер исходника.")));
                return new PipelineResult(cur, log, []);
            }
            cur = cur.Clone();
            for (int i = 0; i < cur.Pixels.Length; i++)
                if (!Pixel.IsEmpty(protectMask.Pixels[i])) cur.Pixels[i] = input.Pixels[i];
        }
        return new PipelineResult(cur, log, RunChecks(cur, values));
    }

    /// <summary>Validates a template without running it: whether all operations and checks exist. An empty list: no errors.</summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        for (int i = 0; i < Steps.Count; i++)
        {
            var op = Steps[i].GetProperty("op").GetString()!;
            try { Operations.Get(op); } catch (Exception ex) { problems.Add(CoreText.F("шаг {0}: {1}", i + 1, ex.Message)); }
        }
        for (int i = 0; i < CheckSteps.Count; i++)
        {
            var c = CheckSteps[i].GetProperty("check").GetString()!;
            try { Checks.Get(c); } catch (Exception ex) { problems.Add(CoreText.F("проверка {0}: {1}", i + 1, ex.Message)); }
        }
        problems.AddRange(ValidateParams());
        return problems;
    }

    /// <summary>
    /// Only the problems with parameters: broken definitions, defaults, every "$name" and "when" (declared, the type fits the operation or check parameter).
    /// Unknown operations and checks are not reported here.
    /// </summary>
    public IReadOnlyList<string> ValidateParams()
    {
        var problems = new List<string>();
        foreach (var e in ParamErrors) problems.Add(CoreText.T("params: ") + e);
        try { ResolveParams(); } catch (ArgumentException ex) { problems.Add(CoreText.T("params: ") + ex.Message); }
        for (int i = 0; i < Steps.Count; i++)
        {
            var op = Operations.All.FirstOrDefault(o => o.Name.Equals(Steps[i].GetProperty("op").GetString(), StringComparison.OrdinalIgnoreCase));
            PipelineParams.ValidateReferences(Steps[i], Params, op?.Parameters ?? [], CoreText.F("шаг {0}", i + 1), problems);
        }
        for (int i = 0; i < CheckSteps.Count; i++)
        {
            var c = Checks.All.FirstOrDefault(x => x.Name.Equals(CheckSteps[i].GetProperty("check").GetString(), StringComparison.OrdinalIgnoreCase));
            PipelineParams.ValidateReferences(CheckSteps[i], Params, c?.Parameters ?? [], CoreText.F("проверка {0}", i + 1), problems);
        }
        return problems;
    }

    /// <summary>Only the checks, without steps: for ready pictures.</summary>
    public IReadOnlyList<CheckLog> RunChecks(PixelImage image, IEnumerable<KeyValuePair<string, object?>>? overrides = null) =>
        RunChecks(image, ResolveParams(overrides));

    private IReadOnlyList<CheckLog> RunChecks(PixelImage image, IReadOnlyDictionary<string, object> values)
    {
        var results = new List<CheckLog>();
        for (int i = 0; i < CheckSteps.Count; i++)
        {
            string name = CheckSteps[i].GetProperty("check").GetString()!;
            bool required = true;
            try
            {
                if (!PipelineParams.IsActive(CheckSteps[i], Params, values)) continue;
                var el = PipelineParams.Substitute(CheckSteps[i], Params, values);
                var args = new OpArgs(el, BaseDir);
                required = args.Bool("required", true);
                var r = Checks.Get(name).Run(image, args);
                results.Add(new CheckLog(i + 1, name, r.Passed, required, r.Message));
            }
            catch (Exception ex)
            {
                results.Add(new CheckLog(i + 1, name, false, required, CoreText.T("ошибка проверки: ") + ex.Message));
            }
        }
        return results;
    }
}
