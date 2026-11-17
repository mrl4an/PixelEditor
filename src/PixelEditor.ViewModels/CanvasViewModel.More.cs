// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using PixelEditor.Core;

namespace PixelEditor.ViewModels;

/// <summary>Polygon selection by nodes, canvas cropping and the highlight of the area under the brush.</summary>
public sealed partial class CanvasViewModel
{
    internal PixelImage? SelectionMaskImage(bool inverted)
    {
        if (SelectionBounds is not PixelRect bounds || bounds.IsEmpty) return null;
        var shape = SelectionShape;
        var image = new PixelImage(_s.Doc.Width, _s.Doc.Height);
        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
                if ((shape?.Contains(x, y) ?? bounds.Contains(x, y)) != inverted)
                    image.Set(x, y, 0xFFFFFFFF);
        return image;
    }

    private void ExportSelectionMask()
    {
        _s.FlushPending();
        if (SelectionMaskImage(inverted: false) is not { } mask) return;
        var path = _s.Dialogs.PickSaveFile("PNG (*.png)|*.png", "mask.png");
        if (path == null) return;
        try
        {
            _s.Images.SavePng(mask, path);
            _s.Status(Loc.T("canvas.selection_mask_saved"));
        }
        catch (Exception ex) { _s.Dialogs.ShowError(ex); }
    }

    private List<(int X, int Y)>? _polyNodes;
    private (int X, int Y) _polyHover;
    private bool _hoverInside;

    public bool IsPolygonTool => _tool == Tool.Polygon;

    // ---- selection by nodes ----

    /// <summary>
    /// Selection by points (a polygon lasso): each click places an outline point. Close it: a click near the first point, the right button or Enter. Backspace removes the last point, Esc cancels.
    /// </summary>
    private void PolygonClick(int x, int y, bool right)
    {
        x = Math.Clamp(x, 0, _s.Doc.Width - 1);
        y = Math.Clamp(y, 0, _s.Doc.Height - 1);
        if (_polyNodes == null)
        {
            if (right) return;
            CommitFloat();
            _selMask = null;
            _selRect = null;
            UpdateSelection();
            _polyNodes = [(x, y)];
            _polyHover = (x, y);
            OnPropertyChanged(nameof(LassoPath));
            _s.Status(Loc.T("canvas.polygon_selection_click_along_the"));
            return;
        }
        var first = _polyNodes[0];
        bool nearFirst = _polyNodes.Count >= 3 && Math.Abs(x - first.X) <= 1 && Math.Abs(y - first.Y) <= 1;
        if (right || nearFirst) { FinishPolygon(); return; }
        if (_polyNodes[^1] != (x, y)) _polyNodes.Add((x, y));
        OnPropertyChanged(nameof(LassoPath));
    }

    private void PolygonHover(int x, int y)
    {
        if (_polyNodes == null) return;
        _polyHover = (x, y);
        OnPropertyChanged(nameof(LassoPath));
    }

    /// <summary>Closes the polygon and turns it into a selection. Fewer than three nodes: cancel.</summary>
    public void FinishPolygon()
    {
        var nodes = _polyNodes;
        _polyNodes = null;
        OnPropertyChanged(nameof(LassoPath));
        if (nodes == null) return;
        var mask = nodes.Count >= 3 ? SelectionMask.Polygon(nodes, _s.Doc.Width, _s.Doc.Height) : null;
        _selMask = mask;
        _selRect = mask?.Bounds;
        UpdateSelection();
        _s.RequestRender();
        _s.Status(mask == null ? Loc.T("canvas.at_least_three_points_are") : Loc.T("canvas.selected_px_by_points", mask.Count, nodes.Count));
    }

    /// <summary>Removes the last placed node (Backspace). Returns true if a polygon is being built.</summary>
    public bool UndoPolygonNode()
    {
        if (_polyNodes == null) return false;
        if (_polyNodes.Count <= 1) _polyNodes = null; else _polyNodes.RemoveAt(_polyNodes.Count - 1);
        OnPropertyChanged(nameof(LassoPath));
        return true;
    }

    public bool IsBuildingPolygon => _polyNodes != null;

    /// <summary>A polyline for the highlight: the nodes, the current cursor point and the closing back to the first node.</summary>
    private IReadOnlyList<(int X, int Y)>? PolygonPath()
    {
        if (_polyNodes == null) return null;
        var path = new List<(int X, int Y)>(_polyNodes) { _polyHover };
        if (_polyNodes.Count >= 2) path.Add(_polyNodes[0]);
        return path;
    }

    private void CancelPolygon()
    {
        if (_polyNodes == null) return;
        _polyNodes = null;
        OnPropertyChanged(nameof(LassoPath));
    }

    // ---- canvas cropping ----

    public RelayCommand CropToSelectionCommand => _cropSel ??= new RelayCommand(CropToSelection, () => HasSelection);
    public RelayCommand CropToContentCommand => _cropContent ??= new RelayCommand(CropToContent);
    private RelayCommand? _cropSel, _cropContent;

    /// <summary>Crops the canvas to the selection frame (all layers). Undone with Ctrl+Z.</summary>
    private void CropToSelection()
    {
        CommitFloat();
        if (_selRect is not PixelRect r || r.IsEmpty) { _s.Status(Loc.T("canvas.no_selection")); return; }
        try
        {
            _s.Doc.Crop(r);
            _s.Status(Loc.T("canvas.canvas_cropped_to_selection", _s.Doc.Width, _s.Doc.Height));
        }
        catch (Exception ex) { _s.Dialogs.ShowError(ex); }
    }

    /// <summary>Crops the canvas to the drawing bounds: empty edges are removed on all sides.</summary>
    private void CropToContent()
    {
        CommitFloat();
        if (_s.Doc.ContentBounds() is not PixelRect r) { _s.Status(Loc.T("canvas.the_drawing_is_empty_nothing")); return; }
        if (r.Width == _s.Doc.Width && r.Height == _s.Doc.Height) { _s.Status(Loc.T("canvas.there_are_no_empty_edges")); return; }
        _s.Doc.Crop(r);
        _s.Status(Loc.T("canvas.canvas_cropped_to_content", _s.Doc.Width, _s.Doc.Height));
    }

    // ---- highlight of the area under the brush ----

    /// <summary>
    /// The pixels the brush, eraser, adjuster or shape outline under the cursor will touch (symmetry included). Empty if the cursor is outside the canvas
    /// or the tool has no notion of an "area" (fill, picker, selection).
    /// </summary>
    public IReadOnlyList<PixelRect> BrushPreview
    {
        get
        {
            if (!_hoverInside) return [];
            int size;
            bool mirror = false;
            if (_tool is Tool.Pencil or Tool.Eraser) { size = (int)_brushSize; mirror = true; }
            else if (_tool == Tool.Shade) size = (int)_brushSize;
            else if (IsShapeTool) size = (int)_shapeThickness;
            else return [];
            size = Math.Max(1, size);

            int w = _s.Doc.Width, h = _s.Doc.Height, half = (size - 1) / 2;
            int ox = _hoverX - half, oy = _hoverY - half;
            int mxo = w - ox - size, myo = h - oy - size;     // the mirrored brush square (an exact reflection)
            var rects = new List<PixelRect> { new(ox, oy, size, size) };
            bool mx = mirror && _mirrorX, my = mirror && _mirrorY;
            if (mx) rects.Add(new(mxo, oy, size, size));
            if (my) rects.Add(new(ox, myo, size, size));
            if (mx && my) rects.Add(new(mxo, myo, size, size));
            return rects.Select(r => r.ClampTo(w, h)).Where(r => !r.IsEmpty).ToList();
        }
    }
}
