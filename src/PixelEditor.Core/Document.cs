// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
namespace PixelEditor.Core;

public sealed class Document
{
    private readonly List<Layer> _layers = new();

    public int Width { get; private set; }
    public int Height { get; private set; }
    public IReadOnlyList<Layer> Layers => _layers;
    public History History { get; } = new();

    /// <summary>Sprite sheet grid cell size; 0 = grid off. Stored in the project.</summary>
    public int SheetCellWidth { get; set; }
    public int SheetCellHeight { get; set; }

    /// <summary>Project palette (stored in .pixproj). null: the project has no palette of its own.</summary>
    /// <summary>Binding to an RSI state (the document is a state sheet); null for an ordinary document.</summary>
    public RsiBinding? Rsi { get; set; }

    /// <summary>The last animation export settings (stored in .pixproj); null until the first export.</summary>
    public AnimationExportProfile? ExportProfile { get; set; }

    public List<uint>? Palette { get; set; }

    /// <summary>Name of the library palette if the project uses one (for the caption).</summary>
    public string? PaletteName { get; set; }
    public bool HasSheetGrid => SheetCellWidth > 0 && SheetCellHeight > 0;

    /// <summary>The layer list changed (add, delete, order), including because of undo/redo.</summary>
    public event Action? LayersChanged;

    /// <summary>The canvas size changed (including because of undo/redo): the display raster must be recreated.</summary>
    public event Action? CanvasChanged;

    /// <summary>
    /// Changes the canvas size without scaling, ax/ay (0..1) set the content anchor. One undo action.
    /// Layers are replaced by new objects, so references to the old layers are invalid after the call.
    /// </summary>
    public void ResizeCanvas(int width, int height, double ax = 0, double ay = 0)
    {
        if (width < 1 || height < 1 || width > 8192 || height > 8192)
            throw new ArgumentOutOfRangeException(nameof(width), CoreText.T("Размер холста от 1 до 8192"));
        if (width == Width && height == Height) return;

        var oldLayers = _layers.ToList();
        int ow = Width, oh = Height;
        var newLayers = oldLayers.Select(l => new Layer(width, height, l.Name,
            Ops.Transform.ResizeCanvas(l.ToImage(), width, height, ax, ay).Pixels)
        { Visible = l.Visible, Locked = l.Locked }).ToList();

        void Apply(List<Layer> layers, int w, int h)
        {
            _layers.Clear();
            _layers.AddRange(layers);
            Width = w;
            Height = h;
            CanvasChanged?.Invoke();
            LayersChanged?.Invoke();
        }

        Apply(newLayers, width, height);
        History.Push(new DelegateAction(() => Apply(oldLayers, ow, oh), () => Apply(newLayers, width, height)));
    }

    /// <summary>
    /// Scales the image (all layers) to width×height with the chosen method (Nearest, Box, Mode); transparency is binary again.
    /// One undo action. Layers are replaced by new objects.
    /// </summary>
    public void ResizeImage(int width, int height, Ops.ResizeMethod method)
    {
        if (width < 1 || height < 1 || width > 8192 || height > 8192)
            throw new ArgumentOutOfRangeException(nameof(width), CoreText.T("Размер от 1 до 8192"));
        if (width == Width && height == Height) return;

        var oldLayers = _layers.ToList();
        int ow = Width, oh = Height;
        var newLayers = oldLayers.Select(l =>
        {
            var scaled = Ops.Resampler.Resize(l.ToImage(), width, height, method);
            return new Layer(width, height, l.Name, Ops.Transform.BinarizeAlpha(scaled, 128).Pixels) { Visible = l.Visible, Locked = l.Locked };
        }).ToList();

        void Apply(List<Layer> layers, int w, int h)
        {
            _layers.Clear();
            _layers.AddRange(layers);
            Width = w;
            Height = h;
            CanvasChanged?.Invoke();
            LayersChanged?.Invoke();
        }

        Apply(newLayers, width, height);
        History.Push(new DelegateAction(() => Apply(oldLayers, ow, oh), () => Apply(newLayers, width, height)));
    }

    /// <summary>Crops the canvas to the rect rectangle (in all layers), one undo action. Layers are replaced by new objects.</summary>
    public void Crop(PixelRect rect)
    {
        rect = rect.ClampTo(Width, Height);
        if (rect.IsEmpty) throw new ArgumentException(CoreText.T("Пустая область обрезки"));
        if (rect.X == 0 && rect.Y == 0 && rect.Width == Width && rect.Height == Height) return;

        var oldLayers = _layers.ToList();
        int ow = Width, oh = Height;
        var newLayers = oldLayers.Select(l => new Layer(rect.Width, rect.Height, l.Name, Selection.Copy(l, rect).Pixels)
        { Visible = l.Visible, Locked = l.Locked }).ToList();

        void Apply(List<Layer> layers, int w, int h)
        {
            _layers.Clear();
            _layers.AddRange(layers);
            Width = w;
            Height = h;
            CanvasChanged?.Invoke();
            LayersChanged?.Invoke();
        }

        Apply(newLayers, rect.Width, rect.Height);
        History.Push(new DelegateAction(() => Apply(oldLayers, ow, oh), () => Apply(newLayers, rect.Width, rect.Height)));
    }

    /// <summary>Bounding box of all opaque pixels over all visible layers; null if the picture is empty.</summary>
    public PixelRect? ContentBounds()
    {
        var flat = Flatten();
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (int y = 0; y < flat.Height; y++)
            for (int x = 0; x < flat.Width; x++)
                if (!Pixel.IsEmpty(flat.Pixels[y * flat.Width + x]))
                {
                    minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                }
        return maxX < 0 ? null : new PixelRect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    public Document(int width, int height, bool addFirstLayer = true)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        Width = width;
        Height = height;
        if (addFirstLayer) _layers.Add(new Layer(width, height, CoreText.T("Слой 1")));
    }

    public static Document FromImage(PixelImage image, string? layerName = null)
    {
        var doc = new Document(image.Width, image.Height, addFirstLayer: false);
        doc._layers.Add(new Layer(image.Width, image.Height, layerName ?? CoreText.T("Слой 1"),(uint[])image.Pixels.Clone()));
        return doc;
    }

    public static Document FromLayers(int width, int height, IEnumerable<Layer> layers)
    {
        var doc = new Document(width, height, addFirstLayer: false);
        doc._layers.AddRange(layers);
        if (doc._layers.Count == 0) doc._layers.Add(new Layer(width, height, CoreText.T("Слой 1")));
        return doc;
    }

    public int IndexOf(Layer layer) => _layers.IndexOf(layer);

    public void RaiseLayersChanged() => LayersChanged?.Invoke();

    // ---------- layers (all operations with undo) ----------

    public void RenameLayer(Layer layer, string name) =>
        ChangeLayerProperty(layer, layer.Name, name, value => layer.Name = value);

    public void SetLayerLocked(Layer layer, bool locked) =>
        ChangeLayerProperty(layer, layer.Locked, locked, value => layer.Locked = value);

    public void SetLayerVisible(Layer layer, bool visible) =>
        ChangeLayerProperty(layer, layer.Visible, visible, value => layer.Visible = value);

    private void ChangeLayerProperty<T>(Layer layer, T previous, T next, Action<T> set)
    {
        if (!_layers.Contains(layer) || EqualityComparer<T>.Default.Equals(previous, next)) return;
        set(next);
        History.Push(new DelegateAction(() => set(previous), () => set(next)));
    }

    /// <summary>Shows all layers or only the given layer. The whole command is undone as one action.</summary>
    public void ShowLayers(Layer? only = null)
    {
        if (only != null && !_layers.Contains(only)) return;
        var changed = _layers.Where(l => l.Visible != (only == null || l == only)).ToArray();
        if (changed.Length == 0) return;
        void Apply(bool undo)
        {
            foreach (var layer in changed) layer.Visible = undo != (only == null || layer == only);
        }
        Apply(undo: false);
        History.Push(new DelegateAction(() => Apply(undo: true), () => Apply(undo: false)));
    }

    private IUndoAction DoInsert(Layer layer, int index)
    {
        index = Math.Clamp(index, 0, _layers.Count);
        _layers.Insert(index, layer);
        LayersChanged?.Invoke();
        return new DelegateAction(
            () => { _layers.Remove(layer); LayersChanged?.Invoke(); },
            () => { _layers.Insert(Math.Clamp(index, 0, _layers.Count), layer); LayersChanged?.Invoke(); });
    }

    private IUndoAction DoRemove(Layer layer)
    {
        int index = _layers.IndexOf(layer);
        _layers.RemoveAt(index);
        LayersChanged?.Invoke();
        return new DelegateAction(
            () => { _layers.Insert(Math.Clamp(index, 0, _layers.Count), layer); LayersChanged?.Invoke(); },
            () => { _layers.Remove(layer); LayersChanged?.Invoke(); });
    }

    public Layer AddLayer(string name, int? index = null, uint[]? pixels = null)
    {
        var layer = new Layer(Width, Height, name, pixels);
        History.Push(DoInsert(layer, index ?? _layers.Count));
        return layer;
    }

    /// <summary>Adds a layer but does not put the addition into history: the action is returned so the caller can merge it with the first edit of the layer (one Ctrl+Z).</summary>
    public (Layer Layer, IUndoAction Action) AddLayerDeferred(string name, int index)
    {
        var layer = new Layer(Width, Height, name);
        return (layer, DoInsert(layer, index));
    }

    public bool RemoveLayer(Layer layer)
    {
        if (_layers.Count <= 1 || !_layers.Contains(layer)) return false;
        History.Push(DoRemove(layer));
        return true;
    }

    public Layer DuplicateLayer(Layer layer)
    {
        var copy = layer.Clone(layer.Name + CoreText.T(" копия"));
        History.Push(DoInsert(copy, _layers.IndexOf(layer) + 1));
        return copy;
    }

    public bool MoveLayer(Layer layer, int newIndex)
    {
        int old = _layers.IndexOf(layer);
        newIndex = Math.Clamp(newIndex, 0, _layers.Count - 1);
        if (old < 0 || old == newIndex) return false;

        void Move(int from, int to)
        {
            var l = _layers[from];
            _layers.RemoveAt(from);
            _layers.Insert(to, l);
            LayersChanged?.Invoke();
        }

        Move(old, newIndex);
        History.Push(new DelegateAction(() => Move(newIndex, old), () => Move(old, newIndex)));
        return true;
    }

    public bool CanMergeDown(Layer layer)
    {
        int index = _layers.IndexOf(layer);
        return index > 0 && layer.Visible && !layer.Locked &&
            _layers[index - 1].Visible && !_layers[index - 1].Locked;
    }

    /// <summary>Merges adjacent visible unlocked layers.</summary>
    public bool MergeDown(Layer layer)
    {
        if (!CanMergeDown(layer)) return false;
        int idx = _layers.IndexOf(layer);
        var below = _layers[idx - 1];
        var batch = new PixelBatch(below);
        for (int i = 0; i < layer.Pixels.Length; i++)
        {
            if (!Pixel.IsEmpty(layer.Pixels[i]))
                batch.Set(i % Width, i / Width, layer.Pixels[i]);
        }
        var actions = new List<IUndoAction>();
        var pixelAction = batch.ToAction();
        if (pixelAction != null) actions.Add(pixelAction);
        actions.Add(DoRemove(layer));
        History.Push(new CompositeAction(actions));
        return true;
    }

    /// <summary>The visible unlocked layers (bottom to top) that "merge visible" would combine; hidden and locked layers are left alone.</summary>
    public IReadOnlyList<Layer> MergeableVisibleLayers() => _layers.Where(l => l.Visible && !l.Locked).ToList();

    public bool CanMergeVisible() => MergeableVisibleLayers().Count >= 2;

    /// <summary>
    /// Merges all visible unlocked layers into the lowest of them (it keeps its name and place); the others are removed.
    /// One undo step. Hidden and locked layers stay as they are. Returns the resulting layer or null if there was nothing to merge.
    /// </summary>
    public Layer? MergeVisible()
    {
        var group = MergeableVisibleLayers();
        if (group.Count < 2) return null;
        var target = group[0];
        var batch = new PixelBatch(target);
        for (int g = 1; g < group.Count; g++)
        {
            var px = group[g].Pixels;
            for (int i = 0; i < px.Length; i++)
                if (!Pixel.IsEmpty(px[i])) batch.Set(i % Width, i / Width, px[i]);
        }
        var actions = new List<IUndoAction>();
        var pixelAction = batch.ToAction();
        if (pixelAction != null) actions.Add(pixelAction);
        for (int g = group.Count - 1; g >= 1; g--) actions.Add(DoRemove(group[g]));
        History.Push(new CompositeAction(actions));
        return target;
    }

    // ---------- compositing ----------

    public void Flatten(uint[] dest)
    {
        Array.Clear(dest);
        foreach (var layer in _layers)
        {
            if (!layer.Visible) continue;
            var src = layer.Pixels;
            for (int i = 0; i < src.Length; i++)
                if (src[i] != 0) dest[i] = src[i];
        }
    }

    public PixelImage Flatten()
    {
        var img = new PixelImage(Width, Height);
        Flatten(img.Pixels);
        return img;
    }
}
