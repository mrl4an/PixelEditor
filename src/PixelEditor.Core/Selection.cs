// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
namespace PixelEditor.Core;

public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public bool Contains(int x, int y) => x >= X && y >= Y && x < X + Width && y < Y + Height;

    /// <summary>A rectangle from two corner pixels (inclusive), in any order.</summary>
    public static PixelRect FromCorners(int x0, int y0, int x1, int y1) =>
        new(Math.Min(x0, x1), Math.Min(y0, y1), Math.Abs(x1 - x0) + 1, Math.Abs(y1 - y0) + 1);

    public PixelRect ClampTo(int width, int height)
    {
        int l = Math.Max(0, X), t = Math.Max(0, Y);
        int r = Math.Min(width, X + Width), b = Math.Min(height, Y + Height);
        return r <= l || b <= t ? new PixelRect(0, 0, 0, 0) : new PixelRect(l, t, r - l, b - t);
    }

    public bool IsEmpty => Width <= 0 || Height <= 0;
}

/// <summary>Operations on a layer area: copy, clear.</summary>
public static class Selection
{
    public static PixelImage Copy(Layer layer, PixelRect rect)
    {
        rect = rect.ClampTo(layer.Width, layer.Height);
        if (rect.IsEmpty) throw new ArgumentException(CoreText.T("Пустое выделение"));
        var img = new PixelImage(rect.Width, rect.Height);
        for (int y = 0; y < rect.Height; y++)
            for (int x = 0; x < rect.Width; x++)
                img.Pixels[y * rect.Width + x] = layer.Get(rect.X + x, rect.Y + y);
        return img;
    }

    public static void Clear(PixelBatch batch, PixelRect rect)
    {
        rect = rect.ClampTo(batch.Layer.Width, batch.Layer.Height);
        for (int y = 0; y < rect.Height; y++)
            for (int x = 0; x < rect.Width; x++)
                batch.Set(rect.X + x, rect.Y + y, Pixel.Empty);
    }
}

/// <summary>
/// A "floating" area: the pixels are cut out (or pasted) and move above the layer. The layer always reflects the current
/// position, so the renderer does not need to do anything special. Commit gives one undo action.
/// An area can have a shape (Mask): then only the pixels under the mask are lifted and erased.
/// </summary>
public sealed class FloatingSelection
{
    private readonly PixelBatch _batch;
    private readonly PixelRect? _source;
    private readonly bool[]? _sourceMask; // mask of the source place (local, the size of _source)

    public PixelImage Pixels { get; private set; }

    /// <summary>The area shape (local bits the size of Pixels); null if the area is rectangular.</summary>
    public bool[]? Mask { get; private set; }

    public int X { get; private set; }
    public int Y { get; private set; }
    public PixelRect Bounds => new(X, Y, Pixels.Width, Pixels.Height);

    private FloatingSelection(Layer layer, PixelImage pixels, int x, int y, PixelRect? source, bool[]? mask)
    {
        _batch = new PixelBatch(layer);
        Pixels = pixels;
        _source = source;
        Mask = mask;
        _sourceMask = mask;
        MoveTo(x, y);
    }

    /// <summary>Cuts out a layer area and makes it floating in its old place.</summary>
    public static FloatingSelection Lift(Layer layer, PixelRect rect)
    {
        rect = rect.ClampTo(layer.Width, layer.Height);
        return new FloatingSelection(layer, Selection.Copy(layer, rect), rect.X, rect.Y, rect, null);
    }

    /// <summary>Lifts a free-form area (magic wand, lasso).</summary>
    public static FloatingSelection LiftMask(Layer layer, SelectionMask mask)
    {
        var img = mask.Copy(layer);
        return new FloatingSelection(layer, img, mask.Bounds.X, mask.Bounds.Y, mask.Bounds, mask.Bits);
    }

    /// <summary>
    /// Lifts an "object": all non-empty pixels connected to (x, y) by adjacency (diagonals included). Neighboring objects and the empty background are not touched.
    /// null if (x, y) is empty.
    /// </summary>
    public static FloatingSelection? LiftObject(Layer layer, int x, int y)
    {
        var mask = SelectionMask.Object(layer, x, y);
        return mask == null ? null : LiftMask(layer, mask);
    }

    /// <summary>Pastes a picture as a floating area (there is no source place). Semi-transparency is discarded.</summary>
    public static FloatingSelection FromImage(Layer layer, PixelImage image, int x, int y)
    {
        var bin = Ops.Transform.BinarizeAlpha(image, 128);
        return new FloatingSelection(layer, bin, x, y, null, null);
    }

    public void MoveTo(int x, int y)
    {
        _batch.Revert();
        if (_source is PixelRect s)
        {
            if (_sourceMask == null) Selection.Clear(_batch, s);
            else
                for (int py = 0; py < s.Height; py++)
                    for (int px = 0; px < s.Width; px++)
                        if (_sourceMask[py * s.Width + px]) _batch.Set(s.X + px, s.Y + py, Pixel.Empty);
        }
        for (int py = 0; py < Pixels.Height; py++)
            for (int px = 0; px < Pixels.Width; px++)
            {
                uint p = Pixels.Pixels[py * Pixels.Width + px];
                if (!Pixel.IsEmpty(p)) _batch.Set(x + px, y + py, p);
            }
        X = x;
        Y = y;
    }

    public void MoveBy(int dx, int dy) => MoveTo(X + dx, Y + dy);

    /// <summary>
    /// Applies an image transform (rotation, flip, scale) to the floating area. The shape is transformed the same way.
    /// The center of the area stays in place.
    /// </summary>
    public void Transform(Func<PixelImage, PixelImage> apply)
    {
        double cx = X + Pixels.Width / 2.0, cy = Y + Pixels.Height / 2.0;
        PixelImage? maskImage = null;
        if (Mask != null)
        {
            maskImage = new PixelImage(Pixels.Width, Pixels.Height);
            for (int i = 0; i < Mask.Length; i++) if (Mask[i]) maskImage.Pixels[i] = 0xFFFFFFFF;
        }
        var next = apply(Pixels);
        if (maskImage != null)
        {
            var m = apply(maskImage);
            Mask = m.Pixels.Select(p => !Pixel.IsEmpty(p)).ToArray();
        }
        Pixels = next;
        MoveTo((int)Math.Round(cx - next.Width / 2.0), (int)Math.Round(cy - next.Height / 2.0));
    }

    /// <summary>A snapshot of the content and position: rotation is recomputed from it at each step (no accumulating loss).</summary>
    public (PixelImage Pixels, bool[]? Mask, int X, int Y) Save() => (Pixels, Mask, X, Y);

    public void Restore((PixelImage Pixels, bool[]? Mask, int X, int Y) state)
    {
        Pixels = state.Pixels;
        Mask = state.Mask;
        MoveTo(state.X, state.Y);
    }

    /// <summary>Stretches the snapshot content (<see cref="Save"/>) to w×h by nearest neighbor and places it at (x, y). The shape is scaled the same way.</summary>
    public void ScaleFrom((PixelImage Pixels, bool[]? Mask, int X, int Y) state, int x, int y, int w, int h)
    {
        w = Math.Max(1, w); h = Math.Max(1, h);
        var img = Ops.Resampler.Resize(state.Pixels, w, h, Ops.ResizeMethod.Nearest);
        bool[]? mask = null;
        if (state.Mask != null)
        {
            var m = new PixelImage(state.Pixels.Width, state.Pixels.Height);
            for (int i = 0; i < state.Mask.Length; i++) if (state.Mask[i]) m.Pixels[i] = 0xFFFFFFFF;
            mask = Ops.Resampler.Resize(m, w, h, Ops.ResizeMethod.Nearest).Pixels.Select(p => !Pixel.IsEmpty(p)).ToArray();
        }
        Pixels = img;
        Mask = mask;
        MoveTo(x, y);
    }

    /// <summary>An action for History (null if nothing changed).</summary>
    public IUndoAction? Commit() => _batch.ToAction();

    /// <summary>Returns the layer to the state before the area was lifted.</summary>
    public void Cancel() => _batch.Revert();
}
