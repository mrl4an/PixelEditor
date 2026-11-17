// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using PixelEditor.Core;

namespace PixelEditor.ViewModels;

/// <summary>Stretching and shrinking the selection by the handles on the frame edges (as in Paint). The size is computed from a snapshot, so quality does not drop.</summary>
public sealed partial class CanvasViewModel
{
    /// <summary>Handles clockwise, starting from the top left corner: 0 NW, 1 N, 2 NE, 3 E, 4 SE, 5 S, 6 SW, 7 W.</summary>
    public static bool IsCornerHandle(int handle) => handle % 2 == 0;

    private (PixelImage Pixels, bool[]? Mask, int X, int Y)? _scaleBase;
    private int _scaleHandle;

    public bool IsScaling => _scaleBase != null;

    /// <summary>The position of a handle (its center) for a frame.</summary>
    public static (double X, double Y) HandlePoint(PixelRect r, int handle)
    {
        double l = r.X, t = r.Y, rr = r.X + r.Width, b = r.Y + r.Height, cx = (l + rr) / 2, cy = (t + b) / 2;
        return handle switch
        {
            0 => (l, t), 1 => (cx, t), 2 => (rr, t), 3 => (rr, cy),
            4 => (rr, b), 5 => (cx, b), 6 => (l, b), _ => (l, cy),
        };
    }

    public bool BeginScaleHandle(int handle)
    {
        if (_batch != null || _drag != DragMode.None || !HasSelection || !_s.CanEditActiveLayer()) return false;
        if (_float == null && !(_selRect is PixelRect r && !r.IsEmpty && TryLift(r))) return false;
        _scaleBase = _float!.Save();
        _scaleHandle = handle;
        return true;
    }

    /// <summary>Drag a handle to the point (px, py) in document pixels. keepAspect: keep the proportions (for corner handles).</summary>
    public void ScaleHandleTo(double px, double py, bool keepAspect)
    {
        if (_scaleBase is not { } b || _float == null) return;
        int bw = b.Pixels.Width, bh = b.Pixels.Height;
        int left = b.X, top = b.Y, right = b.X + bw, bottom = b.Y + bh;
        int h = _scaleHandle;
        int ix = (int)Math.Round(px), iy = (int)Math.Round(py);
        if (h is 0 or 6 or 7) left = Math.Min(ix, right - 1);
        if (h is 2 or 3 or 4) right = Math.Max(ix, left + 1);
        if (h is 0 or 1 or 2) top = Math.Min(iy, bottom - 1);
        if (h is 4 or 5 or 6) bottom = Math.Max(iy, top + 1);

        int w = right - left, hh = bottom - top;
        if (keepAspect && IsCornerHandle(h))
        {
            double k = Math.Max(w / (double)bw, hh / (double)bh);
            w = Math.Max(1, (int)Math.Round(bw * k));
            hh = Math.Max(1, (int)Math.Round(bh * k));
            if (h is 0 or 6) left = right - w; else right = left + w;
            if (h is 0 or 2) top = bottom - hh; else bottom = top + hh;
        }
        _float.ScaleFrom(b, left, top, w, hh);
        UpdateSelection();
        _s.RequestRender();
        _s.Status(Loc.T("canvas.size_status", w, hh));
    }

    public void EndScaleHandle() => _scaleBase = null;
}
