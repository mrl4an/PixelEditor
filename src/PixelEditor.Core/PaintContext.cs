// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
namespace PixelEditor.Core;

/// <summary>Parameters of the current stroke: color, brush size and mirrors.</summary>
public sealed class PaintContext(PixelBatch batch)
{
    public PixelBatch Batch => batch;
    public uint Color { get; set; }
    public int BrushSize { get; set; } = 1;
    public bool MirrorX { get; set; }
    public bool MirrorY { get; set; }

    public void Plot(int x, int y)
    {
        int size = Math.Max(1, BrushSize);
        int ox = x - (size - 1) / 2, oy = y - (size - 1) / 2;
        int w = batch.Layer.Width, h = batch.Layer.Height;
        // the mirrored copy is an exact reflection of the brush square (for even sizes shifting by the "center" gave a one pixel offset)
        int mx = w - ox - size, my = h - oy - size;
        Fill(ox, oy, size);
        if (MirrorX) Fill(mx, oy, size);
        if (MirrorY) Fill(ox, my, size);
        if (MirrorX && MirrorY) Fill(mx, my, size);
    }

    private void Fill(int ox, int oy, int size)
    {
        for (int dy = 0; dy < size; dy++)
            for (int dx = 0; dx < size; dx++)
                batch.Set(ox + dx, oy + dy, Color);
    }
}
