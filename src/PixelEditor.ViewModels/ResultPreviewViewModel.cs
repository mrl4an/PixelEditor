// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Text;
using PixelEditor.Core;
using PixelEditor.Core.Ops;

namespace PixelEditor.ViewModels;

/// <summary>
/// A template result preview before applying: "before" on the left, "after" on the right, what changed, the step and check log.
/// The layer is not touched until the user presses "Apply" (or "Open in a new document" if the result size differs).
/// </summary>
public sealed class ResultPreviewViewModel : DialogViewModel
{
    public ResultPreviewViewModel(string templateName, PixelImage before, PipelineResult result)
    {
        TemplateName = templateName;
        Before = before;
        After = result.Image;
        SameSize = before.Width == After.Width && before.Height == After.Height;

        int colorsBefore = ColorStats.Count(before).Count, colorsAfter = ColorStats.Count(After).Count;
        var info = new List<string> { Loc.T("result_preview.size", before.Width, before.Height, After.Width, After.Height), Loc.T("result_preview.colors", colorsBefore, colorsAfter) };
        if (SameSize)
        {
            int changed = 0;
            for (int i = 0; i < before.Pixels.Length; i++)
                if (before.Pixels[i] != After.Pixels[i]) changed++;
            ChangedPixels = changed;
            info.Add(changed == 0 ? Loc.T("result_preview.pixels_did_not_change_the") : Loc.T("result_preview.pixels_to_change", changed));
        }
        Summary = info;

        var steps = result.Log.Select(l => l.Error != null
            ? $"✗ {l.Index}. {l.Op}: {l.Error}"
            : Loc.T("pipeline_editor.colors", l.Index, l.Op, l.Width, l.Height, l.Colors)).ToList();
        Steps = steps;
        Checks = result.Checks.Select(c => $"{(c.Passed ? "✓" : c.Required ? "✗" : "!")} {c.Check}: {c.Message}").ToList();
        ChecksOk = result.ChecksPassed;
        HasChecks = result.Checks.Count > 0;

        ApplyText = SameSize ? Loc.T("template_lib.apply_to_layer") : Loc.T("result_preview.open_in_a_new_document");
        Explanation = SameSize
            ? Loc.T("result_preview.the_result_will_replace_the")
            : Loc.T("result_preview.the_result_size_does_not", After.Width, After.Height, before.Width, before.Height) +
              Loc.T("result_preview.the_result_will_open_in");
        ApplyCommand = new RelayCommand(() => Close(true));
    }

    public override string Title => Loc.T("result_preview.result_of_template", TemplateName);
    public string TemplateName { get; }
    public PixelImage Before { get; }
    public PixelImage After { get; }
    public bool SameSize { get; }
    public int ChangedPixels { get; }
    public IReadOnlyList<string> Summary { get; }
    public IReadOnlyList<string> Steps { get; }
    public IReadOnlyList<string> Checks { get; }
    public bool ChecksOk { get; }
    public bool HasChecks { get; }
    public string ApplyText { get; }
    public string Explanation { get; }
    public string SummaryText => string.Join(Environment.NewLine, Summary);
    public string DetailsText => new StringBuilder().AppendJoin(Environment.NewLine, Steps).AppendLine().AppendJoin(Environment.NewLine, Checks).ToString().Trim();
    public RelayCommand ApplyCommand { get; }
}
