// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Text.Json;

namespace PixelEditor.Core.Ops;

public sealed record SpriteStyleProfile(int Width, int Height, PixelRect Content, string[] Palette,
    string? OutlineColor = null, int OutlineThickness = 0, string? LightFrom = null);

public static class SpriteStyle
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { IgnoreReadOnlyProperties = true };

    public static SpriteStyleProfile Parse(string json) =>
        JsonSerializer.Deserialize<SpriteStyleProfile>(json, Json)
        ?? throw new ArgumentException("A style profile is required.");

    public static string ToJson(SpriteStyleProfile profile) => JsonSerializer.Serialize(profile, Json);

    public static SpriteStyleProfile Extract(PixelImage reference, int maxColors = 16)
    {
        if (maxColors is < 1 or > 256) throw new ArgumentOutOfRangeException(nameof(maxColors));
        if (reference.Width > 1024 || reference.Height > 1024)
            throw new ArgumentException("Use a sprite reference no larger than 1024×1024.");
        var opaque = Transform.BinarizeAlpha(reference, 128);
        var bounds = Bounds(opaque) ?? throw new ArgumentException("The style reference has no opaque pixels.");
        var edgeColors = new Dictionary<uint, int>();
        int edgeCount = 0, interiorCount = 0;
        long interiorLight = 0;
        var neighbors = Cleanup.Neighbors(diagonal: false);
        for (int y = bounds.Y; y < bounds.Y + bounds.Height; y++)
            for (int x = bounds.X; x < bounds.X + bounds.Width; x++)
            {
                uint p = opaque.Get(x, y);
                if (Pixel.IsEmpty(p)) continue;
                if (neighbors.Any(n => Pixel.IsEmpty(opaque.Get(x + n.Item1, y + n.Item2))))
                {
                    edgeColors[p] = edgeColors.GetValueOrDefault(p) + 1;
                    edgeCount++;
                }
                else
                {
                    interiorLight += Luma(p);
                    interiorCount++;
                }
            }

        var edge = edgeColors.OrderByDescending(p => p.Value).ThenBy(p => p.Key).First();
        bool outline = maxColors > 1 && bounds.Width > 2 && bounds.Height > 2 &&
            edgeCount >= 8 && edge.Value >= edgeCount * 0.75 && interiorCount > 0 &&
            Luma(edge.Key) + 24 < interiorLight / interiorCount;
        uint[] fixedColors = outline ? [edge.Key] : [];
        var palette = Quantizer.BuildSharedPalette([opaque], maxColors, fixedColors);
        return new(reference.Width, reference.Height, bounds, palette.Select(Pixel.ToHex).ToArray(),
            outline ? Pixel.ToHex(edge.Key) : null, outline ? 1 : 0);
    }

    public static PixelImage Apply(PixelImage source, SpriteStyleProfile profile, ResizeMethod resize = ResizeMethod.Smart)
    {
        if (profile.Width is < 1 or > 1024 || profile.Height is < 1 or > 1024)
            throw new ArgumentException("Style canvas dimensions must be between 1 and 1024.");
        var box = profile.Content;
        int thickness = profile.OutlineThickness;
        if (thickness is < 0 or > 4 || box.Width <= 2 * thickness || box.Height <= 2 * thickness ||
            box.X < 0 || box.Y < 0 || box.X > profile.Width - box.Width || box.Y > profile.Height - box.Height)
            throw new ArgumentException("The content box must fit the canvas and leave room for the outline.");

        if (profile.Palette is not { Length: >= 1 and <= 256 })
            throw new ArgumentException("The style needs one to 256 palette colors.");
        var palette = profile.Palette.Select(ReadColor).Distinct().ToArray();
        uint outline = thickness == 0 ? Pixel.Empty : ReadColor(profile.OutlineColor);
        if (thickness > 0 && !palette.Contains(outline))
            throw new ArgumentException("The outline color must be in the palette.");
        var light = profile.LightFrom == null ? ((int Dx, int Dy)?)null : Lighting.ToLight(profile.LightFrom);

        var bounds = Bounds(source) ?? throw new ArgumentException("The source has no opaque pixels.");
        var cropped = new PixelImage(bounds.Width, bounds.Height);
        for (int y = 0; y < bounds.Height; y++)
            Array.Copy(source.Pixels, (bounds.Y + y) * source.Width + bounds.X,
                cropped.Pixels, y * cropped.Width, cropped.Width);

        int innerW = box.Width - 2 * thickness, innerH = box.Height - 2 * thickness;
        double scale = Math.Min((double)innerW / cropped.Width, (double)innerH / cropped.Height);
        int width = Math.Clamp((int)Math.Round(cropped.Width * scale), 1, innerW);
        int height = Math.Clamp((int)Math.Round(cropped.Height * scale), 1, innerH);
        var method = scale > 1 ? ResizeMethod.Nearest : resize;
        var fitted = Transform.BinarizeAlpha(Resampler.Resize(cropped, width, height, method), 128);
        if (fitted.OpaqueCount() == 0)
            throw new ArgumentException("The fitted sprite became transparent. Increase the size or adjust the source mask.");
        if (light is { } direction) fitted = Lighting.EdgeShade(fitted, direction, 1, 0.25, 0.25);
        fitted = PerceptualQuantizer.Apply(fitted, palette, dither: false);

        var result = new PixelImage(profile.Width, profile.Height);
        int left = box.X + (box.Width - width) / 2;
        int top = box.Y + box.Height - thickness - height;
        for (int y = 0; y < height; y++)
            Array.Copy(fitted.Pixels, y * width, result.Pixels, (top + y) * result.Width + left, width);
        return thickness == 0 ? result : Outline.Apply(result, outline, OutlineMode.Outside, diagonal: false, thickness);
    }

    private static uint ReadColor(string? value) =>
        Pixel.TryParseHex(value, out uint color) && !Pixel.IsEmpty(color)
            ? color : throw new ArgumentException($"Invalid opaque palette color: {value}");

    private static int Luma(uint p) => (54 * Pixel.R(p) + 183 * Pixel.G(p) + 19 * Pixel.B(p)) / 256;

    private static PixelRect? Bounds(PixelImage image)
    {
        int left = image.Width, top = image.Height, right = -1, bottom = -1;
        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
            {
                if (Pixel.A(image.Get(x, y)) < 128) continue;
                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }
        return right < 0 ? null : new(left, top, right - left + 1, bottom - top + 1);
    }
}
