// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using PixelEditor.Core;

namespace PixelEditor.ViewModels;

/// <summary>A font in the tool options: the face and a small rendered sample.</summary>
public sealed class TextFontChoice
{
    public TextFontChoice(PixelFontFace face)
    {
        Face = face;
        int scale = face.CellWidth > 6 ? 1 : 2;
        Sample = PixelText.Render("Abc Абв", 0xFFFFFFFF, new TextStyle(Font: face.Id, Scale: scale)).Image ?? new PixelImage(1, 1);
    }

    public PixelFontFace Face { get; }
    public string Id => Face.Id;
    public string Name => Face.Name;
    /// <summary>White ink on transparent: the view uses it as an opacity mask so the theme decides the color.</summary>
    public PixelImage Sample { get; }
    public int SampleWidth => Sample.Width;
    public int SampleHeight => Sample.Height;
    public override string ToString() => Name;
}

/// <summary>The caret of the text session in canvas pixels (X may be fractional: it sits in the gap between letters).</summary>
public sealed record TextCaretInfo(double X, double Y, double Height);

/// <summary>
/// The Text tool: click puts a caret, typing shows a live preview over the canvas (not in any layer), commit writes the pixels as
/// ONE undo step (with the new layer for objects when enabled). The window forwards characters and editing keys to TextInsert and
/// friends; all rules live here.
/// Undo rule: while a session is active Ctrl+Z undoes typing steps and, when none are left, cancels the session (a re-edited text
/// comes back as it was); it never reaches the drawing history underneath.
/// </summary>
public sealed partial class CanvasViewModel
{
    private sealed record LastText(string Text, TextStyle Style, int X, int Y, uint Color, uint Outline, Layer Layer, PixelImage Image, int ImageX, int ImageY, PixelRect Box);

    private static readonly IReadOnlyList<TextFontChoice> FontChoices = PixelFonts.All.Select(f => new TextFontChoice(f)).ToList();

    private TextFontChoice _textFont = FontChoices[0];
    private double _textScale = 1, _textLetter = 1, _textLine = 1;
    private TextAlign _textAlign = TextAlign.Left;
    private bool _textUpper, _textProportional, _textOutline;

    private TextSession? _text;
    private uint _textInk, _textEdge;
    private LastText? _lastText;      // the last committed text, kept for re-editing while nothing else touched it
    private LastText? _reEditing;     // set while a re-edited text is open: its commit was undone and Cancel redoes it
    private bool _textHistoryGuard;
    private History? _hookedHistory;
    private int _textDragX, _textDragY, _textOrigX, _textOrigY;
    private PixelRect? _textFrame;
    private IReadOnlyList<PixelRect> _textSelection = [];

    // ---- options (remembered in settings.json) ----

    public IReadOnlyList<TextFontChoice> TextFonts => FontChoices;

    public TextFontChoice TextFont
    {
        get => _textFont;
        set { if (value != null && SetProperty(ref _textFont, value)) { OnPropertyChanged(nameof(TextUpperOnlyAvailable)); TextOptionsChanged(); } }
    }

    /// <summary>The "uppercase only" switch matters only for fonts that have lowercase letters.</summary>
    public bool TextUpperOnlyAvailable => _textFont.Face.HasLowercase;

    /// <summary>Scale ×1..×4 (nearest neighbor).</summary>
    public double TextScale { get => _textScale; set { if (SetProperty(ref _textScale, Math.Clamp(Math.Round(value), 1, TextStyle.MaxScale))) TextOptionsChanged(); } }
    public double TextLetterSpacing { get => _textLetter; set { if (SetProperty(ref _textLetter, Math.Clamp(Math.Round(value), 0, 4))) TextOptionsChanged(); } }
    public double TextLineSpacing { get => _textLine; set { if (SetProperty(ref _textLine, Math.Clamp(Math.Round(value), 0, 8))) TextOptionsChanged(); } }
    public TextAlign TextAlignment { get => _textAlign; set { if (SetProperty(ref _textAlign, value)) TextOptionsChanged(); } }
    public bool TextUpperOnly { get => _textUpper; set { if (SetProperty(ref _textUpper, value)) TextOptionsChanged(); } }
    public bool TextProportional { get => _textProportional; set { if (SetProperty(ref _textProportional, value)) TextOptionsChanged(); } }
    /// <summary>A 1 px outline in the secondary color.</summary>
    public bool TextOutline { get => _textOutline; set { if (SetProperty(ref _textOutline, value)) TextOptionsChanged(); } }

    public TextStyle CurrentTextStyle =>
        new(_textFont.Id, (int)_textScale, (int)_textLetter, (int)_textLine, _textAlign, _textUpper, _textProportional, _textOutline);

    private void TextOptionsChanged()
    {
        if (_text != null) _text.Style = CurrentTextStyle;
    }

    // ---- state for the view ----

    /// <summary>True while text is being typed: the window must send the keys to the text and ignore single-key shortcuts.</summary>
    public bool IsTextEditing => _text != null;

    /// <summary>The text session (for tests and the view); null when no text is being typed.</summary>
    public TextSession? TextEdit => _text;

    /// <summary>The preview picture and where its corner lies (includes the outline margin); null without ink.</summary>
    public PixelImage? TextPreviewImage => _s.Preview?.Image;
    public (int X, int Y)? TextPreviewOrigin => _s.Preview is { } p ? (p.X, p.Y) : null;

    /// <summary>The bounding frame of the text box in canvas pixels; null when no text is being typed.</summary>
    public PixelRect? TextFrame => _textFrame;

    public TextCaretInfo? TextCaret
    {
        get
        {
            if (_text == null) return null;
            var (cx, cy, h) = _text.Layout.Caret(_text.Caret);
            return new TextCaretInfo(_text.X + cx, _text.Y + cy, h);
        }
    }

    public IReadOnlyList<PixelRect> TextSelection => _textSelection;

    private void HookText()
    {
        _s.FlushRequested += CommitText;
        _colors.PropertyChanged += (_, e) =>
        {
            if (_text == null) return;
            if (e.PropertyName == nameof(ColorsViewModel.Primary)) { _textInk = _colors.Primary; OnTextChanged(); }
            else if (e.PropertyName == nameof(ColorsViewModel.Secondary)) { _textEdge = _colors.Secondary; OnTextChanged(); }
        };
        HookHistory();
    }

    private void HookHistory()
    {
        if (_hookedHistory != null) _hookedHistory.Changed -= OnHistoryChangedForText;
        _hookedHistory = _s.Doc.History;
        _hookedHistory.Changed += OnHistoryChangedForText;
    }

    // anything but our own commit or redo invalidates re-editing: the layer may have changed
    private void OnHistoryChangedForText()
    {
        if (!_textHistoryGuard) _lastText = null;
    }

    /// <summary>The document was replaced or resized: the session and the re-edit memory lose their meaning.</summary>
    private void ResetText()
    {
        HookHistory();
        _text = null;
        _reEditing = null;
        _lastText = null;
        _textFrame = null;
        _textSelection = [];
        _s.Preview = null;
        OnPropertyChanged(nameof(IsTextEditing));
        OnPropertyChanged(nameof(TextFrame));
        OnPropertyChanged(nameof(TextCaret));
        OnPropertyChanged(nameof(TextSelection));
    }

    // ---- pointer ----

    /// <summary>A click with the Text tool: drags the preview, or commits the current text and starts a new one (or re-opens the last one).</summary>
    private bool BeginTextOrMove(int x, int y)
    {
        if (_text != null)
        {
            if (TextFrame is { } f && f.Contains(x, y))
            {
                _drag = DragMode.MoveText;
                _textDragX = x;
                _textDragY = y;
                _textOrigX = _text.X;
                _textOrigY = _text.Y;
                return true;
            }
            CommitText();
        }
        if (_float != null && _float.Bounds.Contains(x, y))
        {
            _drag = DragMode.MoveFloat;
            _dragX = x;
            _dragY = y;
            _floatOrigX = _float.X;
            _floatOrigY = _float.Y;
            return true;
        }
        if (!TryReEditText(x, y)) BeginTextAt(x, y);
        return false;
    }

    private void TextDragTo(int x, int y)
    {
        if (_text == null) return;
        int w = Math.Max(1, _s.Doc.Width), h = Math.Max(1, _s.Doc.Height);
        _text.MoveTo(Math.Clamp(_textOrigX + x - _textDragX, -w, w - 1), Math.Clamp(_textOrigY + y - _textDragY, -h, h - 1));
    }

    /// <summary>Starts a new text at a pixel: the caret is there and typing shows a live preview. False if the layer cannot take text.</summary>
    public bool BeginTextAt(int x, int y)
    {
        var layer = _s.ActiveLayer;
        if (layer.Locked) { _s.Status(Loc.T("canvas.the_layer_is_locked")); return false; }
        if (!layer.Visible) { _s.Status(Loc.T("canvas.the_layer_is_hidden_turn")); return false; }
        CommitText();
        CommitFloat();
        _selRect = null;
        _selMask = null;
        UpdateSelection();
        StartSession(new TextSession(CurrentTextStyle, Math.Clamp(x, 0, Math.Max(0, _s.Doc.Width - 1)), Math.Clamp(y, 0, Math.Max(0, _s.Doc.Height - 1))), _colors.Primary, _colors.Secondary);
        return true;
    }

    private void StartSession(TextSession session, uint ink, uint edge)
    {
        _text = session;
        _textInk = ink;
        _textEdge = edge;
        session.Changed += OnTextChanged;
        OnPropertyChanged(nameof(IsTextEditing));
        OnTextChanged();
        _s.NotifyStateChanged();
    }

    /// <summary>
    /// Re-opens the last committed text when the click lands on it and nothing else has changed that layer: the commit is undone
    /// (so the new commit is again one combined step) and the session restarts with the old string, style, colors and place.
    /// </summary>
    private bool TryReEditText(int x, int y)
    {
        var l = _lastText;
        if (l == null || !l.Box.Contains(x, y)) return false;
        var layer = _s.ActiveLayer;
        if (!ReferenceEquals(layer, l.Layer) || _s.Doc.IndexOf(layer) < 0 || layer.Locked || !layer.Visible || !_s.Doc.History.CanUndo) return false;
        for (int py = 0; py < l.Image.Height; py++)
            for (int px = 0; px < l.Image.Width; px++)
            {
                uint p = l.Image.Pixels[py * l.Image.Width + px];
                int lx = l.ImageX + px, ly = l.ImageY + py;
                if (!Pixel.IsEmpty(p) && layer.InBounds(lx, ly) && layer.Get(lx, ly) != p) return false;   // the pixels were changed by something else
            }
        CommitFloat();
        _selRect = null;
        _selMask = null;
        _textHistoryGuard = true;
        try { _s.Doc.History.Undo(); }
        finally { _textHistoryGuard = false; }
        _lastText = null;
        _reEditing = l;
        var session = new TextSession(l.Style, l.X, l.Y);
        session.Load(l.Text);
        UpdateSelection();
        StartSession(session, l.Color, l.Outline);
        return true;
    }

    // ---- editing keys (the window translates keys and characters into these calls) ----

    public void TextInsert(string text) => _text?.Insert(text);
    public void TextBackspace() => _text?.Backspace();
    public void TextDelete() => _text?.Delete();
    public void TextMoveCaret(TextMove move, bool extend = false) => _text?.Move(move, extend);
    public void TextSelectAll() => _text?.SelectAll();

    /// <summary>Moves the preview by whole pixels (Alt+arrows while typing).</summary>
    public bool TextNudge(int dx, int dy)
    {
        if (_text == null) return false;
        _text.MoveTo(_text.X + dx, _text.Y + dy);
        return true;
    }

    /// <summary>Pastes the clipboard text at the caret (through <see cref="IClipboardService"/>).</summary>
    public void TextPaste()
    {
        if (_text == null) return;
        var t = _s.Clipboard.GetText();
        if (string.IsNullOrEmpty(t)) { _s.Status(Loc.T("canvas.text_clipboard_empty")); return; }
        _text.Insert(t);
    }

    /// <summary>Ctrl+C / Ctrl+X on the selected text.</summary>
    public void TextCopy(bool cut)
    {
        if (_text == null || !_text.HasSelection) return;
        var s = cut ? _text.Cut() : _text.SelectedText;
        _s.Clipboard.TrySetText(s);
    }

    /// <summary>Ctrl+Z inside a session: undo typing, or cancel the session when there is nothing left. False if there is no session.</summary>
    public bool TextUndo()
    {
        if (_text == null) return false;
        if (!_text.Undo()) CancelText();
        return true;
    }

    public bool TextRedo()
    {
        if (_text == null) return false;
        _text.Redo();
        return true;
    }

    public void TextCommit() => CommitText();
    public void TextCancel() => CancelText();

    // ---- preview ----

    private void OnTextChanged()
    {
        var s = _text;
        if (s == null) return;
        var r = PixelText.Render(s.Text, _textInk, s.Style, _textEdge);
        var (bw, bh) = r.Layout.ScaledSize;
        _textFrame = new PixelRect(s.X, s.Y, bw, bh);
        _textSelection = s.HasSelection
            ? r.Layout.SelectionRects(s.SelectionStart, s.SelectionEnd).Select(q => new PixelRect(s.X + q.X, s.Y + q.Y, q.Width, q.Height)).ToList()
            : [];
        _s.Preview = r.Image != null ? (r.Image, s.X + r.OffsetX, s.Y + r.OffsetY) : null;
        _s.RequestRender();
        OnPropertyChanged(nameof(TextFrame));
        OnPropertyChanged(nameof(TextCaret));
        OnPropertyChanged(nameof(TextSelection));
        _s.Status(r.Layout.Missing.Count > 0
            ? Loc.T("canvas.text_missing", string.Join(" ", r.Layout.Missing.Select(c => c == '�' ? "?" : c.ToString())), s.Style.Face.Name)
            : Loc.T("canvas.text_editing", bw, bh));
    }

    private void EndSession()
    {
        _text = null;
        _reEditing = null;
        _textFrame = null;
        _textSelection = [];
        _s.Preview = null;
        _s.RequestRender();
        OnPropertyChanged(nameof(IsTextEditing));
        OnPropertyChanged(nameof(TextFrame));
        OnPropertyChanged(nameof(TextCaret));
        OnPropertyChanged(nameof(TextSelection));
    }

    // ---- commit and cancel ----

    /// <summary>Writes the text into the layer as one undo step (together with the new layer when "new layer for objects" is on). No text, no layer.</summary>
    private void CommitText()
    {
        var s = _text;
        if (s == null) return;
        var reEdit = _reEditing;
        EndSession();
        var render = PixelText.Render(s.Text, _textInk, s.Style, _textEdge);
        if (render.Image == null)
        {
            _s.Status(reEdit != null ? Loc.T("canvas.text_removed") : Loc.T("canvas.text_empty"));
            _s.NotifyStateChanged();
            return;
        }

        var img = render.Image;
        int ix = s.X + render.OffsetX, iy = s.Y + render.OffsetY;
        var layer = BeginObjectLayer(Loc.T("main.text"));
        var batch = new PixelBatch(layer);
        for (int y = 0; y < img.Height; y++)
            for (int x = 0; x < img.Width; x++)
            {
                uint p = img.Pixels[y * img.Width + x];
                if (!Pixel.IsEmpty(p)) batch.Set(ix + x, iy + y, p);
            }
        var action = batch.ToAction();
        _textHistoryGuard = true;
        try
        {
            if (_pendingLayerAction != null) CompletePendingLayer(action);
            else if (action != null) _s.Doc.History.Push(action);
        }
        finally { _textHistoryGuard = false; }
        if (action == null)
        {
            _s.Status(Loc.T("canvas.text_outside"));
            _s.NotifyStateChanged();
            return;
        }

        _colors.NoteUsed(_textInk);
        var (bw, bh) = render.Layout.ScaledSize;
        _lastText = new LastText(s.Text, s.Style, s.X, s.Y, _textInk, _textEdge, layer, img, ix, iy, new PixelRect(s.X, s.Y, bw, bh));

        // the ink stays selected: the arrows move it, a click elsewhere fixes it (as the floating text used to behave)
        var rect = new PixelRect(ix, iy, img.Width, img.Height);
        if (rect.ClampTo(_s.Doc.Width, _s.Doc.Height) == rect)
        {
            var bits = new bool[img.Pixels.Length];
            for (int i = 0; i < bits.Length; i++) bits[i] = !Pixel.IsEmpty(img.Pixels[i]);
            _selMask = new SelectionMask(rect, bits);
            _selRect = rect;
        }
        UpdateSelection();
        _s.RequestRender();
        _s.Status(Loc.T("canvas.text_placed", bw, bh));
    }

    private void CancelText()
    {
        var s = _text;
        if (s == null) return;
        var reEdit = _reEditing;
        EndSession();
        if (reEdit != null)
        {
            // the original commit was undone when the session opened: bring it back
            _textHistoryGuard = true;
            try { _s.Doc.History.Redo(); }
            finally { _textHistoryGuard = false; }
            if (_s.Doc.IndexOf(reEdit.Layer) >= 0) _s.SetActiveLayer(reEdit.Layer);
            _lastText = reEdit;
        }
        _s.Status(Loc.T("canvas.text_cancelled"));
        _s.NotifyStateChanged();
    }
}
