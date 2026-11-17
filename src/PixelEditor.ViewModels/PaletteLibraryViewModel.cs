// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Collections.ObjectModel;
using PixelEditor.Core;

namespace PixelEditor.ViewModels;

/// <summary>A palette in the library list.</summary>
public sealed class PaletteItem(PaletteInfo info)
{
    public PaletteInfo Info { get; } = info;
    public string Id => Info.Id;
    public string Name => Info.Name;
    public string Description => Info.Description;
    public bool IsSystem => Info.IsSystem;
    public IReadOnlyList<uint> Colors => Info.Colors;
    public string Kind => Info.IsSystem ? Loc.T("palette_lib.system") : Loc.T("palette_lib.custom");
    public string Summary => Loc.T("palette_lib.col", Info.Colors.Count, Kind);
}

/// <summary>
/// The palette library: system (immutable) and your own. A palette can be applied to the project (replace or extend),
/// the current project palette can be saved under your own name, renamed, your own deleted, imported and exported as a file.
/// </summary>
public sealed class PaletteLibraryViewModel : DialogViewModel
{
    private readonly ColorsViewModel _colors;
    private readonly PaletteLibrary _library;
    private readonly IDialogService _dialogs;
    private readonly IShellService _shell;
    private PaletteItem? _selected;
    private string _message = "";
    private uint? _selectedColor;

    public PaletteLibraryViewModel(ColorsViewModel colors, PaletteLibrary library, IDialogService dialogs, IShellService shell)
    {
        _colors = colors;
        _library = library;
        _dialogs = dialogs;
        _shell = shell;

        UseCommand = new RelayCommand(Use, () => _selected != null);
        AppendCommand = new RelayCommand(Append, () => _selected != null);
        SaveCurrentCommand = new RelayCommand(SaveCurrent, () => _colors.Palette.Count > 0);
        DuplicateCommand = new RelayCommand(Duplicate, () => _selected != null);
        EditCommand = new RelayCommand(Edit, () => _selected is { IsSystem: false });
        UpdateColorsCommand = new RelayCommand(UpdateColors, () => _selected is { IsSystem: false } && _colors.Palette.Count > 0);
        DeleteCommand = new RelayCommand(Delete, () => _selected is { IsSystem: false });
        NewCommand = new RelayCommand(() => { _colors.NewPalette(); if (_colors.Palette.Count == 0) Close(true); });
        AddNewCommand = new RelayCommand(AddNew);
        AddColorCommand = new RelayCommand(AddColor, () => _selected is { IsSystem: false });
        RemoveColorCommand = new RelayCommand(RemoveColor, () => _selected is { IsSystem: false, Colors.Count: > 1 } && _selectedColor != null);
        ImportCommand = new RelayCommand(Import);
        ExportCommand = new RelayCommand(Export, () => _selected != null);
        OpenFolderCommand = new RelayCommand(() => _shell.OpenFolder(_library.UserDirectory));
        Reload();
    }

    public override string Title => Loc.T("palette_lib.palette_library");

    public ObservableCollection<PaletteItem> Items { get; } = new();

    public PaletteItem? Selected
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value)) return;
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(CanModify));
            OnPropertyChanged(nameof(SystemHint));
            SelectedColor = null;
            AddColorCommand.RaiseCanExecuteChanged();
            RemoveColorCommand.RaiseCanExecuteChanged();
            RaiseCommands();
        }
    }

    public bool HasSelection => _selected != null;

    /// <summary>Whether the selected palette can be changed or deleted (only your own).</summary>
    public bool CanModify => _selected is { IsSystem: false };

    public string SystemHint => _selected is { IsSystem: true }
        ? Loc.T("palette_lib.a_system_palette_cannot_be")
        : "";

    /// <summary>The result of the last action: what happened or why not.</summary>
    public string Message { get => _message; private set => SetProperty(ref _message, value); }

    public string CurrentInfo => _colors.Palette.Count == 0
        ? Loc.T("palette_lib.the_project_has_an_empty")
        : Loc.T("palette_lib.project_palette_col", _colors.PaletteTitle, _colors.Palette.Count);

    public RelayCommand UseCommand { get; }
    public RelayCommand AppendCommand { get; }
    public RelayCommand SaveCurrentCommand { get; }
    public RelayCommand DuplicateCommand { get; }
    public RelayCommand EditCommand { get; }
    public RelayCommand UpdateColorsCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand NewCommand { get; }
    public RelayCommand AddNewCommand { get; }
    public RelayCommand AddColorCommand { get; }
    public RelayCommand RemoveColorCommand { get; }

    /// <summary>The color selected in the palette on the right (for deletion).</summary>
    public uint? SelectedColor
    {
        get => _selectedColor;
        set { if (SetProperty(ref _selectedColor, value)) RemoveColorCommand.RaiseCanExecuteChanged(); }
    }

    public RelayCommand ImportCommand { get; }
    public RelayCommand ExportCommand { get; }
    public RelayCommand OpenFolderCommand { get; }

    private void RaiseCommands()
    {
        UseCommand.RaiseCanExecuteChanged();
        AppendCommand.RaiseCanExecuteChanged();
        SaveCurrentCommand.RaiseCanExecuteChanged();
        DuplicateCommand.RaiseCanExecuteChanged();
        EditCommand.RaiseCanExecuteChanged();
        UpdateColorsCommand.RaiseCanExecuteChanged();
        DeleteCommand.RaiseCanExecuteChanged();
        ExportCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Rereads the library and selects the palette with the given id (or the first).</summary>
    public void Reload(string? selectId = null)
    {
        Items.Clear();
        foreach (var p in _library.All()) Items.Add(new PaletteItem(p));
        Selected = Items.FirstOrDefault(i => i.Id == selectId) ?? Items.FirstOrDefault();
        OnPropertyChanged(nameof(CurrentInfo));
    }

    private string? NameProblem(string name, string? exceptId)
    {
        var clash = _library.All().FirstOrDefault(p =>
            p.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase) && !string.Equals(p.Id, exceptId, StringComparison.OrdinalIgnoreCase));
        return clash == null ? null : clash.IsSystem ? Loc.T("palette_lib.this_name_is_taken_by") : Loc.T("colors.a_palette_with_this_name");
    }

    /// <summary>"+ Add": a new own palette. Asks for the name and description, puts the primary color as the first color; the rest are added with the "+ Primary color" button.</summary>
    private void AddNew()
    {
        var dlg = new NameDescriptionViewModel(Loc.T("colors.new_palette"), Unique(Loc.T("colors.new_palette")), "", n => NameProblem(n, null),
            [_colors.Primary], Loc.T("new_doc.create"),
            Loc.T("palette_lib.the_primary_color_becomes_the"));
        if (_dialogs.ShowDialog(dlg) != true) return;
        var first = _colors.Primary;
        Persist(() => _library.Save(dlg.Name, dlg.Description, [first]), Loc.T("palette_lib.palette_created_add_colors", dlg.Name));
        // the new palette becomes the project palette at once: add colors with the "+ To palette" button under the color
        if (_selected is { IsSystem: false } created) _colors.ApplyPalette(created.Info);
    }

    private void AddColor()
    {
        if (_selected is not { IsSystem: false } item) return;
        var c = _colors.Primary;
        if (item.Colors.Contains(c)) { Message = Loc.T("palette_lib.this_color_is_already_in"); return; }
        Persist(() => _library.Save(item.Name, item.Description, item.Colors.Append(c), item.Id), Loc.T("palette_lib.added", Pixel.ToHex(c)));
        _colors.RefreshFromLibrary(item.Id);
    }

    private void RemoveColor()
    {
        if (_selected is not { IsSystem: false } item || _selectedColor is not uint c || item.Colors.Count < 2) return;
        Persist(() => _library.Save(item.Name, item.Description, item.Colors.Where(x => x != c), item.Id), Loc.T("palette_lib.removed", Pixel.ToHex(c)));
        _colors.RefreshFromLibrary(item.Id);
    }

    private void Use()
    {
        if (_selected == null) return;
        _colors.ApplyPalette(_selected.Info);
        Close(true);
    }

    private void Append()
    {
        if (_selected == null) return;
        int added = _colors.AppendColors(_selected.Colors);
        Message = added == 0 ? Loc.T("palette_lib.all_colors_are_already_in") : Loc.T("palette_lib.colors_added", added);
        OnPropertyChanged(nameof(CurrentInfo));
    }

    /// <summary>Saves the project palette to the library: asks for the name and description.</summary>
    private void SaveCurrent()
    {
        var dlg = new NameDescriptionViewModel(Loc.T("colors.save_palette_to_library"), "", "", n => NameProblem(n, null),
            _colors.Palette.ToList(), Loc.T("colors.save"), Loc.T("colors.the_palette_will_be_available"));
        if (_dialogs.ShowDialog(dlg) != true) return;
        Persist(() => _library.Save(dlg.Name, dlg.Description, _colors.Palette), Loc.T("palette_lib.palette_saved", dlg.Name));
    }

    private void Duplicate()
    {
        if (_selected == null) return;
        var baseName = _selected.Name + Loc.T("palette_lib.copy");
        var dlg = new NameDescriptionViewModel(Loc.T("palette_lib.make_your_own_copy"), Unique(baseName), _selected.Description, n => NameProblem(n, null),
            _selected.Colors, Loc.T("palette_lib.create_copy"));
        if (_dialogs.ShowDialog(dlg) != true) return;
        var colors = _selected.Colors;
        Persist(() => _library.Save(dlg.Name, dlg.Description, colors), Loc.T("palette_lib.palette_created", dlg.Name));
    }

    private string Unique(string name)
    {
        var result = name;
        for (int i = 2; NameProblem(result, null) != null; i++) result = $"{name} {i}";
        return result;
    }

    private void Edit()
    {
        if (_selected is not { IsSystem: false } item) return;
        var dlg = new NameDescriptionViewModel(Loc.T("palette_lib.name_and_description_2"), item.Name, item.Description, n => NameProblem(n, item.Id),
            item.Colors, Loc.T("colors.save"));
        if (_dialogs.ShowDialog(dlg) != true) return;
        var colors = item.Colors;
        Persist(() => _library.Save(dlg.Name, dlg.Description, colors, item.Id), Loc.T("palette_lib.saved", dlg.Name));
    }

    /// <summary>Replaces the colors of the selected own palette with the project palette colors.</summary>
    private void UpdateColors()
    {
        if (_selected is not { IsSystem: false } item) return;
        if (!_dialogs.ConfirmDestructive(Loc.T("palette_lib.update_palette"),
                Loc.T("palette_lib.replace_the_colors_of_palette", item.Name, item.Colors.Count, _colors.Palette.Count), Loc.T("palette_lib.replace"))) return;
        Persist(() => _library.Save(item.Name, item.Description, _colors.Palette, item.Id), Loc.T("palette_lib.palette_updated", item.Name));
    }

    private void Delete()
    {
        if (_selected is not { IsSystem: false } item) return;
        if (!_dialogs.ConfirmDestructive(Loc.T("palette_lib.delete_palette"), Loc.T("palette_lib.delete_palette_projects_where_it", item.Name))) return;
        var index = Items.IndexOf(item);
        try
        {
            if (!_library.Delete(item.Id)) { Message = Loc.T("palette_lib.could_not_delete_the_palette"); return; }
        }
        catch (Exception ex) { _dialogs.ShowError(ex); return; }
        Reload();
        if (Items.Count > 0) Selected = Items[Math.Min(index, Items.Count - 1)];
        Message = Loc.T("palette_lib.deleted", item.Name);
    }

    private void Import()
    {
        var path = _dialogs.PickOpenFile(Loc.T("colors.palettes_hex_gpl_hex_gpl"));
        if (path == null) return;
        try
        {
            var colors = PaletteIO.Load(path);
            if (colors.Count == 0) { Message = Loc.T("colors.the_file_contains_no_colors"); return; }
            var dlg = new NameDescriptionViewModel(Loc.T("palette_lib.import_palette"), Unique(Path.GetFileNameWithoutExtension(path)), "", n => NameProblem(n, null),
                colors, Loc.T("palette_lib.add"));
            if (_dialogs.ShowDialog(dlg) != true) return;
            Persist(() => _library.Save(dlg.Name, dlg.Description, colors), Loc.T("palette_lib.palette_imported", dlg.Name));
        }
        catch (Exception ex) { _dialogs.ShowError(ex); }
    }

    private void Export()
    {
        if (_selected == null) return;
        var path = _dialogs.PickSaveFile("Lospec .hex (*.hex)|*.hex|GIMP .gpl (*.gpl)|*.gpl", PaletteLibrary.Slug(_selected.Name) + ".hex");
        if (path == null) return;
        try { PaletteIO.Save(path, _selected.Colors); Message = Loc.T("palette_lib.palette_exported"); }
        catch (Exception ex) { _dialogs.ShowError(ex); }
    }

    private void Persist(Func<PaletteInfo> save, string successMessage)
    {
        try
        {
            var info = save();
            Reload(info.Id);
            Message = successMessage;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowError(ex);
        }
    }
}
