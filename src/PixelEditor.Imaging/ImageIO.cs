// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PixelEditor.Core;

namespace PixelEditor.Imaging;

/// <summary>Reading and writing raster formats through WIC (PNG, JPG, BMP, GIF, TIFF, WebP if the codec is present).</summary>
public static class ImageIO
{
    /// <summary>Loads an image with full alpha (before alphaThreshold). For a GIF the first frame is taken.</summary>
    public static PixelImage Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        using var ms = new MemoryStream(bytes);
        var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        return FromBitmap(decoder.Frames[0]);
    }

    /// <param name="zeroAlphaMeansOpaque">
    /// Some programs put a 32-bit raster with zeros in the alpha channel on the clipboard (screenshots, DIB).
    /// If the whole picture has zero alpha, treat it as opaque, otherwise the paste looks empty.
    /// </param>
    public static PixelImage FromBitmap(BitmapSource source, bool zeroAlphaMeansOpaque = false)
    {
        var converted = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int w = converted.PixelWidth, h = converted.PixelHeight;
        var bytes = new byte[w * h * 4];
        converted.CopyPixels(bytes, w * 4, 0);

        if (zeroAlphaMeansOpaque)
        {
            bool anyAlpha = false;
            for (int i = 3; i < bytes.Length; i += 4)
                if (bytes[i] != 0) { anyAlpha = true; break; }
            if (!anyAlpha)
                for (int i = 3; i < bytes.Length; i += 4) bytes[i] = 255;
        }

        var px = new uint[w * h];
        for (int i = 0; i < px.Length; i++)
        {
            byte b = bytes[i * 4], g = bytes[i * 4 + 1], r = bytes[i * 4 + 2], a = bytes[i * 4 + 3];
            px[i] = a == 0 ? Pixel.Empty : Pixel.Argb(a, r, g, b);
        }
        return new PixelImage(w, h, px);
    }

    public static BitmapSource ToBitmap(PixelImage image)
    {
        var bmp = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null, image.Pixels, image.Width * 4);
        bmp.Freeze();
        return bmp;
    }

    public static byte[] EncodePng(PixelImage image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(ToBitmap(image)));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    public static void SaveGif(IReadOnlyList<PixelImage> frames, int delayMs, string path, bool loop = true) =>
        File.WriteAllBytes(path, GifWriter.Encode(frames, delayMs, loop));

    public static void SavePng(PixelImage image, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(ToBitmap(image)));
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        encoder.Save(fs);
    }
}
