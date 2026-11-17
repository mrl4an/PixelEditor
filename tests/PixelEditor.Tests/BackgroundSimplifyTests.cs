// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Text;
using PixelEditor.Core;
using PixelEditor.Core.Ops;

namespace PixelEditor.Tests;

public class BackgroundSimplifyTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void Painterly_SimplifiedMode_FindsTheEnclosedSky_DefaultsDoNot()
    {
        var c = BgCases.Painterly();
        var sb = new StringBuilder();
        double Line(string label, PixelImage r)
        {
            var m = BgCases.Measure(r, c.Subject);
            sb.AppendLine($"{label,-34} bgIoU {m.BgIoU:F4}  subjIoU {m.SubjIoU:F4}  eaten {m.Eaten,5}  left {m.Left,5}");
            return m.BgIoU;
        }
        double rb = Line("removeBackground (default)", BgCases.Run("removeBackground", c.Image));
        double co = Line("cutout (default)", BgCases.Run("cutout", c.Image));
        double rbAll = Line("removeBackground enclosed=all", BgCases.Run("removeBackground", c.Image, """{"enclosed":"all"}"""));
        PixelImage? best = null;
        foreach (var n in new[] { 4, 6, 8, 12 })
        {
            var r = BgCases.Run("removeBackground", c.Image, $$"""{"simplify":{{n}}}""");
            Line($"removeBackground simplify={n}", r);
            Line($"cutout simplify={n}", BgCases.Run("cutout", c.Image, $$"""{"simplify":{{n}}}"""));
            if (n == 8) best = r;
        }
        Line("removeBackground simplify=8 denoise=1", BgCases.Run("removeBackground", c.Image, """{"simplify":8,"denoise":1}"""));
        output.WriteLine(sb.ToString());
        string? shots = Environment.GetEnvironmentVariable("PIXED_BG_SHOTS");
        if (shots != null)
        {
            Directory.CreateDirectory(shots);
            BgCases.WritePanels(Path.Combine(shots, "painterly.png"), [c.Image, BgCases.TruthImage(c), BgCases.Run("removeBackground", c.Image), best!, BgCases.Run("cutout", c.Image, """{"simplify":8}""")]);
            File.WriteAllText(Path.Combine(shots, "painterly.txt"), sb.ToString());
        }
        var m8 = BgCases.Measure(best!, c.Subject);
        Assert.True(m8.BgIoU >= 0.95 && m8.SubjIoU >= 0.95, $"simplify=8: {m8.BgIoU:F4}/{m8.SubjIoU:F4}");
        Assert.True(m8.BgIoU > rb + 0.02 && m8.BgIoU > co + 0.02, $"no gain: {m8.BgIoU:F4} vs {rb:F4}/{co:F4}");
    }

    [Fact]
    public void Simplify8_OnTheStandardCases_IsReportedAgainstTheDefaults()
    {
        var sb = new StringBuilder();
        foreach (var c in BgCases.All())
        {
            var d = BgCases.Measure(BgCases.Run("removeBackground", c.Image), c.Subject);
            var s = BgCases.Measure(BgCases.Run("removeBackground", c.Image, """{"simplify":8}"""), c.Subject);
            sb.AppendLine($"{c.Name,-24} default bg {d.BgIoU:F4} subj {d.SubjIoU:F4} | simplify=8 bg {s.BgIoU:F4} subj {s.SubjIoU:F4}");
        }
        output.WriteLine(sb.ToString());   // informational: the mode is opt-in and aimed at painted pictures, so no floor is asserted here
        var flat = BgCases.All().First(x => x.Name == "flat_antialiased");
        var m = BgCases.Measure(BgCases.Run("removeBackground", flat.Image, """{"simplify":8}"""), flat.Subject);
        Assert.True(m.BgIoU > 0.97 && m.SubjIoU > 0.97, $"{m.BgIoU:F4}/{m.SubjIoU:F4}");
    }

    [Fact]
    public void SimplifiedMode_KeepsOriginalColors_OfWhatIsLeft()
    {
        var c = BgCases.Painterly();
        var r = BgCases.Run("removeBackground", c.Image, """{"simplify":8,"defringe":false}""");
        for (int i = 0; i < r.Pixels.Length; i++)
            if (!Pixel.IsEmpty(r.Pixels[i])) Assert.Equal(c.Image.Pixels[i], r.Pixels[i]);   // the mask is applied to the original, nothing is recolored
    }

    [Fact]
    public void SimplifiedMode_EnclosedOff_LeavesPatches_AndMinSizeIsHonored()
    {
        var c = BgCases.Painterly();
        int inner = 66 * 128 + 112;                                    // the middle of the reins loop
        Assert.True(Pixel.IsEmpty(BgCases.Run("removeBackground", c.Image, """{"simplify":8}""").Pixels[inner]));
        Assert.False(Pixel.IsEmpty(BgCases.Run("removeBackground", c.Image, """{"simplify":8,"enclosed":"off"}""").Pixels[inner]));
        Assert.False(Pixel.IsEmpty(BgCases.Run("removeBackground", c.Image, """{"simplify":8,"enclosedMin":100000}""").Pixels[inner]));
    }

    [Fact]
    public void SimplifiedMode_IsOffByDefault_AndSafeOnTransparentAndTinyInput()
    {
        var c = BgCases.All().First(x => x.Name == "flat_antialiased");
        Assert.Equal(BgCases.Run("removeBackground", c.Image).Pixels, BgCases.Run("removeBackground", c.Image, """{"simplify":0}""").Pixels);
        Assert.Equal(0, BgCases.Run("removeBackground", new PixelImage(8, 8), """{"simplify":8}""").OpaqueCount());
        BgCases.Run("cutout", new PixelImage(2, 2), """{"simplify":8}""");
        BgCases.Run("removeBackground", new PixelImage(1, 1), """{"simplify":8}""");
    }
}
