// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
namespace PixelEditor.Core;

/// <summary>
/// A pixel = uint 0xAARRGGBB. In a document alpha is only 0 (empty, exactly the value 0) or 255.
/// Full alpha is allowed only in input images before alphaThreshold is applied.
/// </summary>
public static class Pixel
{
    public const uint Empty = 0;

    public static uint Argb(byte a, byte r, byte g, byte b) =>
        ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b;

    public static uint Rgb(byte r, byte g, byte b) => Argb(255, r, g, b);

    public static byte A(uint p) => (byte)(p >> 24);
    public static byte R(uint p) => (byte)(p >> 16);
    public static byte G(uint p) => (byte)(p >> 8);
    public static byte B(uint p) => (byte)p;

    public static bool IsEmpty(uint p) => (p >> 24) == 0;

    /// <summary>Binarizes alpha: >= threshold gives an opaque pixel, otherwise empty.</summary>
    public static uint Binarize(uint p, int threshold = 128) =>
        A(p) >= threshold && A(p) != 0 ? p | 0xFF000000u : Empty;

    public static string ToHex(uint p) => IsEmpty(p) ? "transparent" : $"#{p & 0xFFFFFFu:X6}";

    public static bool TryParseHex(string? s, out uint color)
    {
        color = Empty;
        if (string.IsNullOrWhiteSpace(s)) return false;
        s = s.Trim();
        if (s.Equals("transparent", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("none", StringComparison.OrdinalIgnoreCase))
            return true;
        if (s.StartsWith('#')) s = s[1..];
        if (s.Length == 3)
            s = string.Concat(s.Select(c => new string(c, 2)));
        if (s.Length != 6) return false;
        if (!uint.TryParse(s, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var v)) return false;
        color = 0xFF000000u | v;
        return true;
    }
}
