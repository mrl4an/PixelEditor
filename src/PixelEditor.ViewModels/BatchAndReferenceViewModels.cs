// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Collections.ObjectModel;
using PixelEditor.Core.Ops;
using PixelEditor.Core.Plugins;

namespace PixelEditor.ViewModels;

/// <summary>Kind: ok, checks_failed or error (it colors the status; the Status text depends on the language).</summary>
public sealed record BatchRow(string File, string Status, string Details, string Kind = "");

/// <summary>One template for a whole folder of pictures. The results are PNGs, the report shows what passed and what did not.</summary>
public sealed class BatchViewModel : DialogViewModel
{
    private readonly IDialogService _dialogs;
    private readonly IBatchRunner _runner;
    private TemplateItem? _template;
    private string _inDir = "", _outDir = "", _summary = "";
    private bool _recurse, _running;

    public BatchViewModel(AppServices services, PipelineLibrary library)
    {
        _dialogs = services.Dialogs;
        _runner = services.Batch;
        foreach (var t in library.All()) Templates.Add(TemplateItem.From(t));
        _template = Templates.FirstOrDefault();

        BrowseTemplateCommand = new RelayCommand(BrowseTemplate);
        BrowseInputCommand = new RelayCommand(BrowseInput);
        BrowseOutputCommand = new RelayCommand(BrowseOutput);
        RunCommand = new RelayCommand(() => _ = RunAsync(), () => !_running);
    }

    public override string Title => Loc.T("batch.batch_process_folder");

    public ObservableCollection<TemplateItem> Templates { get; } = new();
    public ObservableCollection<BatchRow> Rows { get; } = new();

    public TemplateItem? SelectedTemplate { get => _template; set => SetProperty(ref _template, value); }
    public string InputDir { get => _inDir; set => SetProperty(ref _inDir, value); }
    public string OutputDir { get => _outDir; set => SetProperty(ref _outDir, value); }
    public bool Recurse { get => _recurse; set => SetProperty(ref _recurse, value); }
    public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }
    public bool IsRunning { get => _running; private set { if (SetProperty(ref _running, value)) RunCommand.RaiseCanExecuteChanged(); } }

    public RelayCommand BrowseTemplateCommand { get; }
    public RelayCommand BrowseInputCommand { get; }
    public RelayCommand BrowseOutputCommand { get; }
    public RelayCommand RunCommand { get; }

    private void BrowseTemplate()
    {
        var path = _dialogs.PickOpenFile(Loc.T("auto.pipeline_json_json"));
        if (path == null) return;
        var item = new TemplateItem(Path.GetFileName(path), path);
        Templates.Add(item);
        SelectedTemplate = item;
    }

    private void BrowseInput()
    {
        var dir = _dialogs.PickFolder(Loc.T("batch.images_folder"));
        if (dir == null) return;
        InputDir = dir;
        if (string.IsNullOrWhiteSpace(_outDir)) OutputDir = Path.Combine(dir, "processed");
    }

    private void BrowseOutput()
    {
        var dir = _dialogs.PickFolder(Loc.T("batch.output_folder"));
        if (dir != null) OutputDir = dir;
    }

    public async Task RunAsync()
    {
        if (_template == null) { Summary = Loc.T("batch.choose_a_template"); return; }
        var inDir = _inDir.Trim();
        var outDir = _outDir.Trim();
        if (inDir.Length == 0 || outDir.Length == 0) { Summary = Loc.T("batch.specify_both_folders"); return; }

        IsRunning = true;
        Rows.Clear();
        Summary = Loc.T("batch.processing");
        try
        {
            var report = await _runner.RunAsync(_template.Source, inDir, outDir, _recurse);
            foreach (var i in report.Items)
                Rows.Add(new BatchRow(i.File,
                    i.Status switch { "ok" => Loc.T("batch.done"), "checks_failed" => Loc.T("batch.checks_failed"), _ => Loc.T("batch.error_3") },
                    string.Join("; ", i.Problems), i.Status is "ok" or "checks_failed" ? i.Status : "error"));
            Summary = Loc.T("batch.total_done_checks_failed_errors", report.Total, report.Ok, report.ChecksFailed, report.Errors);
        }
        catch (Exception ex)
        {
            Summary = Loc.T("batch.error_2") + ex.Message;
        }
        finally
        {
            IsRunning = false;
        }
    }
}

/// <summary>A reference of what is available to templates and AI: operations, checks, plugins.</summary>
public sealed class ReferenceViewModel : DialogViewModel
{
    private readonly IShellService _shell;

    public ReferenceViewModel(IShellService shell, string templatesDirectory)
    {
        _shell = shell;

        static string Params(IEnumerable<string> p) => p.Any() ? Loc.T("batch.parameters") + string.Join(", ", p) : Loc.T("batch.no_parameters");

        Operations = Core.Ops.Operations.All.OrderBy(o => o.Source != "builtin").ThenBy(o => o.Name)
            .Select(o => $"{o.Name}   [{o.Source}]\n{o.Description}\n{Params(o.Parameters)}").ToList();
        Checks = Core.Ops.Checks.All.OrderBy(c => c.Source != "builtin").ThenBy(c => c.Name)
            .Select(c => $"{c.Name}   [{c.Source}]\n{c.Description}\n{Params(c.Parameters)}").ToList();

        var plugins = PluginLoader.Loaded.Select(p =>
            $"{p.File}\n{Loc.T("batch.plugin_operations", p.Operations.Count == 0 ? Loc.T("batch.none") : string.Join(", ", p.Operations))}; " +
            Loc.T("batch.plugin_checks", p.Checks.Count == 0 ? Loc.T("batch.none") : string.Join(", ", p.Checks)) +
            (p.Error != null ? Loc.T("batch.error", p.Error) : "")).ToList();
        if (plugins.Count == 0)
            plugins.Add(Loc.T("batch.no_plugins_put_a_dll") +
                        Loc.T("batch.how_to_write_a_plugin"));
        Plugins = plugins;

        OpenPluginsFolderCommand = new RelayCommand(() => _shell.OpenFolder(PluginLoader.DefaultDirectories().Last()));
        OpenTemplatesFolderCommand = new RelayCommand(() => _shell.OpenFolder(templatesDirectory));
    }

    public override string Title => Loc.T("batch.reference_operations_checks_plugins");
    public IReadOnlyList<string> Operations { get; }
    public IReadOnlyList<string> Checks { get; }
    public IReadOnlyList<string> Plugins { get; }

    public string AiText =>
        Loc.T("batch.the_ai_works_with_the") +
        Loc.T("batch.command_line_pixed_exe") +
        "  pixed ops | checks | plugins\n" +
        Loc.T("batch.pixed_run_template_json_input") +
        Loc.T("batch.pixed_batch_template_json_input") +
        Loc.T("batch.mcp_server_pixed_mcp_exe") +
        Loc.T("batch.claude_mcp_add_pixel_editor") +
        Loc.T("batch.details_and_rules_for_agents");

    public RelayCommand OpenPluginsFolderCommand { get; }
    public RelayCommand OpenTemplatesFolderCommand { get; }
}
