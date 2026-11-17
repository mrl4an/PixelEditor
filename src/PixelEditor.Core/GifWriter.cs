// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using PixelEditor.Core.Ops;

namespace PixelEditor.Core;

/// <summary>
/// GIF89a with binary transparency and looping. Each frame has its own local palette (up to 255 colors + transparent index 0);
/// if there are more colors, the frame is quantized. Frames must be the same size.
/// </summary>
public static class GifWriter
{
    public static byte[] Encode(IReadOnlyList<PixelImage> frames, int delayMs, bool loop = true)
    {
        if (frames.Count == 0) throw new ArgumentException(CoreText.T("Нет кадров"));
        int w = frames[0].Width, h = frames[0].Height;
        if (frames.Any(f => f.Width != w || f.Height != h)) throw new ArgumentException(CoreText.T("Все кадры должны быть одного размера"));
        if (w > 65535 || h > 65535) throw new ArgumentException(CoreText.T("Слишком большой кадр для GIF"));

        var ms = new MemoryStream();
        void Bytes(params byte[] b) => ms.Write(b, 0, b.Length);
        void U16(int v) { ms.WriteByte((byte)(v & 0xFF)); ms.WriteByte((byte)(v >> 8)); }

        Bytes("GIF89a"u8.ToArray());
        U16(w); U16(h);
        Bytes(0x00, 0x00, 0x00); // no global palette

        if (loop)
        {
            Bytes(0x21, 0xFF, 0x0B);
            Bytes("NETSCAPE2.0"u8.ToArray());
            Bytes(0x03, 0x01, 0x00, 0x00, 0x00);
        }

        int delayCs = Math.Max(2, (int)Math.Round(delayMs / 10.0));
        foreach (var frame in frames)
        {
            var (indices, palette) = Index(frame);
            int tableSize = 2;
            int bits = 1;
            while (tableSize < palette.Count + 1) { tableSize <<= 1; bits++; }
            int minCode = Math.Max(2, bits);

            // graphics control extension: transparent index 0, clear the frame before the next one
            Bytes(0x21, 0xF9, 0x04, (byte)((2 << 2) | 1));
            U16(delayCs);
            Bytes(0x00, 0x00);

            ms.WriteByte(0x2C);
            U16(0); U16(0); U16(w); U16(h);
            ms.WriteByte((byte)(0x80 | (bits - 1)));

            // local table: 0 = transparent (black), then the colors
            ms.WriteByte(0); ms.WriteByte(0); ms.WriteByte(0);
            foreach (var c in palette) { ms.WriteByte(Pixel.R(c)); ms.WriteByte(Pixel.G(c)); ms.WriteByte(Pixel.B(c)); }
            for (int i = palette.Count + 1; i < tableSize; i++) { ms.WriteByte(0); ms.WriteByte(0); ms.WriteByte(0); }

            ms.WriteByte((byte)minCode);
            var data = Lzw(indices, minCode);
            for (int pos = 0; pos < data.Length; pos += 255)
            {
                int n = Math.Min(255, data.Length - pos);
                ms.WriteByte((byte)n);
                ms.Write(data, pos, n);
            }
            ms.WriteByte(0);
        }
        ms.WriteByte(0x3B);
        return ms.ToArray();
    }

    private static (byte[] Indices, List<uint> Palette) Index(PixelImage frame)
    {
        var src = frame;
        var colors = ColorStats.Count(src).Select(c => c.Color).ToList();
        if (colors.Count > 255)
        {
            var pal = Quantizer.BuildPalette(src, 255);
            src = Quantizer.Apply(src, pal, dither: false);
            colors = ColorStats.Count(src).Select(c => c.Color).ToList();
        }
        var map = new Dictionary<uint, byte>();
        for (int i = 0; i < colors.Count; i++) map[colors[i]] = (byte)(i + 1);

        var idx = new byte[src.Pixels.Length];
        for (int i = 0; i < idx.Length; i++)
            idx[i] = Pixel.IsEmpty(src.Pixels[i]) ? (byte)0 : map[src.Pixels[i]];
        return (idx, colors);
    }

    private static byte[] Lzw(byte[] indices, int minCode)
    {
        int clear = 1 << minCode, eoi = clear + 1;
        var output = new List<byte>(indices.Length / 2 + 16);
        long buf = 0;
        int bitCount = 0;

        void Write(int code, int size)
        {
            buf |= (long)code << bitCount;
            bitCount += size;
            while (bitCount >= 8)
            {
                output.Add((byte)(buf & 0xFF));
                buf >>= 8;
                bitCount -= 8;
            }
        }

        var dict = new Dictionary<int, int>();
        int next = eoi + 1, codeSize = minCode + 1;
        Write(clear, codeSize);

        int prefix = indices[0];
        for (int i = 1; i < indices.Length; i++)
        {
            int k = indices[i];
            int key = (prefix << 8) | k;
            if (dict.TryGetValue(key, out var code)) { prefix = code; continue; }

            Write(prefix, codeSize);
            if (next > (1 << codeSize) - 1 && codeSize < 12) codeSize++;
            if (next < 4096)
            {
                dict[key] = next++;
            }
            else
            {
                Write(clear, codeSize);
                dict.Clear();
                next = eoi + 1;
                codeSize = minCode + 1;
            }
            prefix = k;
        }
        Write(prefix, codeSize);
        if (next > (1 << codeSize) - 1 && codeSize < 12) codeSize++;
        Write(eoi, codeSize);
        if (bitCount > 0) output.Add((byte)(buf & 0xFF));
        return output.ToArray();
    }
}
