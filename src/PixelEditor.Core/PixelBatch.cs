// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
namespace PixelEditor.Core;

/// <summary>
/// Accumulates the edits of one layer for one stroke/operation. Edits are applied at once,
/// but the old values are remembered, so they can be reverted (Revert) for shape previews
/// or turned into one undo action (ToAction).
/// </summary>
public sealed class PixelBatch(Layer layer)
{
    private readonly Dictionary<int, (uint Old, uint New)> _changes = new();

    public Layer Layer => layer;
    public bool HasChanges => _changes.Count > 0;

    /// <summary>How many pixels changed relative to the original state.</summary>
    public int Count => _changes.Count;

    public uint Get(int x, int y) => layer.Get(x, y);

    public void Set(int x, int y, uint color)
    {
        if (!layer.InBounds(x, y)) return;
        int i = y * layer.Width + x;
        uint current = layer.Pixels[i];
        if (current == color) return;

        if (_changes.TryGetValue(i, out var e))
        {
            if (e.Old == color) _changes.Remove(i);
            else _changes[i] = (e.Old, color);
        }
        else
        {
            _changes[i] = (current, color);
        }
        layer.Pixels[i] = color;
    }

    public void Revert()
    {
        foreach (var (i, e) in _changes) layer.Pixels[i] = e.Old;
        _changes.Clear();
    }

    public IUndoAction? ToAction()
    {
        if (_changes.Count == 0) return null;
        var idx = new int[_changes.Count];
        var oldV = new uint[idx.Length];
        var newV = new uint[idx.Length];
        int n = 0;
        foreach (var (i, e) in _changes)
        {
            idx[n] = i;
            oldV[n] = e.Old;
            newV[n] = e.New;
            n++;
        }
        return new PixelChangeAction(layer, idx, oldV, newV);
    }
}
