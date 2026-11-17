// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using PixelEditor.Core;
using PixelEditor.Core.Ops;

namespace PixelEditor.ViewModels;

/// <summary>A template in the library list.</summary>
public sealed class TemplateRow(TemplateInfo info)
{
    public TemplateInfo Info { get; } = info;
    public string Id => Info.Id;
    public string Name => Info.Name;
    public string Description => Info.Description;
    public bool IsSystem => Info.IsSystem;
    public string Kind => Info.IsSystem ? Loc.T("settings.system") : Loc.T("settings.custom");
    public string Summary => Loc.T("template_lib.steps_checks", Info.StepCount, Info.CheckCount, Kind);

    /// <summary>Steps by lines: "1. resize width=32 height=32".</summary>
    public IReadOnlyList<string> Outline => TemplateOutline.Describe(Info.Json);
}

public static class TemplateOutline
{
    /// <summary>A short description of the template steps and checks for the list.</summary>
    public static IReadOnlyList<string> Describe(string json)
    {
        var lines = new List<string>();
        try
        {
            var node = JsonNode.Parse(json);
            var steps = node is JsonArray a ? a : (node as JsonObject)?["steps"] as JsonArray;
            var checks = (node as JsonObject)?["checks"] as JsonArray;
            int n = 1;
            foreach (var s in steps?.OfType<JsonObject>() ?? []) lines.Add($"{n++}. {Line(s, "op")}");
            n = 1;
            foreach (var c in checks?.OfType<JsonObject>() ?? []) lines.Add(Loc.T("template_lib.check", n++, Line(c, "check")));
        }
        catch (JsonException) { lines.Add(Loc.T("template_lib.the_template_cannot_be_read")); }
        return lines;
    }

    private static string Line(JsonObject o, string key)
    {
        var parts = new List<string> { o[key]?.ToString() ?? "?" };
        foreach (var kv in o)
            if (kv.Key != key && kv.Value != null) parts.Add($"{kv.Key}={kv.Value.ToJsonString().Trim('"')}");
        return string.Join(" ", parts);
    }
}

/// <summary>
/// The processing template manager: system (standard) and your own. A template can be run on the current layer, a new one created with the builder,
/// a system one copied, your own changed or deleted, imported and exported as a JSON file.
/// </summary>
public sealed class TemplateLibraryViewModel : DialogViewModel
{
    private readonly EditorSession _s;
    private readonly AutomationViewModel _automation;
    private TemplateRow? _selected;
    private string _message = "";

    public TemplateLibraryViewModel(EditorSession session, AutomationViewModel automation)
    {
        _s = session;
        _automation = automation;
        RunCommand = new RelayCommand(Run, () => _selected != null);
        NewCommand = new RelayCommand(() => OpenEditor(null, asCopy: false));
        DuplicateCommand = new RelayCommand(() => OpenEditor(_selected?.Info, asCopy: true), () => _selected != null);
        EditCommand = new RelayCommand(() => OpenEditor(_selected?.Info, asCopy: false), () => _selected is { IsSystem: false });
        DeleteCommand = new RelayCommand(Delete, () => _selected is { IsSystem: false });
        ImportCommand = new RelayCommand(Import);
        ExportCommand = new RelayCommand(Export, () => _selected != null);
        OpenFolderCommand = new RelayCommand(() => _s.Services.Shell.OpenFolder(Library.UserDirectory));
        Reload();
    }

    public override string Title => Loc.T("template_lib.processing_templates");

    private PipelineLibrary Library => _s.Settings.Templates;
    public ObservableCollection<TemplateRow> Items { get; } = new();

    public TemplateRow? Selected
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value)) return;
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(CanModify));
            OnPropertyChanged(nameof(SystemHint));
            OnPropertyChanged(nameof(Outline));
            foreach (var c in new[] { RunCommand, DuplicateCommand, EditCommand, DeleteCommand, ExportCommand }) c.RaiseCanExecuteChanged();
        }
    }

    public bool HasSelection => _selected != null;
    public bool CanModify => _selected is { IsSystem: false };
    public IReadOnlyList<string> Outline => _selected?.Outline ?? [];

    public string SystemHint => _selected is { IsSystem: true }
        ? Loc.T("template_lib.a_system_template_cannot_be")
        : "";

    public string Message { get => _message; private set => SetProperty(ref _message, value); }

    public RelayCommand RunCommand { get; }
    public RelayCommand NewCommand { get; }
    public RelayCommand DuplicateCommand { get; }
    public RelayCommand EditCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand ImportCommand { get; }
    public RelayCommand ExportCommand { get; }
    public RelayCommand OpenFolderCommand { get; }

    public void Reload(string? selectId = null)
    {
        Items.Clear();
        foreach (var t in Library.All()) Items.Add(new TemplateRow(t));
        Selected = Items.FirstOrDefault(i => i.Id == selectId) ?? Items.FirstOrDefault();
        _automation.RefreshTemplates();
    }

    private void Run()
    {
        if (_selected == null) return;
        var item = TemplateItem.From(_selected.Info);
        Close(true);
        _automation.RunTemplate(item);
    }

    private void OpenEditor(TemplateInfo? source, bool asCopy)
    {
        if (source == null && asCopy) return;
        var editor = _automation.CreateEditor(source, asCopy);
        if (_s.Dialogs.ShowDialog(editor) == true)
        {
            Reload(editor.SavedId);
            Message = Loc.T("template_lib.template_saved", _selected?.Name);
        }
    }

    private void Delete()
    {
        if (_selected is not { IsSystem: false } row) return;
        if (!_s.Dialogs.ConfirmDestructive(Loc.T("rsi_dlg.delete_template"), Loc.T("template_lib.delete_template_this_cannot_be", row.Name))) return;
        int index = Items.IndexOf(row);
        try
        {
            if (!Library.Delete(row.Id)) { Message = Loc.T("template_lib.could_not_delete_the_template"); return; }
        }
        catch (Exception ex) { _s.Dialogs.ShowError(ex); return; }
        Reload();
        if (Items.Count > 0) Selected = Items[Math.Min(index, Items.Count - 1)];
        Message = Loc.T("template_lib.template_deleted", row.Name);
    }

    private void Import()
    {
        var path = _s.Dialogs.PickOpenFile(Loc.T("template_lib.template_json_json"));
        if (path == null) return;
        try
        {
            var json = File.ReadAllText(path);
            var parsed = Pipeline.Parse(json);
            var name = string.IsNullOrWhiteSpace(parsed.Name) || parsed.Name == "pipeline" ? Path.GetFileNameWithoutExtension(path) : parsed.Name;
            var info = Library.Save(name, parsed.Description ?? "", json);
            Reload(info.Id);
            Message = Loc.T("template_lib.template_imported", info.Name);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or JsonException or IOException)
        {
            _s.Dialogs.ShowError(ex);
        }
    }

    private void Export()
    {
        if (_selected == null) return;
        var path = _s.Dialogs.PickSaveFile(Loc.T("template_lib.template_json_json"), PaletteLibrary.Slug(_selected.Name) + ".json");
        if (path == null) return;
        try { File.WriteAllText(path, _selected.Info.Json); Message = Loc.T("template_lib.template_exported"); }
        catch (Exception ex) { _s.Dialogs.ShowError(ex); }
    }
}
