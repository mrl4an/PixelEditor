// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
namespace PixelEditor.Core;

public sealed class Layer
{
    public string Name { get; set; }
    public bool Visible { get; set; } = true;
    public bool Locked { get; set; }
    public int Width { get; }
    public int Height { get; }
    public uint[] Pixels { get; }

    public Layer(int width, int height, string name, uint[]? pixels = null)
    {
        Width = width;
        Height = height;
        Name = name;
        Pixels = pixels ?? new uint[width * height];
        if (Pixels.Length != width * height) throw new ArgumentException(CoreText.T("Неверная длина массива пикселей"));
    }

    public bool InBounds(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;

    public uint Get(int x, int y) => InBounds(x, y) ? Pixels[y * Width + x] : Pixel.Empty;

    public Layer Clone(string? name = null) =>
        new(Width, Height, name ?? Name, (uint[])Pixels.Clone()) { Visible = Visible, Locked = Locked };

    public PixelImage ToImage() => new(Width, Height, (uint[])Pixels.Clone());
}
