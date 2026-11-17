// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Text;
using PixelEditor.Core;
using PixelEditor.Core.Ops;

namespace PixelEditor.Tests;

/// <summary>
/// Runs every built-in template on varied inputs (photo-like, painted, exact sprite, thick outline, flat background, tiny, large, transparent, non-square).
/// No step may fail on any of them. PIXED_TPL_SHOTS=folder also writes before/after panels (x4) and a table.
/// </summary>
public class TemplateEvaluationTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static PixelImage ThickOutlineSprite()
    {
        var img = new PixelImage(28, 28);
        for (int y = 0; y < 28; y++)
            for (int x = 0; x < 28; x++)
            {
                double dx = (x - 14) / 11.0, dy = (y - 14) / 11.0, d = dx * dx + dy * dy;
                if (d <= 1) img.Set(x, y, d > 0.62 ? Pixel.Rgb(25, 20, 35) : y < 12 ? Pixel.Rgb(200, 80, 60) : Pixel.Rgb(235, 190, 120));
            }
        img.Set(10, 10, Pixel.Rgb(255, 255, 255));     // a highlight of one pixel: must survive the defaults of the cleanup
        return img;
    }

    private static PixelImage Large()
    {
        var img = new PixelImage(512, 512);
        var rnd = new Random(5);
        for (int y = 0; y < 512; y++)
            for (int x = 0; x < 512; x++)
            {
                double d = Math.Sqrt((x - 256.0) * (x - 256.0) + (y - 280.0) * (y - 280.0));
                int n = rnd.Next(-6, 7);
                uint c = d < 170 ? Pixel.Rgb((byte)Math.Clamp(190 - d / 2 + n, 0, 255), (byte)Math.Clamp(80 + x / 6 + n, 0, 255), (byte)Math.Clamp(70 + n, 0, 255))
                                 : Pixel.Rgb((byte)Math.Clamp(225 + n, 0, 255), (byte)Math.Clamp(232 + n, 0, 255), (byte)Math.Clamp(240 + n, 0, 255));
                img.Set(x, y, c);
            }
        return img;
    }

    private static PixelImage Wide()
    {
        var img = new PixelImage(100, 30);
        for (int y = 0; y < 30; y++)
            for (int x = 0; x < 100; x++)
                img.Set(x, y, x is >= 20 and < 80 && y is >= 6 and < 24 ? Pixel.Rgb((byte)(40 + x), (byte)(120 + y * 3), 60) : Pixel.Rgb(240, 240, 235));
        return img;
    }

    private static PixelImage Tiny()
    {
        var img = new PixelImage(8, 8);
        var rnd = new Random(9);
        for (int i = 0; i < 64; i++) img.Pixels[i] = Pixel.Rgb((byte)rnd.Next(256), (byte)rnd.Next(256), (byte)rnd.Next(256));
        return img;
    }

    [Fact]
    public void EveryBuiltInTemplate_RunsOnVariedInputs_WithoutStepErrors()
    {
        string? shots = Environment.GetEnvironmentVariable("PIXED_TPL_SHOTS");
        if (shots != null) Directory.CreateDirectory(shots);
        var cases = BgCases.All();
        var inputs = new List<(string Name, PixelImage Image)>
        {
            ("photo_noisy_flat", cases.First(c => c.Name == "noisy_flat_jpeg").Image),
            ("photo_radial_gradient", cases.First(c => c.Name == "gradient_radial").Image),
            ("painterly", BgCases.Painterly().Image),
            ("sprite_exact_on_flat_bg", cases.First(c => c.Name == "pixel_art_exact").Image),
            ("sprite_exact_transparent", BgCases.TruthImage(cases.First(c => c.Name == "pixel_art_exact"))),
            ("sprite_thick_outline", ThickOutlineSprite()),
            ("tiny_8x8", Tiny()),
            ("large_512", Large()),
            ("transparent_16", new PixelImage(16, 16)),
            ("non_square_100x30", Wide()),
        };
        var variants = new (string Label, string Id, Dictionary<string, object?> Values)[]
        {
            ("defaults", "photo-to-pixel-art", new()),
            ("bg=auto outline=black", "photo-to-pixel-art", new() { ["background"] = "auto", ["outline"] = "black", ["size"] = 32 }),
            ("bg=complex outline=colored", "photo-to-pixel-art", new() { ["background"] = "complex", ["outline"] = "colored" }),
            ("defaults", "downscale-pixel-art", new() { ["size"] = 16 }),
            ("defaults", "remove-background", new()),
            ("complex+trim", "remove-background", new() { ["subject"] = "complex", ["trim"] = true }),
            ("simplify=8", "remove-background", new() { ["simplify"] = 8 }),
            ("defaults", "remove-outline", new()),
            ("dark", "remove-outline", new() { ["dark"] = true }),
            ("defaults", "clean-sprite", new()),
            ("speckle=3", "clean-sprite", new() { ["speckle"] = 3 }),
            ("defaults", "apply-palette", new()),
            ("Game Boy dither", "apply-palette", new() { ["palette"] = "Game Boy", ["dither"] = true }),
            ("defaults", "prepare-for-game", new()),
            ("cell 32 bottom", "prepare-for-game", new() { ["fit"] = "cell", ["cell"] = 32, ["anchor"] = "bottom" }),
            ("defaults", "check-ready", new()),
        };

        var sb = new StringBuilder();
        sb.AppendLine($"{"input",-26} {"template",-24} {"variant",-28} in -> out        colors ok  checks");
        foreach (var (name, image) in inputs)
        {
            var panels = new List<PixelImage> { image };
            foreach (var (label, id, values) in variants)
            {
                var p = Pipeline.Parse(BuiltInTemplates.All.Single(t => t.Id == "system:" + id).Json);
                var r = p.Run(image, null, values);
                var failed = r.Checks.Where(c => !c.Passed && c.Required).Select(c => $"{c.Check}: {c.Message}").ToList();
                sb.AppendLine($"{name,-26} {id,-24} {label,-28} {image.Width}x{image.Height} -> {r.Image.Width}x{r.Image.Height}  {r.Image.DistinctColors(),5}  {(r.Ok ? "ok" : "ERR")}  {(failed.Count == 0 ? "pass" : "FAIL " + string.Join(" | ", failed))}");
                Assert.True(r.Ok, $"{name} / {id} / {label}: " + string.Join("; ", r.Log.Where(l => l.Error != null).Select(l => l.Op + ": " + l.Error)));
                if (shots != null && id != "check-ready" && (label != "defaults" || id is "photo-to-pixel-art" or "remove-background" or "clean-sprite" or "prepare-for-game"))
                    panels.Add(r.Image);
            }
            if (shots != null) BgCases.WritePanels(Path.Combine(shots, name + ".png"), panels.Take(9).ToList(), image.Width <= 32 ? 8 : image.Width >= 256 ? 1 : 4);
        }
        output.WriteLine(sb.ToString());
        if (shots != null) File.WriteAllText(Path.Combine(shots, "table.txt"), sb.ToString());
    }

    [Fact]
    public void Checks_AreMeaningful_TheyFailWhenTheyShould()
    {
        PipelineResult Run(string id, PixelImage img, Dictionary<string, object?>? v = null) =>
            Pipeline.Parse(BuiltInTemplates.All.Single(t => t.Id == "system:" + id).Json).Run(img, null, v);

        // a sprite bigger than the cell is NOT cropped: the size check fails instead
        Assert.False(Run("prepare-for-game", ThickOutlineSprite(), new() { ["fit"] = "cell", ["cell"] = 16 }).ChecksPassed);
        Assert.True(Run("prepare-for-game", ThickOutlineSprite(), new() { ["fit"] = "cell", ["cell"] = 32 }).ChecksPassed);
        // too many colors for the limit
        Assert.False(Run("prepare-for-game", Large(), new() { ["maxColors"] = 8 }).ChecksPassed);
        Assert.False(Run("check-ready", Large()).ChecksPassed);                       // too big and too many colors
        Assert.True(Run("check-ready", ThickOutlineSprite()).ChecksPassed);
        // an apply-palette check fails on a palette that does not hold the result: nothing but the palette may remain
        Assert.True(Run("apply-palette", BgCases.Painterly().Image).ChecksPassed);
        // removing the background of a picture that is all background erases the subject: the check says so
        var flat = new PixelImage(16, 16);
        Array.Fill(flat.Pixels, Pixel.Rgb(10, 20, 30));
        Assert.False(Run("remove-background", flat).ChecksPassed);
        // clean-sprite leaves one-pixel highlights that are part of a bigger sprite alone
        var cleaned = Run("clean-sprite", ThickOutlineSprite()).Image;
        Assert.Equal(Pixel.Rgb(255, 255, 255), cleaned.Get(10, 10));
        // a lone speck is removed and the check would catch it otherwise
        var speck = ThickOutlineSprite();
        speck.Set(1, 1, Pixel.Rgb(0, 255, 0));
        Assert.True(Pipeline.Parse(BuiltInTemplates.All.Single(t => t.Id == "system:clean-sprite").Json).RunChecks(speck).Any(c => !c.Passed));
        Assert.True(Run("clean-sprite", speck).Success);
        Assert.True(Pixel.IsEmpty(Run("clean-sprite", speck).Image.Get(1, 1)));
    }
}
