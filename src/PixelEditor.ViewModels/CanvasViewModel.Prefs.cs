// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
namespace PixelEditor.ViewModels;

/// <summary>Remembering the selected tool and its settings between runs (settings.json).</summary>
public sealed partial class CanvasViewModel
{
    private bool _restoringPrefs;

    private static readonly HashSet<string> PrefNames =
    [
        nameof(Tool), nameof(BrushSize), nameof(ShapeThickness), nameof(FilledShapes), nameof(FillTolerance), nameof(FillWholeLayer),
        nameof(WandTolerance), nameof(WandContiguous), nameof(WandFollow), nameof(EraserTolerance), nameof(EraserContiguous), nameof(EraserFollow), nameof(TextFont), nameof(TextScale),
        nameof(ShowPixelGrid), nameof(NewLayerForObjects), nameof(LastShapeTool), nameof(LastSelectionTool),
    ];

    private void RestorePrefs()
    {
        var st = _s.Settings;
        _restoringPrefs = true;
        try
        {
            BrushSize = st.BrushSize;
            ShapeThickness = st.ShapeThickness;
            FilledShapes = st.FilledShapes;
            FillTolerance = st.FillTolerance;
            FillWholeLayer = st.FillWholeLayer;
            WandTolerance = st.WandTolerance;
            WandContiguous = st.WandContiguous;
            WandFollow = st.WandFollow;
            EraserTolerance = st.EraserTolerance;
            EraserContiguous = st.EraserContiguous;
            EraserFollow = st.EraserFollow;
            TextFont = FontChoices.FirstOrDefault(f => f.Id == st.TextFont) ?? FontChoices[0];
            TextScale = st.TextScale;
            ShowPixelGrid = st.ShowPixelGrid;
            NewLayerForObjects = st.NewLayerForObjects;
            if (Enum.TryParse<Tool>(st.LastShape, out var shape) && shape is Tool.Line or Tool.Rect or Tool.Ellipse) _lastShape = shape;
            if (Enum.TryParse<Tool>(st.LastSelection, out var sel) && sel is Tool.Select or Tool.Wand or Tool.Lasso or Tool.Polygon) _lastSelection = sel;
            // "Move" without a selection is useless, so it is not restored at startup
            if (Enum.TryParse<Tool>(st.ToolName, out var tool) && tool != Tool.Move) Tool = tool;
        }
        finally { _restoringPrefs = false; }
        PropertyChanged += (_, e) =>
        {
            if (!_restoringPrefs && e.PropertyName != null && PrefNames.Contains(e.PropertyName)) SavePrefs();
        };
    }

    private void SavePrefs()
    {
        var st = _s.Settings;
        // "Move" is not remembered as the previous drawing tool: we save what was before it
        if (_tool != Tool.Move) st.ToolName = _tool.ToString();
        st.BrushSize = _brushSize;
        st.ShapeThickness = _shapeThickness;
        st.FilledShapes = _filled;
        st.FillTolerance = _fillTolerance;
        st.FillWholeLayer = _fillWholeLayer;
        st.WandTolerance = _wandTolerance;
        st.WandContiguous = _wandContiguous;
        st.WandFollow = _wandFollow;
        st.EraserTolerance = _eraserTolerance;
        st.EraserContiguous = _eraserContiguous;
        st.EraserFollow = _eraserFollow;
        st.TextFont = _textFont.Id;
        st.TextScale = _textScale;
        st.ShowPixelGrid = _showGrid;
        st.NewLayerForObjects = _newLayerForObjects;
        st.LastShape = _lastShape.ToString();
        st.LastSelection = _lastSelection.ToString();
        st.Save();
    }
}
