// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
namespace PixelEditor.Core;

public interface IUndoAction
{
    void Undo();
    void Redo();
}

public sealed class DelegateAction(Action undo, Action redo) : IUndoAction
{
    public void Undo() => undo();
    public void Redo() => redo();
}

public sealed class CompositeAction(IReadOnlyList<IUndoAction> actions) : IUndoAction
{
    public void Undo()
    {
        for (int i = actions.Count - 1; i >= 0; i--) actions[i].Undo();
    }

    public void Redo()
    {
        foreach (var a in actions) a.Redo();
    }
}

/// <summary>Stores only the changed pixels (indices + old/new values), not canvas snapshots.</summary>
public sealed class PixelChangeAction(Layer layer, int[] indices, uint[] oldValues, uint[] newValues) : IUndoAction
{
    public void Undo()
    {
        for (int i = 0; i < indices.Length; i++) layer.Pixels[indices[i]] = oldValues[i];
    }

    public void Redo()
    {
        for (int i = 0; i < indices.Length; i++) layer.Pixels[indices[i]] = newValues[i];
    }
}

public sealed class History
{
    private readonly Stack<IUndoAction> _undo = new();
    private readonly Stack<IUndoAction> _redo = new();

    public event Action? Changed;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Push(IUndoAction action)
    {
        _undo.Push(action);
        _redo.Clear();
        Changed?.Invoke();
    }

    public void Commit(PixelBatch batch)
    {
        var action = batch.ToAction();
        if (action != null) Push(action);
    }

    public void Undo()
    {
        if (!CanUndo) return;
        var a = _undo.Pop();
        a.Undo();
        _redo.Push(a);
        Changed?.Invoke();
    }

    public void Redo()
    {
        if (!CanRedo) return;
        var a = _redo.Pop();
        a.Redo();
        _undo.Push(a);
        Changed?.Invoke();
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        Changed?.Invoke();
    }
}
