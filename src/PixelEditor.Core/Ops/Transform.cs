// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
namespace PixelEditor.Core.Ops;

public static class Transform
{
    public static PixelImage Pad(PixelImage src, int left, int top, int right, int bottom)
    {
        var dst = new PixelImage(src.Width + left + right, src.Height + top + bottom);
        for (int y = 0; y < src.Height; y++)
            for (int x = 0; x < src.Width; x++)
                dst.Set(x + left, y + top, src.Pixels[y * src.Width + x]);
        return dst;
    }

    /// <summary>Trims the empty edges. A completely empty picture becomes 1x1.</summary>
    public static PixelImage Trim(PixelImage src)
    {
        int minX = src.Width, minY = src.Height, maxX = -1, maxY = -1;
        for (int y = 0; y < src.Height; y++)
            for (int x = 0; x < src.Width; x++)
            {
                if (Pixel.IsEmpty(src.Pixels[y * src.Width + x])) continue;
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            }
        if (maxX < 0) return new PixelImage(1, 1);
        var dst = new PixelImage(maxX - minX + 1, maxY - minY + 1);
        for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
                dst.Set(x - minX, y - minY, src.Pixels[y * src.Width + x]);
        return dst;
    }

    public static PixelImage FlipH(PixelImage src)
    {
        var dst = new PixelImage(src.Width, src.Height);
        for (int y = 0; y < src.Height; y++)
            for (int x = 0; x < src.Width; x++)
                dst.Pixels[y * src.Width + (src.Width - 1 - x)] = src.Pixels[y * src.Width + x];
        return dst;
    }

    public static PixelImage FlipV(PixelImage src)
    {
        var dst = new PixelImage(src.Width, src.Height);
        for (int y = 0; y < src.Height; y++)
            for (int x = 0; x < src.Width; x++)
                dst.Pixels[(src.Height - 1 - y) * src.Width + x] = src.Pixels[y * src.Width + x];
        return dst;
    }

    /// <summary>Rotation clockwise by 90° * times.</summary>
    public static PixelImage Rotate90(PixelImage src, int times)
    {
        times = ((times % 4) + 4) % 4;
        var cur = src;
        for (int t = 0; t < times; t++)
        {
            var dst = new PixelImage(cur.Height, cur.Width);
            for (int y = 0; y < cur.Height; y++)
                for (int x = 0; x < cur.Width; x++)
                    dst.Pixels[x * dst.Width + (cur.Height - 1 - y)] = cur.Pixels[y * cur.Width + x];
            cur = dst;
        }
        return times == 0 ? src.Clone() : cur;
    }

    /// <summary>Shifts the content. wrap=true: what went past the edge appears on the other side (for tiles).</summary>
    public static PixelImage Shift(PixelImage src, int dx, int dy, bool wrap)
    {
        var dst = new PixelImage(src.Width, src.Height);
        for (int y = 0; y < src.Height; y++)
            for (int x = 0; x < src.Width; x++)
            {
                int sx = x - dx, sy = y - dy;
                if (wrap)
                {
                    sx = ((sx % src.Width) + src.Width) % src.Width;
                    sy = ((sy % src.Height) + src.Height) % src.Height;
                }
                else if (!src.InBounds(sx, sy)) continue;
                dst.Pixels[y * src.Width + x] = src.Pixels[sy * src.Width + sx];
            }
        return dst;
    }

    /// <summary>Anchor: topleft, top, topright, left, center, right, bottomleft, bottom, bottomright.</summary>
    public static (double X, double Y) ParseAnchor(string? anchor) => (anchor ?? "topleft").ToLowerInvariant().Replace("-", "").Replace("_", "") switch
    {
        "topleft" => (0, 0),
        "top" => (0.5, 0),
        "topright" => (1, 0),
        "left" => (0, 0.5),
        "center" => (0.5, 0.5),
        "right" => (1, 0.5),
        "bottomleft" => (0, 1),
        "bottom" => (0.5, 1),
        "bottomright" => (1, 1),
        var a => throw new ArgumentException(CoreText.F("Неизвестный anchor '{0}'", a)),
    };

    /// <summary>Changes the canvas size without scaling; ax, ay (0..1) set the anchor of the old content.</summary>
    public static PixelImage ResizeCanvas(PixelImage src, int width, int height, double ax, double ay)
    {
        var dst = new PixelImage(width, height);
        int ox = (int)Math.Round((width - src.Width) * ax), oy = (int)Math.Round((height - src.Height) * ay);
        for (int y = 0; y < src.Height; y++)
            for (int x = 0; x < src.Width; x++)
                dst.Set(x + ox, y + oy, src.Pixels[y * src.Width + x]);
        return dst;
    }

    public static PixelImage BinarizeAlpha(PixelImage src, int threshold)
    {
        var dst = new PixelImage(src.Width, src.Height);
        for (int i = 0; i < src.Pixels.Length; i++)
            dst.Pixels[i] = Pixel.Binarize(src.Pixels[i], threshold);
        return dst;
    }
}
