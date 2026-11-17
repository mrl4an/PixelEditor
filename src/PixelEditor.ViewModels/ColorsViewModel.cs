// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Collections.ObjectModel;
using PixelEditor.Core;

namespace PixelEditor.ViewModels;

/// <summary>The primary and secondary color, the palette and the statistics of used colors.</summary>
public sealed class ColorsViewModel : ObservableObject
{
    private readonly EditorSession _s;
    private string? _activeName;
    private List<uint>? _baseColors;
    private bool _syncing;
    private string? _activeUserId; // id of the own library palette if the project uses one: palette edits are saved into it at once
    private uint _primary = Pixel.Rgb(0, 0, 0);
    private uint _secondary = Pixel.Empty;
    private uint? _selectedPalette;
    private ColorCount? _selectedStat;
    private string _statsHeader = Loc.T("colors.used_colors");
    private bool _suppressStats;

    public ColorsViewModel(EditorSession session)
    {
        _s = session;
        var start = Library.Find("system:pico-8") ?? BuiltInPalettes.All[0];
        foreach (var c in start.Colors) Palette.Add(c);
        _activeName = start.Name;
        _baseColors = start.Colors.ToList();
        Palette.CollectionChanged += (_, _) => OnPaletteEdited();
        PaletteLibraryCommand = new RelayCommand(() => _s.Dialogs.ShowDialog(
            new PaletteLibraryViewModel(this, Library, _s.Dialogs, _s.Services.Shell)));
        SavePaletteToLibraryCommand = new RelayCommand(SaveToLibrary, () => Palette.Count > 0);
        NewPaletteCommand = new RelayCommand(NewPalette);

        AddToPaletteCommand = new RelayCommand(AddToPalette);
        RemoveFromPaletteCommand = new RelayCommand(RemoveSelectedFromPalette);
        PaletteFromImageCommand = new RelayCommand(PaletteFromImage);
        OpenPaletteCommand = new RelayCommand(OpenPalette);
        SavePaletteCommand = new RelayCommand(SavePalette);
        SortByLuminanceCommand = new RelayCommand(() => SetPalette(PaletteTools.SortByLuminance(Palette)));
        SortByHueCommand = new RelayCommand(() => SetPalette(PaletteTools.SortByHue(Palette)));
        RemoveUnusedCommand = new RelayCommand(RemoveUnused);
        ReplaceStatColorCommand = new RelayCommand(ReplaceStatColor);
        StatToPaletteCommand = new RelayCommand(StatToPalette);
        DeleteStatColorCommand = new RelayCommand(DeleteStatColor);
        ClearHighlightCommand = new RelayCommand(() => SelectedStat = null);
        // when the highlight is dropped elsewhere (eyedropper, tool action) the list selection must follow
        _s.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(EditorSession.Highlight) && _s.Highlight == null && _selectedStat != null)
            {
                _suppressStats = true;
                SelectedStat = null;
                _suppressStats = false;
            }
        };
        SwapColorsCommand = new RelayCommand(SwapColors);
        ShadesCommand = new RelayCommand(AddShades);
        _showHsv = _s.Settings.ColorPickerHsv;
        (_hue, _sat, _val) = Hsv.FromRgb(_primary);

        // the statistics are recomputed not after every stroke but when edits calm down
        _statsDebounce = new Debouncer(_s.Services.Timers, TimeSpan.FromMilliseconds(250), RefreshStats);
        _s.ContentChanged += _statsDebounce.Trigger;
        _s.DocumentChanged += OnDocumentChanged;
        RefreshStats();
        SyncToDoc();
    }

    private readonly Debouncer _statsDebounce;

    // ---- colors ----

    public uint Primary => _primary;
    public uint Secondary => _secondary;

    public double Red
    {
        get => Pixel.R(_primary);
        set => SetPrimary(Pixel.Rgb((byte)Math.Clamp(value, 0, 255), Pixel.G(_primary), Pixel.B(_primary)));
    }

    public double Green
    {
        get => Pixel.G(_primary);
        set => SetPrimary(Pixel.Rgb(Pixel.R(_primary), (byte)Math.Clamp(value, 0, 255), Pixel.B(_primary)));
    }

    public double Blue
    {
        get => Pixel.B(_primary);
        set => SetPrimary(Pixel.Rgb(Pixel.R(_primary), Pixel.G(_primary), (byte)Math.Clamp(value, 0, 255)));
    }

    /// <summary>The primary color as #RRGGBB. Invalid input restores the previous value.</summary>
    public string PrimaryHex
    {
        get => Pixel.ToHex(_primary);
        set
        {
            if (Pixel.TryParseHex(value, out var c)) SetPrimary(c);
            else OnPropertyChanged();
        }
    }

    // HSV view of the primary color. The values are remembered so that the picker does not jump:
    // a grey keeps its last hue, black keeps its last hue and saturation.
    private double _hue, _sat, _val;

    /// <summary>Hue of the primary color, 0..360.</summary>
    public double Hue
    {
        get => _hue;
        set => SetHsv(value, _sat, _val);
    }

    /// <summary>Saturation of the primary color, 0..1.</summary>
    public double Saturation
    {
        get => _sat;
        set => SetHsv(_hue, value, _val);
    }

    /// <summary>Value (brightness) of the primary color, 0..1.</summary>
    public double Value
    {
        get => _val;
        set => SetHsv(_hue, _sat, value);
    }

    /// <summary>Sets the primary color from HSV. The given HSV is kept as is (no round trip through RGB), so dragging is stable.</summary>
    public void SetHsv(double h, double s, double v)
    {
        h %= 360;
        if (h < 0) h += 360;
        _hue = h;
        _sat = Math.Clamp(s, 0, 1);
        _val = Math.Clamp(v, 0, 1);
        var c = Hsv.ToRgb(_hue, _sat, _val);
        if (c != _primary)
        {
            _primary = c;
            OnPropertyChanged(nameof(Primary));
            OnPropertyChanged(nameof(Red));
            OnPropertyChanged(nameof(Green));
            OnPropertyChanged(nameof(Blue));
            OnPropertyChanged(nameof(PrimaryHex));
        }
        OnPropertyChanged(nameof(Hue));
        OnPropertyChanged(nameof(Saturation));
        OnPropertyChanged(nameof(Value));
    }

    public void SetPrimary(uint c)
    {
        if (c == _primary) return;
        _primary = c;
        var (h, s, v) = Hsv.FromRgb(c);
        if (v > 0)
        {
            _sat = s;
            if (s > 0) _hue = h;
        }
        _val = v;
        OnPropertyChanged(nameof(Primary));
        OnPropertyChanged(nameof(Red));
        OnPropertyChanged(nameof(Green));
        OnPropertyChanged(nameof(Blue));
        OnPropertyChanged(nameof(PrimaryHex));
        OnPropertyChanged(nameof(Hue));
        OnPropertyChanged(nameof(Saturation));
        OnPropertyChanged(nameof(Value));
    }

    /// <summary>Shows the HSV square (true) or the RGB sliders (false) in the color section. Remembered in settings.</summary>
    public bool ShowHsv
    {
        get => _showHsv;
        set
        {
            if (!SetProperty(ref _showHsv, value)) return;
            OnPropertyChanged(nameof(ShowRgb));
            _s.Settings.ColorPickerHsv = value;
            _s.Settings.Save();
        }
    }

    public bool ShowRgb
    {
        get => !_showHsv;
        set => ShowHsv = !value;
    }

    private bool _showHsv = true;

    // ---- recently used colors ----

    public const int RecentLimit = 8;

    /// <summary>Colors that were actually painted with, most recent first (no duplicates, never empty). Session only.</summary>
    public ObservableCollection<uint> Recent { get; } = new();

    /// <summary>Remembers a color that was used to paint. Selecting a color does not count.</summary>
    public void NoteUsed(uint color)
    {
        if (Pixel.IsEmpty(color)) return;
        int i = Recent.IndexOf(color);
        if (i == 0) return;
        if (i > 0) Recent.Move(i, 0);
        else
        {
            Recent.Insert(0, color);
            while (Recent.Count > RecentLimit) Recent.RemoveAt(Recent.Count - 1);
        }
    }

    // ---- shade ramp ----

    public RelayCommand ShadesCommand { get; }

    /// <summary>Adds a ramp of 2 darker and 2 lighter shades of the primary color to the palette (after the colors already there).</summary>
    private void AddShades()
    {
        if (Pixel.IsEmpty(_primary)) { _s.Status(Loc.T("colors.shades_need_color")); return; }
        int added = AppendColors(PaletteTools.ShadeRamp(_primary));
        _s.Status(Loc.T("colors.shades_added", added));
    }

    public void SetSecondary(uint c)
    {
        if (SetProperty(ref _secondary, c, nameof(Secondary))) { }
    }

    private void SwapColors()
    {
        var (p, s) = (_primary, _secondary);
        SetPrimary(s);
        SetSecondary(p);
    }

    /// <summary>A click on a palette color: left button primary, right button secondary.</summary>
    public void PaletteClick(uint color, bool right)
    {
        if (right) SetSecondary(color); else SetPrimary(color);
    }

    // ---- palette ----

    public ObservableCollection<uint> Palette { get; } = new();

    public uint? SelectedPaletteColor
    {
        get => _selectedPalette;
        set => SetProperty(ref _selectedPalette, value);
    }

    public RelayCommand AddToPaletteCommand { get; }
    public RelayCommand RemoveFromPaletteCommand { get; }
    public RelayCommand PaletteFromImageCommand { get; }
    public RelayCommand OpenPaletteCommand { get; }
    public RelayCommand SavePaletteCommand { get; }
    public RelayCommand SortByLuminanceCommand { get; }
    public RelayCommand SortByHueCommand { get; }
    public RelayCommand RemoveUnusedCommand { get; }
    public RelayCommand SwapColorsCommand { get; }

    private void SetPalette(IEnumerable<uint> colors)
    {
        var list = colors.ToList();
        _syncing = true;
        Palette.Clear();
        foreach (var c in list) Palette.Add(c);
        _syncing = false;
        OnPaletteEdited();
    }

    private void AddToPalette()
    {
        if (Pixel.IsEmpty(_primary) || Palette.Contains(_primary)) return;
        Palette.Add(_primary);
    }

    private void RemoveSelectedFromPalette()
    {
        if (_selectedPalette is uint c) Palette.Remove(c);
    }

    private void PaletteFromImage()
    {
        var colors = ColorStats.Count(_s.Doc.Flatten()).Select(c => c.Color).Take(256).ToList();
        if (colors.Count == 0) return;
        SetPalette(colors);
        ForgetName();
        _s.Status(Loc.T("colors.palette_from_image_colors", colors.Count));
    }

    private void RemoveUnused()
    {
        int before = Palette.Count;
        SetPalette(PaletteTools.RemoveUnused(Palette, _s.Doc.Flatten()));
        _s.Status(Loc.T("colors.unused_colors_removed", before - Palette.Count));
    }

    private void OpenPalette()
    {
        var path = _s.Dialogs.PickOpenFile(Loc.T("colors.palettes_hex_gpl_hex_gpl"));
        if (path == null) return;
        try
        {
            var colors = PaletteIO.Load(path);
            if (colors.Count == 0) { _s.Status(Loc.T("colors.the_file_contains_no_colors")); return; }
            SetPalette(colors);
            ForgetName();
            _s.Status(Loc.T("colors.palette_loaded_colors", colors.Count));
        }
        catch (Exception ex) { _s.Dialogs.ShowError(ex); }
    }

    private void SavePalette()
    {
        var path = _s.Dialogs.PickSaveFile("Lospec .hex (*.hex)|*.hex|GIMP .gpl (*.gpl)|*.gpl", "palette.hex");
        if (path == null) return;
        try { PaletteIO.Save(path, Palette); _s.Status(Loc.T("colors.palette_saved")); }
        catch (Exception ex) { _s.Dialogs.ShowError(ex); }
    }


    // ---- palette library ----

    /// <summary>Palette library: system and your own, shared by all projects.</summary>
    public PaletteLibrary Library => _s.Settings.Palettes;

    public RelayCommand PaletteLibraryCommand { get; }
    public RelayCommand NewPaletteCommand { get; }

    /// <summary>Starts a new empty palette: colors are added with the "+ Primary color" button, the finished one can be saved to the library.</summary>
    public void NewPalette()
    {
        int n = Palette.Count;
        if (n > 0 && (_activeName == null || IsPaletteModified) &&
            !_s.Dialogs.ConfirmDestructive(Loc.T("colors.new_palette"),
                Loc.T("colors.start_a_new_palette_the", n), Loc.T("colors.start_new")))
            return;
        SetPalette([]);
        ForgetName();
        _s.Status(Loc.T("colors.empty_palette_choose_a_color"));
    }

    public RelayCommand SavePaletteToLibraryCommand { get; }

    /// <summary>The name of the applied library palette; null if the palette is your own (from a file, from a picture, typed by hand).</summary>
    public string? ActivePaletteName => _activeName;

    /// <summary>The palette differs from the one applied from the library.</summary>
    public bool IsPaletteModified => _baseColors != null && !_baseColors.SequenceEqual(Palette);

    public string PaletteTitle => _activeName == null ? Loc.T("colors.custom_palette") : _activeName + (IsPaletteModified ? " *" : "");

    /// <summary>Replaces the project palette with a library palette. The project is marked as changed: the palette is saved in the project file.</summary>
    public void ApplyPalette(PaletteInfo info)
    {
        SetPalette(info.Colors);
        _activeName = info.Name;
        _activeUserId = info.IsSystem ? null : info.Id;
        _baseColors = info.Colors.ToList();
        SyncToDoc();
        _s.Dirty = true;
        OnPaletteTitleChanged();
        _s.Status(Loc.T("colors.palette_colors", info.Name, info.Colors.Count));
    }

    /// <summary>Adds to the project palette the colors that are not there yet. Returns how many were added.</summary>
    public int AppendColors(IEnumerable<uint> colors)
    {
        int added = 0;
        _syncing = true; // one save and one dirty mark for the whole group
        try
        {
            foreach (var c in colors)
                if (!Pixel.IsEmpty(c) && !Palette.Contains(c)) { Palette.Add(c); added++; }
        }
        finally { _syncing = false; }
        if (added > 0) OnPaletteEdited();
        return added;
    }

    private void ForgetName()
    {
        _activeUserId = null;
        _activeName = null;
        _baseColors = null;
        OnPaletteTitleChanged();
    }

    /// <summary>Saves the project palette to the library (the dialog asks for the name and description); the project palette gets this name.</summary>
    private void SaveToLibrary()
    {
        var library = Library;
        var dlg = new NameDescriptionViewModel(Loc.T("colors.save_palette_to_library"), _activeName ?? "", "",
            n => library.All().Any(p => p.Name.Equals(n, StringComparison.CurrentCultureIgnoreCase)) ? Loc.T("colors.a_palette_with_this_name") : null,
            Palette.ToList(), Loc.T("colors.save"), Loc.T("colors.the_palette_will_be_available"));
        if (_s.Dialogs.ShowDialog(dlg) != true) return;
        try
        {
            var info = library.Save(dlg.Name, dlg.Description, Palette);
            _activeUserId = info.Id;
            _activeName = info.Name;
            _baseColors = info.Colors.ToList();
            SyncToDoc();
            _s.Dirty = true;
            OnPaletteTitleChanged();
            _s.Status(Loc.T("colors.palette_saved_to_the_library", info.Name));
        }
        catch (Exception ex) { _s.Dialogs.ShowError(ex); }
    }

    private void OnPaletteEdited()
    {
        if (_syncing) return;
        SyncToDoc();
        _s.Dirty = true;
        AutoSaveToLibrary();
        SavePaletteToLibraryCommand.RaiseCanExecuteChanged();
        OnPaletteTitleChanged();
    }

    /// <summary>If the project works with its own library palette, adding and removing colors is saved into it at once.</summary>
    private void AutoSaveToLibrary()
    {
        if (_activeUserId == null || _activeName == null || Palette.Count == 0) return;
        try
        {
            var current = Library.Find(_activeUserId);
            var info = Library.Save(_activeName, current?.Description ?? "", Palette, _activeUserId);
            _baseColors = info.Colors.ToList();
        }
        catch (Exception ex) { _s.Status(Loc.T("colors.could_not_save_the_palette") + ex.Message); }
    }

    /// <summary>A library palette was changed in the library window: if the project uses it, pick up the new colors.</summary>
    public void RefreshFromLibrary(string id)
    {
        if (_activeUserId != id) return;
        var info = Library.Find(id);
        if (info == null) return;
        _syncing = true;
        Palette.Clear();
        foreach (var c in info.Colors) Palette.Add(c);
        _syncing = false;
        _baseColors = info.Colors.ToList();
        SyncToDoc();
        SavePaletteToLibraryCommand.RaiseCanExecuteChanged();
        OnPaletteTitleChanged();
    }

    private void OnPaletteTitleChanged()
    {
        OnPropertyChanged(nameof(PaletteTitle));
        OnPropertyChanged(nameof(ActivePaletteName));
        OnPropertyChanged(nameof(IsPaletteModified));
    }

    /// <summary>The palette is stored in the project. A new document (and an old project without a palette) inherits the current palette.</summary>
    private void SyncToDoc()
    {
        _s.Doc.Palette = Palette.ToList();
        _s.Doc.PaletteName = _activeName;
    }

    private void OnDocumentChanged()
    {
        RefreshStats();
        var doc = _s.Doc;
        if (doc.Palette is { Count: > 0 })
        {
            var colors = doc.Palette.ToList();
            if (!colors.SequenceEqual(Palette))
            {
                _syncing = true;
                Palette.Clear();
                foreach (var c in colors) Palette.Add(c);
                _syncing = false;
            }
            _activeName = doc.PaletteName;
            var fromLibrary = _activeName != null ? Library.Find(_activeName) : null;
            _baseColors = fromLibrary?.Colors.ToList();
            _activeUserId = fromLibrary is { IsSystem: false } ? fromLibrary.Id : null;
            SavePaletteToLibraryCommand.RaiseCanExecuteChanged();
            OnPaletteTitleChanged();
        }
        else SyncToDoc();
    }

    // ---- color statistics ----

    public ObservableCollection<ColorCount> Stats { get; } = new();

    public string StatsHeader
    {
        get => _statsHeader;
        private set => SetProperty(ref _statsHeader, value);
    }

    /// <summary>The selected color is highlighted on the canvas.</summary>
    public ColorCount? SelectedStat
    {
        get => _selectedStat;
        set
        {
            if (!SetProperty(ref _selectedStat, value) || _suppressStats) return;
            _s.Highlight = value?.Color;
        }
    }

    public RelayCommand ReplaceStatColorCommand { get; }
    public RelayCommand StatToPaletteCommand { get; }
    public RelayCommand DeleteStatColorCommand { get; }
    public RelayCommand ClearHighlightCommand { get; }

    /// <summary>Recomputes the color list from the flattened image. Called by the UI with a delay after edits.</summary>
    public void RefreshStats()
    {
        var list = ColorStats.Count(_s.Doc.Flatten());
        _suppressStats = true;
        Stats.Clear();
        foreach (var c in list) Stats.Add(c);

        ColorCount? keep = null;
        if (_s.Highlight is uint h)
        {
            int idx = list.FindIndex(c => c.Color == h);
            if (idx >= 0) keep = list[idx];
        }
        SelectedStat = keep;
        _suppressStats = false;
        if (keep == null && _s.Highlight != null) _s.Highlight = null;

        StatsHeader = Loc.T("colors.used_colors_pixels", list.Count, list.Sum(c => c.Count));
    }

    private void ReplaceStatColor()
    {
        if (_selectedStat is not ColorCount c) { _s.Status(Loc.T("colors.choose_a_color_in_the")); return; }
        _s.FlushPending();
        _s.Highlight = null;
        int n = ColorStats.ReplaceColor(_s.Doc, c.Color, _primary);
        _s.Status(Loc.T("colors.pixels_replaced", n));
    }

    /// <summary>Erases all pixels of the color selected in the list (on all unlocked layers), one undo step.</summary>
    private void DeleteStatColor()
    {
        if (_selectedStat is not ColorCount c) { _s.Status(Loc.T("colors.choose_a_color_in_the")); return; }
        _s.FlushPending();
        _s.Highlight = null;
        int n = ColorStats.ReplaceColor(_s.Doc, c.Color, Pixel.Empty);
        _s.Status(Loc.T("colors.pixels_deleted", n));
    }

    private void StatToPalette()
    {
        if (_selectedStat is ColorCount c && !Palette.Contains(c.Color)) Palette.Add(c.Color);
    }
}
