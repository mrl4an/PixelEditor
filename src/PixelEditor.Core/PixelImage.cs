// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
namespace PixelEditor.Core;

/// <summary>A plain raster without layers. The unit of exchange between operations, pipelines and the CLI.</summary>
public sealed class PixelImage
{
    public int Width { get; }
    public int Height { get; }
    public uint[] Pixels { get; }

    public PixelImage(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), CoreText.T("Размер должен быть > 0"));
        Width = width;
        Height = height;
        Pixels = new uint[checked(width * height)];
    }

    public PixelImage(int width, int height, uint[] pixels)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), CoreText.T("Размер должен быть > 0"));
        if (pixels.Length != width * height) throw new ArgumentException(CoreText.T("Неверная длина массива пикселей"));
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public bool InBounds(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;

    public uint Get(int x, int y) => InBounds(x, y) ? Pixels[y * Width + x] : Pixel.Empty;

    public void Set(int x, int y, uint c)
    {
        if (InBounds(x, y)) Pixels[y * Width + x] = c;
    }

    public PixelImage Clone() => new(Width, Height, (uint[])Pixels.Clone());

    public int OpaqueCount()
    {
        int n = 0;
        foreach (var p in Pixels) if (!Pixel.IsEmpty(p)) n++;
        return n;
    }

    public int DistinctColors()
    {
        var set = new HashSet<uint>();
        foreach (var p in Pixels) if (!Pixel.IsEmpty(p)) set.Add(p);
        return set.Count;
    }
}
