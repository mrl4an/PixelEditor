// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.IO;
using PixelEditor.Core;
using PixelEditor.Core.Ops;

namespace PixelEditor.ViewModels;

/// <summary>Sprite sheets: the cell grid, import and export of sheets and slicing, animation.</summary>
public sealed class SheetViewModel : ObservableObject
{
    private readonly EditorSession _s;

    public SheetViewModel(EditorSession session)
    {
        _s = session;
        SheetGridCommand = new RelayCommand(SetGrid);
        AnimationCommand = new RelayCommand(ShowAnimation);
        ImportSheetCommand = new RelayCommand(ImportSheet);
        ExportLayersSheetCommand = new RelayCommand(ExportLayersSheet);
        ExportSlicesCommand = new RelayCommand(ExportSlices);
    }

    public RelayCommand SheetGridCommand { get; }
    public RelayCommand AnimationCommand { get; }
    public RelayCommand ImportSheetCommand { get; }
    public RelayCommand ExportLayersSheetCommand { get; }
    public RelayCommand ExportSlicesCommand { get; }

    private void SetGrid()
    {
        var doc = _s.Doc;
        var current = doc.HasSheetGrid ? $"{doc.SheetCellWidth}x{doc.SheetCellHeight}" : "32x32";
        var text = _s.Dialogs.Prompt(Loc.T("sheet.sheet_grid"),
            Loc.T("sheet.cell_size_width_height_for"), current);
        if (text == null) return;
        if (!SizeParser.TryParse(text, out var w, out var h)) { _s.Status(Loc.T("sheet.could_not_understand_the_size")); return; }

        if (w == 0 || h == 0) doc.SheetCellWidth = doc.SheetCellHeight = 0;
        else if (w > doc.Width || h > doc.Height) { _s.Status(Loc.T("sheet.the_cell_is_larger_than")); return; }
        else { doc.SheetCellWidth = w; doc.SheetCellHeight = h; }

        _s.Dirty = true;
        _s.RaiseOverlaysChanged();
        _s.Status(doc.HasSheetGrid
            ? Loc.T("sheet.sheet_grid_cells", w, h, Sheet.Columns(doc.Width, w), Sheet.Rows(doc.Height, h))
            : Loc.T("sheet.the_sheet_grid_is_off"));
    }

    private void ShowAnimation()
    {
        _s.FlushPending();
        var doc = _s.Doc;
        var frames = Animation.GetFrames(doc);
        if (frames.Count == 0) { _s.Status(Loc.T("sheet.no_frames")); return; }
        if (doc.Rsi is { } b && doc.Width == b.SheetWidth && doc.Height == b.SheetHeight)
        {
            ShowSheetAnimation(doc, b);
            return;
        }
        bool grid = doc.HasSheetGrid && (doc.Width > doc.SheetCellWidth || doc.Height > doc.SheetCellHeight);
        _s.Dialogs.Show(new AnimationViewModel(_s.Dialogs, _s.Services.Timers, frames,
            grid ? Loc.T("sheet.frames_grid_cells", doc.SheetCellWidth, doc.SheetCellHeight) : Loc.T("sheet.frames_layers_bottom_to_top")));
    }

    /// <summary>An RSI sheet or animation sheet animation: one row per animation, frames have their own delays (can be edited and saved into the sheet).</summary>
    private void ShowSheetAnimation(Document doc, RsiBinding b)
    {
        var cells = Sheet.Slice(doc.Flatten(), b.FrameWidth, b.FrameHeight).ToList();
        var rows = new List<AnimationRow>();
        var labels = b.RowLabels;
        for (int d = 0; d < b.Directions; d++)
            rows.Add(new AnimationRow(labels[d], cells.Where(c => c.Row == d).OrderBy(c => c.Col).Select(c => c.Image).ToList()));
        _s.Dialogs.Show(new AnimationViewModel(_s.Dialogs, _s.Services.Timers, rows, b.Delays,
            Loc.T("sheet.sheet_rows_fr", b.State, b.Directions, b.Frames),
            delays =>
            {
                if (_s.Doc.Rsi is not { } cur) return;
                _s.Doc.Rsi = cur with { Delays = delays };
                _s.Dirty = true;
                _s.Status(Loc.T("sheet.frame_delays_saved_to_the"));
            }));
    }

    private void ImportSheet()
    {
        var path = _s.Dialogs.PickOpenFile(Loc.T("sheet.images_png_jpg_jpeg_bmp"));
        if (path == null) return;
        try
        {
            var img = Transform.BinarizeAlpha(_s.Images.Load(path), 128);
            var text = _s.Dialogs.Prompt(Loc.T("sheet.import_sheet"), Loc.T("sheet.size_of_one_cell_sheet", img.Width, img.Height), "32x32");
            if (text == null) return;
            if (!SizeParser.TryParse(text, out var cw, out var ch) || cw < 1 || ch < 1)
            {
                _s.Status(Loc.T("sheet.could_not_understand_the_size"));
                return;
            }

            var cells = Sheet.Slice(img, cw, ch).Select(c => c.Image).ToList();
            if (cells.Count == 0) { _s.Status(Loc.T("sheet.the_sheet_has_no_whole")); return; }
            while (cells.Count > 1 && cells[^1].OpaqueCount() == 0) cells.RemoveAt(cells.Count - 1);
            if (!_s.ConfirmDiscard()) return;

            var layers = cells.Select((c, i) => new Layer(cw, ch, Loc.T("sheet.frame", i + 1), c.Pixels)).ToList();
            _s.SetDocument(Document.FromLayers(cw, ch, layers), null);
            _s.Status(Loc.T("sheet.frames_imported_each_cell_became", layers.Count));
        }
        catch (Exception ex) { _s.Dialogs.ShowError(ex); }
    }

    private void ExportLayersSheet()
    {
        _s.FlushPending();
        var count = _s.Doc.Layers.Count;
        var text = _s.Dialogs.Prompt(Loc.T("sheet.export_layers_as_a_sheet"), Loc.T("sheet.number_of_columns_layers", count), count.ToString());
        if (text == null) return;
        if (!int.TryParse(text, out var cols) || cols < 1) { _s.Status(Loc.T("sheet.a_number_of_columns_is")); return; }
        var path = _s.Dialogs.PickSaveFile("PNG (*.png)|*.png", "sheet.png");
        if (path == null) return;
        try
        {
            var sheet = Sheet.Compose(_s.Doc.Layers.Select(l => l.ToImage()).ToList(), cols);
            _s.Images.SavePng(sheet, path);
            _s.Status(Loc.T("sheet.sheet_saved", sheet.Width, sheet.Height));
        }
        catch (Exception ex) { _s.Dialogs.ShowError(ex); }
    }

    private void ExportSlices()
    {
        _s.FlushPending();
        var doc = _s.Doc;
        if (!doc.HasSheetGrid) { _s.Status(Loc.T("sheet.first_set_the_sheet_grid")); return; }
        var folder = _s.Dialogs.PickFolder(Loc.T("sheet.folder_for_the_cells"));
        if (folder == null) return;
        try
        {
            int n = 0;
            foreach (var (col, row, cell) in Sheet.Slice(doc.Flatten(), doc.SheetCellWidth, doc.SheetCellHeight, skipEmpty: true))
            {
                _s.Images.SavePng(cell, Path.Combine(folder, $"cell_{row:00}_{col:00}.png"));
                n++;
            }
            _s.Status(Loc.T("sheet.cells_saved_empty_ones_skipped", n));
        }
        catch (Exception ex) { _s.Dialogs.ShowError(ex); }
    }
}
