// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using PixelEditor.Core;

namespace PixelEditor.ViewModels;

/// <summary>
/// The shared state of one open document: the document itself, the active layer, "dirtiness", the status and events.
/// Child view models (layers, colors, canvas, sheet, automation) work with it and with each other only through it.
/// </summary>
public sealed class EditorSession : ObservableObject
{
    private Document _doc = null!;
    private Layer _active = null!;
    private int _layerHint;
    private bool _dirty;
    private string? _projectPath;
    private string _status = Loc.T("batch.done");
    private uint? _highlight;

    public EditorSession(AppServices services, AppSettings settings)
    {
        Services = services;
        Settings = settings;
        SetDocument(new Document(Math.Clamp(settings.DefaultWidth, 1, 4096), Math.Clamp(settings.DefaultHeight, 1, 4096)), null);
    }

    public AppServices Services { get; }
    public IDialogService Dialogs => Services.Dialogs;
    public IClipboardService Clipboard => Services.Clipboard;
    public IImageStore Images => Services.Images;
    public AppSettings Settings { get; }

    public Document Doc => _doc;
    public Layer ActiveLayer => _active;
    public int ActiveLayerIndex => _doc.IndexOf(_active);

    public bool Dirty
    {
        get => _dirty;
        set { if (SetProperty(ref _dirty, value)) StateChanged?.Invoke(); }
    }

    public string? ProjectPath
    {
        get => _projectPath;
        set { if (SetProperty(ref _projectPath, value)) StateChanged?.Invoke(); }
    }

    public string StatusText
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    /// <summary>The color whose pixels are highlighted on the canvas (the others are darkened).</summary>
    public uint? Highlight
    {
        get => _highlight;
        set { if (SetProperty(ref _highlight, value)) RequestRender(); }
    }

    /// <summary>Asks whether unsaved changes may be abandoned. Supplied by the main model.</summary>
    public Func<bool> ConfirmDiscard { get; set; } = () => true;

    // ---- events ----
    public event Action? DocumentChanged;     // another document was opened
    public event Action? CanvasResized;       // the canvas size of the current document changed
    public event Action? LayersChanged;       // the layer list changed
    public event Action? FrameInvalidated;    // the canvas needs a redraw
    public event Action? ContentChanged;      // the content changed (a reason to recompute the color statistics)
    public event Action? StateChanged;        // something the commands and the title depend on changed
    public event Action? OverlaysChanged;     // the sheet grid changed
    public event Action? FlushRequested;      // commit everything "floating" before a structural operation

    public void SetDocument(Document doc, string? path)
    {
        if (_doc != null)
        {
            _doc.History.Changed -= OnHistoryChanged;
            _doc.LayersChanged -= OnLayersChanged;
            _doc.CanvasChanged -= OnCanvasChanged;
        }
        _doc = doc;
        _doc.History.Changed += OnHistoryChanged;
        _doc.LayersChanged += OnLayersChanged;
        _doc.CanvasChanged += OnCanvasChanged;

        _active = doc.Layers[^1];
        _layerHint = doc.Layers.Count - 1;
        _highlight = null;
        _projectPath = path;
        _dirty = false;

        OnPropertyChanged(nameof(Doc));
        OnPropertyChanged(nameof(ActiveLayer));
        OnPropertyChanged(nameof(ProjectPath));
        OnPropertyChanged(nameof(Dirty));
        DocumentChanged?.Invoke();
        LayersChanged?.Invoke();
        FrameInvalidated?.Invoke();
        ContentChanged?.Invoke();
        OverlaysChanged?.Invoke();
        StateChanged?.Invoke();
        Status(Loc.T("session.document", doc.Width, doc.Height));
    }

    public void SetActiveLayer(Layer layer)
    {
        _active = layer;
        _layerHint = _doc.IndexOf(layer);
        OnPropertyChanged(nameof(ActiveLayer));
        StateChanged?.Invoke();
    }

    public void Status(string text) => StatusText = text;

    public void FlushPending() => FlushRequested?.Invoke();
    public void RequestRender() => FrameInvalidated?.Invoke();
    public void NotifyStateChanged() => StateChanged?.Invoke();
    public void RaiseOverlaysChanged() => OverlaysChanged?.Invoke();

    /// <summary>The content changed not through history (for example a layer visibility was toggled).</summary>
    public void NotifyContentChanged(bool markDirty)
    {
        if (markDirty) Dirty = true;
        FrameInvalidated?.Invoke();
        ContentChanged?.Invoke();
    }

    private void OnHistoryChanged()
    {
        _dirty = true;
        OnPropertyChanged(nameof(Dirty));
        FrameInvalidated?.Invoke();
        ContentChanged?.Invoke();
        StateChanged?.Invoke();
    }

    private void OnLayersChanged()
    {
        if (!_doc.Layers.Contains(_active))
        {
            _active = _doc.Layers[Math.Min(_layerHint, _doc.Layers.Count - 1)];
            OnPropertyChanged(nameof(ActiveLayer));
        }
        LayersChanged?.Invoke();
        StateChanged?.Invoke();
    }

    private void OnCanvasChanged()
    {
        OnPropertyChanged(nameof(Doc));
        CanvasResized?.Invoke();
    }

    /// <summary>
    /// A picture drawn over the composition but not part of any layer (the live preview of the text being typed).
    /// The owner sets it and calls <see cref="RequestRender"/>; it is never exported or saved.
    /// </summary>
    public (PixelImage Image, int X, int Y)? Preview { get; set; }

    /// <summary>The composition of all layers; when a color is highlighted the other pixels are darkened.</summary>
    public void ComposeFrame(uint[] dest)
    {
        _doc.Flatten(dest);
        if (_highlight is uint h)
            for (int i = 0; i < dest.Length; i++)
            {
                uint p = dest[i];
                if (p != 0 && p != h) dest[i] = Dim(p);
            }
        if (Preview is { } pv && dest.Length == _doc.Width * _doc.Height)
            for (int y = 0; y < pv.Image.Height; y++)
            {
                int dy = pv.Y + y;
                if (dy < 0 || dy >= _doc.Height) continue;
                for (int x = 0; x < pv.Image.Width; x++)
                {
                    int dx = pv.X + x;
                    uint p = pv.Image.Pixels[y * pv.Image.Width + x];
                    if (dx >= 0 && dx < _doc.Width && !Pixel.IsEmpty(p)) dest[dy * _doc.Width + dx] = p;
                }
            }
    }

    private static uint Dim(uint p)
    {
        static byte Mix(byte c) => (byte)(c * 0.3 + 128 * 0.7);
        return Pixel.Rgb(Mix(Pixel.R(p)), Mix(Pixel.G(p)), Mix(Pixel.B(p)));
    }

    // ---- shared operations on the active layer ----

    /// <summary>Commits the "floating" content and checks that the active layer can be changed.</summary>
    public bool CanEditActiveLayer()
    {
        FlushPending();
        if (_active.Locked) { Status(Loc.T("canvas.the_layer_is_locked")); return false; }
        return true;
    }

    public static void Stamp(PixelBatch batch, PixelImage img)
    {
        for (int i = 0; i < img.Pixels.Length; i++)
            batch.Set(i % img.Width, i / img.Width, img.Pixels[i]);
    }

    /// <summary>A result of the same size is written into the layer (with undo), otherwise opening it as a new document is offered.</summary>
    public bool ApplyResult(PixelImage result, string what, bool sizeChangeConfirmed = false)
    {
        if (result.Width == _active.Width && result.Height == _active.Height)
        {
            var batch = new PixelBatch(_active);
            Stamp(batch, result);
            Status(batch.HasChanges ? Loc.T("session.applied_pixels_changed", what, batch.Count) : Loc.T("session.no_changes", what));
            bool changed = batch.HasChanges;
            _doc.History.Commit(batch);
            return changed;
        }

        if ((sizeChangeConfirmed || Dialogs.Confirm(Loc.T("session.the_result_is_but_the", what, result.Width, result.Height, _active.Width, _active.Height), "Pixel Editor"))
            && ConfirmDiscard())
            SetDocument(Document.FromImage(result), null);
        return true;
    }

    /// <summary>The result size differs: the document canvas gets the result size (with undo, other layers are cropped or padded with emptiness), the result is written into the active layer.</summary>
    public void ApplyResultResizingCanvas(PixelImage result, string what)
    {
        FlushPending();
        if (result.Width != _doc.Width || result.Height != _doc.Height)
            _doc.ResizeCanvas(result.Width, result.Height);
        var batch = new PixelBatch(_active);           // layers are replaced by new objects after the canvas changes: _active is updated in OnCanvasChanged
        Stamp(batch, result);
        _doc.History.Commit(batch);
        Status(Loc.T("session.canvas_resized_to", what, result.Width, result.Height));
    }
}
