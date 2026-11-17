// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using PixelEditor.Core;

namespace PixelEditor.ViewModels;

/// <summary>
/// A new layer for each "object" (shape, paste, text): otherwise sprites merge into one layer and cannot be moved separately.
/// The layer is created when the action starts and goes into history together with the first edit as one step (one Ctrl+Z removes both the layer and the drawing).
/// If the action changed nothing or was cancelled, the layer is removed.
/// </summary>
public sealed partial class CanvasViewModel
{
    private bool _newLayerForObjects = true;
    private Layer? _pendingLayer, _layerBefore;
    private IUndoAction? _pendingLayerAction;
    private bool _floatOwnsPendingLayer;

    /// <summary>Shapes, paste and text are drawn on a new layer above the current one.</summary>
    public bool NewLayerForObjects { get => _newLayerForObjects; set => SetProperty(ref _newLayerForObjects, value); }

    /// <summary>The layer to draw an object on: a new one (if enabled and the layer is not locked) or the current one.</summary>
    private Layer BeginObjectLayer(string name)
    {
        DiscardPendingLayer();
        if (!_newLayerForObjects) return _s.ActiveLayer;
        _layerBefore = _s.ActiveLayer;
        var (layer, action) = _s.Doc.AddLayerDeferred($"{name} {_s.Doc.Layers.Count + 1}", _s.ActiveLayerIndex + 1);
        _pendingLayer = layer;
        _pendingLayerAction = action;
        _s.SetActiveLayer(layer);
        return layer;
    }

    /// <summary>Finishes the object: the layer and the edit as one history step; if there is no edit, the layer is removed.</summary>
    private void CompletePendingLayer(IUndoAction? content)
    {
        if (_pendingLayerAction == null)
        {
            if (content != null) _s.Doc.History.Push(content);
            return;
        }
        var layerAction = _pendingLayerAction;
        if (content == null) { DiscardPendingLayer(); return; }
        _pendingLayer = null;
        _pendingLayerAction = null;
        _layerBefore = null;
        _floatOwnsPendingLayer = false;
        _s.Doc.History.Push(new CompositeAction([layerAction, content]));
    }

    private void DiscardPendingLayer()
    {
        if (_pendingLayerAction == null) return;
        var back = _layerBefore;
        _pendingLayerAction.Undo();
        _pendingLayer = null;
        _pendingLayerAction = null;
        _layerBefore = null;
        _floatOwnsPendingLayer = false;
        if (back != null && _s.Doc.IndexOf(back) >= 0) _s.SetActiveLayer(back);
    }

    private void ForgetPendingLayer()
    {
        _pendingLayer = null;
        _pendingLayerAction = null;
        _layerBefore = null;
        _floatOwnsPendingLayer = false;
    }
}
