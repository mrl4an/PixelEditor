// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using PixelEditor.Core;
using PixelEditor.Core.Ops;

namespace PixelEditor.Tests;

public class FeatureTests
{
    private static readonly uint Red = Pixel.Rgb(255, 0, 0);
    private static readonly uint Blue = Pixel.Rgb(0, 0, 255);
    private static readonly uint Green = Pixel.Rgb(0, 255, 0);

    // ---------- shift and canvas ----------

    [Fact]
    public void Shift_WrapAndClip()
    {
        var img = new PixelImage(3, 1, [Red, Blue, Green]);
        var wrap = Transform.Shift(img, 1, 0, wrap: true);
        Assert.Equal([Green, Red, Blue], wrap.Pixels);
        var clip = Transform.Shift(img, 1, 0, wrap: false);
        Assert.Equal([0u, Red, Blue], clip.Pixels);
        var back = Transform.Shift(img, -1, 0, wrap: true);
        Assert.Equal([Blue, Green, Red], back.Pixels);
    }

    [Fact]
    public void ResizeCanvas_AnchorsContent()
    {
        var img = new PixelImage(1, 1, [Red]);
        var c = Transform.ResizeCanvas(img, 3, 3, 0.5, 0.5);
        Assert.Equal(Red, c.Get(1, 1));
        Assert.Equal(1, c.OpaqueCount());
        var br = Transform.ResizeCanvas(img, 3, 3, 1, 1);
        Assert.Equal(Red, br.Get(2, 2));
    }

    [Fact]
    public void Document_ResizeCanvas_UndoRedo()
    {
        var doc = new Document(2, 2);
        doc.Layers[0].Pixels[0] = Red;
        int canvasEvents = 0;
        doc.CanvasChanged += () => canvasEvents++;

        doc.ResizeCanvas(4, 3, 0, 0);
        Assert.Equal((4, 3), (doc.Width, doc.Height));
        Assert.Equal(Red, doc.Layers[0].Get(0, 0));
        Assert.Equal(12, doc.Layers[0].Pixels.Length);

        doc.History.Undo();
        Assert.Equal((2, 2), (doc.Width, doc.Height));
        Assert.Equal(4, doc.Layers[0].Pixels.Length);
        doc.History.Redo();
        Assert.Equal((4, 3), (doc.Width, doc.Height));
        Assert.Equal(3, canvasEvents);
    }

    [Fact]
    public void Project_PersistsSheetGrid()
    {
        var doc = new Document(8, 8) { SheetCellWidth = 4, SheetCellHeight = 4 };
        using var ms = new MemoryStream();
        ProjectIO.Save(doc, ms);
        ms.Position = 0;
        var back = ProjectIO.Load(ms);
        Assert.Equal((4, 4), (back.SheetCellWidth, back.SheetCellHeight));
    }

    // ---------- selection ----------

    [Fact]
    public void FloatingSelection_MoveCommitUndo()
    {
        var doc = new Document(5, 1);
        var layer = doc.Layers[0];
        layer.Pixels[0] = Red;
        layer.Pixels[1] = Blue;

        var sel = FloatingSelection.Lift(layer, new PixelRect(0, 0, 2, 1));
        sel.MoveBy(2, 0);
        Assert.Equal([0u, 0u, Red, Blue, 0u], layer.Pixels);

        sel.MoveBy(-1, 0); // moves again without accumulating traces
        Assert.Equal([0u, Red, Blue, 0u, 0u], layer.Pixels);

        var action = sel.Commit();
        Assert.NotNull(action);
        doc.History.Push(action!);
        doc.History.Undo();
        Assert.Equal([Red, Blue, 0u, 0u, 0u], layer.Pixels);
    }

    [Fact]
    public void FloatingSelection_Cancel_RestoresLayer_AndPasteKeepsBackground()
    {
        var doc = new Document(3, 1);
        var layer = doc.Layers[0];
        layer.Pixels[0] = Red;
        layer.Pixels[2] = Green;

        var cut = FloatingSelection.Lift(layer, new PixelRect(0, 0, 1, 1));
        cut.MoveBy(1, 0);
        cut.Cancel();
        Assert.Equal([Red, 0u, Green], layer.Pixels);

        // a paste with a transparent pixel does not erase what is under it
        var pasted = FloatingSelection.FromImage(layer, new PixelImage(2, 1, [Pixel.Empty, Blue]), 1, 0);
        Assert.Equal([Red, 0u, Blue], layer.Pixels);
        pasted.Cancel();
        Assert.Equal([Red, 0u, Green], layer.Pixels);
    }

    // ---------- sheets and animation ----------

    [Fact]
    public void Sheet_SliceAndCompose_RoundTrip()
    {
        var img = new PixelImage(4, 2);
        img.Set(0, 0, Red);
        img.Set(2, 1, Blue);
        var cells = Sheet.Slice(img, 2, 2).ToList();
        Assert.Equal(2, cells.Count);
        Assert.Equal(Red, cells[0].Image.Get(0, 0));
        Assert.Equal(Blue, cells[1].Image.Get(0, 1));

        var back = Sheet.Compose(cells.Select(c => c.Image).ToList(), 2);
        Assert.Equal(img.Pixels, back.Pixels);

        Assert.Single(Sheet.Slice(img, 2, 2, skipEmpty: true), c => c.Col == 0);
        var blank = new PixelImage(4, 2);
        Assert.Empty(Sheet.Slice(blank, 2, 2, skipEmpty: true));
    }

    [Fact]
    public void Animation_Frames_FromLayersOrGrid()
    {
        var doc = new Document(2, 2);
        doc.Layers[0].Pixels[0] = Red;
        doc.AddLayer("кадр 2").Pixels[0] = Blue;
        Assert.Equal(2, Animation.GetFrames(doc).Count);

        var sheet = new Document(4, 2) { SheetCellWidth = 2, SheetCellHeight = 2 };
        sheet.Layers[0].Pixels[0] = Red; // cell 0
        Assert.Single(Animation.GetFrames(sheet)); // the tail of empty ones is dropped
        sheet.Layers[0].Pixels[2] = Blue; // cell 1
        Assert.Equal(2, Animation.GetFrames(sheet).Count);
    }

    // ---------- palette ----------

    [Fact]
    public void PaletteTools_SortAndRemoveUnused()
    {
        var white = Pixel.Rgb(255, 255, 255);
        var black = Pixel.Rgb(0, 0, 0);
        Assert.Equal([black, Blue, Green, white], PaletteTools.SortByLuminance([white, Green, black, Blue]));

        var hue = PaletteTools.SortByHue([Blue, Red, white, black, Green]);
        Assert.Equal([black, white], hue.Take(2));
        Assert.Equal([Red, Green, Blue], hue.Skip(2));

        var img = new PixelImage(2, 1, [Red, Red]);
        Assert.Equal([Red], PaletteTools.RemoveUnused([Red, Blue, Green], img));
    }

    // ---------- GIF ----------

    [Fact]
    public void Gif_RoundTrip_SmallFramesWithTransparency()
    {
        var f1 = new PixelImage(3, 2, [Red, 0, Blue, 0, Green, Red]);
        var f2 = new PixelImage(3, 2, [Blue, Blue, 0, 0, 0, Green]);
        var bytes = GifWriter.Encode([f1, f2], 120);

        var gif = GifDecoder.Decode(bytes);
        Assert.Equal((3, 2), (gif.Width, gif.Height));
        Assert.Equal(2, gif.Frames.Count);
        Assert.Equal(12, gif.Frames[0].DelayCs);
        Assert.Equal(f1.Pixels, gif.Frames[0].Image.Pixels);
        Assert.Equal(f2.Pixels, gif.Frames[1].Image.Pixels);
        Assert.True(gif.Looping);
    }

    [Theory]
    [InlineData(40, 40, 50)]    // code size growth
    [InlineData(120, 120, 200)] // dictionary reset (more than 4096 entries)
    [InlineData(1, 1, 1)]
    [InlineData(300, 5, 2)]     // long repeats
    public void Gif_RoundTrip_LargerImages(int w, int h, int colors)
    {
        var rng = new Random(42);
        var palette = Enumerable.Range(0, colors).Select(i => Pixel.Rgb((byte)(i * 7), (byte)(i * 13), (byte)(i * 29))).ToArray();
        var img = new PixelImage(w, h);
        for (int i = 0; i < img.Pixels.Length; i++)
            img.Pixels[i] = rng.Next(10) == 0 ? Pixel.Empty : palette[rng.Next(colors)];

        var gif = GifDecoder.Decode(GifWriter.Encode([img], 100, loop: false));
        Assert.Single(gif.Frames);
        Assert.Equal(img.Pixels, gif.Frames[0].Image.Pixels);
        Assert.False(gif.Looping);
    }

    [Fact]
    public void Gif_MoreThan255Colors_IsQuantizedNotCorrupted()
    {
        var img = new PixelImage(32, 32);
        for (int i = 0; i < img.Pixels.Length; i++)
            img.Pixels[i] = Pixel.Rgb((byte)(i % 256), (byte)(i / 4), (byte)(i * 3));
        var gif = GifDecoder.Decode(GifWriter.Encode([img], 100));
        Assert.True(gif.Frames[0].Image.DistinctColors() <= 255);
        Assert.Equal(1024, gif.Frames[0].Image.OpaqueCount());
    }

    [Fact]
    public void Gif_MismatchedFrames_Throw()
    {
        Assert.Throws<ArgumentException>(() => GifWriter.Encode([new PixelImage(1, 1), new PixelImage(2, 1)], 100));
        Assert.Throws<ArgumentException>(() => GifWriter.Encode([], 100));
    }
}

/// <summary>An independent GIF decoder to check the encoder (it does not use the encoder code).</summary>
public static class GifDecoder
{
    public sealed record Frame(PixelImage Image, int DelayCs);
    public sealed record Gif(int Width, int Height, List<Frame> Frames, bool Looping);

    private static int U16(byte[] g, int p) => g[p] | (g[p + 1] << 8);

    public static Gif Decode(byte[] g)
    {
        Assert.Equal("GIF89a", System.Text.Encoding.ASCII.GetString(g, 0, 6));
        int w = U16(g, 6), h = U16(g, 8);
        int packed = g[10];
        int pos = 13;
        if ((packed & 0x80) != 0) pos += 3 * (1 << ((packed & 7) + 1));

        var frames = new List<Frame>();
        bool looping = false;
        int delay = 0, trans = -1;

        while (true)
        {
            byte b = g[pos++];
            if (b == 0x3B) break;
            if (b == 0x21)
            {
                byte label = g[pos++];
                if (label == 0xF9)
                {
                    pos++; // block size
                    int p = g[pos++];
                    delay = U16(g, pos); pos += 2;
                    trans = (p & 1) != 0 ? g[pos] : -1;
                    pos++;
                    pos++; // terminator
                }
                else
                {
                    if (label == 0xFF && System.Text.Encoding.ASCII.GetString(g, pos + 1, 11) == "NETSCAPE2.0") looping = true;
                    while (g[pos] != 0) pos += g[pos] + 1;
                    pos++;
                }
                continue;
            }
            Assert.Equal(0x2C, b);
            int fw = U16(g, pos + 4), fh = U16(g, pos + 6);
            int fp = g[pos + 8];
            pos += 9;
            var table = new List<uint>();
            if ((fp & 0x80) != 0)
            {
                int n = 1 << ((fp & 7) + 1);
                for (int i = 0; i < n; i++) table.Add(Pixel.Rgb(g[pos + i * 3], g[pos + i * 3 + 1], g[pos + i * 3 + 2]));
                pos += n * 3;
            }
            int minCode = g[pos++];
            var data = new List<byte>();
            while (g[pos] != 0)
            {
                int n = g[pos++];
                for (int i = 0; i < n; i++) data.Add(g[pos++]);
            }
            pos++;

            var idx = LzwDecode(data.ToArray(), minCode);
            Assert.Equal(fw * fh, idx.Count);
            var img = new PixelImage(fw, fh);
            for (int i = 0; i < idx.Count; i++)
                img.Pixels[i] = idx[i] == trans ? Pixel.Empty : table[idx[i]];
            frames.Add(new Frame(img, delay));
        }
        return new Gif(w, h, frames, looping);
    }

    private static List<byte> LzwDecode(byte[] data, int minCode)
    {
        int clear = 1 << minCode, eoi = clear + 1;
        var table = new List<byte[]>();
        void Reset()
        {
            table.Clear();
            for (int i = 0; i < clear; i++) table.Add([(byte)i]);
            table.Add([]); table.Add([]);
        }
        Reset();

        var result = new List<byte>();
        int codeSize = minCode + 1, prev = -1, pos = 0, bits = 0;
        long buf = 0;
        while (true)
        {
            while (bits < codeSize)
            {
                if (pos >= data.Length) return result;
                buf |= (long)data[pos++] << bits;
                bits += 8;
            }
            int code = (int)(buf & ((1 << codeSize) - 1));
            buf >>= codeSize;
            bits -= codeSize;

            if (code == clear) { Reset(); codeSize = minCode + 1; prev = -1; continue; }
            if (code == eoi) return result;

            byte[] entry;
            if (code < table.Count) entry = table[code];
            else if (code == table.Count && prev >= 0) entry = [.. table[prev], table[prev][0]];
            else throw new InvalidDataException($"Неверный код LZW {code}");
            result.AddRange(entry);

            if (prev >= 0 && table.Count < 4096)
            {
                table.Add([.. table[prev], entry[0]]);
                if (table.Count == (1 << codeSize) && codeSize < 12) codeSize++;
            }
            prev = code;
        }
    }
}
