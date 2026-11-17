// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using PixelEditor.Core;

namespace PixelEditor.ViewModels;

public enum TextMove { Left, Right, Up, Down, Home, End, TextStart, TextEnd }

/// <summary>
/// The text being typed on the canvas: the string, the caret, the selection, the position and the style.
/// Knows nothing about the document or the window; the canvas model draws its preview and commits it.
/// It has its own small undo stack (typing steps), which is what Ctrl+Z does while the session is active.
/// </summary>
public sealed class TextSession
{
    public const int MaxLength = 2000;

    private readonly record struct Snapshot(string Text, int Caret, int Anchor);

    private readonly List<Snapshot> _undo = new(), _redo = new();
    private TextStyle _style;
    private TextLayout? _layout;
    private int _wantColumn = -1;
    private bool _typingRun;

    public TextSession(TextStyle style, int x, int y)
    {
        _style = style;
        X = x;
        Y = y;
    }

    /// <summary>Something changed (text, caret, selection, position or style): the preview must be redrawn.</summary>
    public event Action? Changed;

    public string Text { get; private set; } = "";
    public int Caret { get; private set; }
    /// <summary>The other end of the selection (equal to the caret when nothing is selected).</summary>
    public int Anchor { get; private set; }
    public int X { get; private set; }
    public int Y { get; private set; }

    public TextStyle Style
    {
        get => _style;
        set { if (_style == value) return; _style = value; _layout = null; Changed?.Invoke(); }
    }

    public TextLayout Layout => _layout ??= new TextLayout(Text, _style);

    public bool HasSelection => Anchor != Caret;
    public int SelectionStart => Math.Min(Anchor, Caret);
    public int SelectionEnd => Math.Max(Anchor, Caret);
    public string SelectedText => Text.Substring(SelectionStart, SelectionEnd - SelectionStart);
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>True if there is anything visible to commit.</summary>
    public bool HasInk => Text.Any(c => !char.IsWhiteSpace(c));

    /// <summary>Starts the session from an existing text (re-editing): the caret goes to the end, there is no undo history.</summary>
    public void Load(string text)
    {
        Text = TextLayout.Normalize(text);
        Caret = Anchor = Text.Length;
        _layout = null;
        _undo.Clear();
        _redo.Clear();
        Changed?.Invoke();
    }

    // ---------------------------------------------------------------- editing

    /// <summary>Types or pastes text (a selection is replaced). Control characters are dropped, Enter is "\n".</summary>
    public void Insert(string? s)
    {
        s = TextLayout.Normalize(s);
        if (s.Length == 0) return;
        bool single = s.Length == 1 && s != "\n";
        bool continuing = single && _typingRun && !HasSelection;
        if (!continuing) PushUndo();
        _typingRun = single;
        if (HasSelection) RemoveSelection();
        int room = MaxLength - Text.Length;
        if (room <= 0) return;
        if (s.Length > room) s = s[..room];
        Text = Text.Insert(Caret, s);
        Caret = Anchor = Caret + s.Length;
        Edited();
    }

    public void Backspace()
    {
        if (!HasSelection && Caret == 0) return;
        PushUndo();
        _typingRun = false;
        if (HasSelection) RemoveSelection();
        else { Text = Text.Remove(Caret - 1, 1); Caret = Anchor = Caret - 1; }
        Edited();
    }

    public void Delete()
    {
        if (!HasSelection && Caret >= Text.Length) return;
        PushUndo();
        _typingRun = false;
        if (HasSelection) RemoveSelection();
        else Text = Text.Remove(Caret, 1);
        Edited();
    }

    /// <summary>Removes the selected text and returns it (for cut); empty if nothing is selected.</summary>
    public string Cut()
    {
        if (!HasSelection) return "";
        var s = SelectedText;
        PushUndo();
        _typingRun = false;
        RemoveSelection();
        Edited();
        return s;
    }

    private void RemoveSelection()
    {
        int a = SelectionStart, b = SelectionEnd;
        Text = Text.Remove(a, b - a);
        Caret = Anchor = a;
    }

    private void Edited()
    {
        _layout = null;
        _wantColumn = -1;
        Changed?.Invoke();
    }

    // ---------------------------------------------------------------- caret

    public void SelectAll()
    {
        Anchor = 0;
        Caret = Text.Length;
        _typingRun = false;
        Changed?.Invoke();
    }

    /// <summary>Moves the caret; with <paramref name="extend"/> the selection grows, otherwise it collapses.</summary>
    public void Move(TextMove move, bool extend)
    {
        _typingRun = false;
        var layout = Layout;
        var (line, col) = layout.LineColumn(Caret);
        int target = Caret;
        switch (move)
        {
            case TextMove.Left:
                target = !extend && HasSelection ? SelectionStart : Math.Max(0, Caret - 1);
                _wantColumn = -1;
                break;
            case TextMove.Right:
                target = !extend && HasSelection ? SelectionEnd : Math.Min(Text.Length, Caret + 1);
                _wantColumn = -1;
                break;
            case TextMove.Home: target = layout.IndexOf(line, 0); _wantColumn = -1; break;
            case TextMove.End: target = layout.IndexOf(line, int.MaxValue); _wantColumn = -1; break;
            case TextMove.TextStart: target = 0; _wantColumn = -1; break;
            case TextMove.TextEnd: target = Text.Length; _wantColumn = -1; break;
            case TextMove.Up:
            case TextMove.Down:
            {
                if (_wantColumn < 0) _wantColumn = col;
                int nl = line + (move == TextMove.Up ? -1 : 1);
                target = nl < 0 ? 0 : nl >= layout.Lines.Count ? Text.Length : layout.IndexOf(nl, _wantColumn);
                break;
            }
        }
        Caret = target;
        if (!extend) Anchor = target;
        Changed?.Invoke();
    }

    /// <summary>Moves the whole text on the canvas.</summary>
    public void MoveTo(int x, int y)
    {
        if (x == X && y == Y) return;
        X = x;
        Y = y;
        Changed?.Invoke();
    }

    // ---------------------------------------------------------------- undo of typing

    private void PushUndo()
    {
        _undo.Add(new Snapshot(Text, Caret, Anchor));
        if (_undo.Count > 200) _undo.RemoveAt(0);
        _redo.Clear();
    }

    /// <summary>Undoes the last typing step. False if there is nothing to undo.</summary>
    public bool Undo()
    {
        if (_undo.Count == 0) return false;
        _redo.Add(new Snapshot(Text, Caret, Anchor));
        Restore(_undo[^1]);
        _undo.RemoveAt(_undo.Count - 1);
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0) return false;
        _undo.Add(new Snapshot(Text, Caret, Anchor));
        Restore(_redo[^1]);
        _redo.RemoveAt(_redo.Count - 1);
        return true;
    }

    private void Restore(Snapshot s)
    {
        Text = s.Text;
        Caret = s.Caret;
        Anchor = s.Anchor;
        _typingRun = false;
        Edited();
    }
}
