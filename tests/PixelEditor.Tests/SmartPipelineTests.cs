// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using PixelEditor.Core;
using PixelEditor.Core.Ops;

namespace PixelEditor.Tests;

public class CutoutTests
{
    private static PixelImage GradientBackgroundWithSubject()
    {
        // background: a smooth gradient from blue to light blue; subject: a red ellipse with a dark outline
        var img = new PixelImage(80, 60);
        for (int y = 0; y < 60; y++)
            for (int x = 0; x < 80; x++)
                img.Set(x, y, Pixel.Rgb((byte)(40 + x / 4), (byte)(90 + y), (byte)(200 - x / 3)));
        for (int y = 0; y < 60; y++)
            for (int x = 0; x < 80; x++)
            {
                double d = Math.Sqrt((x - 40) * (x - 40) + (y - 30) * (y - 30) * 1.4);
                if (d < 18) img.Set(x, y, Pixel.Rgb(30, 10, 10));
                if (d < 16) img.Set(x, y, Pixel.Rgb(210, 50, 40));
            }
        return img;
    }

    [Fact]
    public void Cutout_RemovesGradientBackground_KeepsSubjectAndItsOutline()
    {
        var r = Cutout.Remove(GradientBackgroundWithSubject(), clusters: 5, tolerance: 40, edgeStop: 45, keepLargest: true);
        Assert.True(Pixel.IsEmpty(r.Pixels[0]));
        Assert.True(Pixel.IsEmpty(r.Pixels[59 * 80 + 79]));
        Assert.Equal(Pixel.Rgb(210, 50, 40), r.Pixels[30 * 80 + 40]); // the center of the subject
        Assert.Equal(Pixel.Rgb(30, 10, 10), r.Pixels[30 * 80 + 40 + 17]); // the outline
        int subject = r.Pixels.Count(p => !Pixel.IsEmpty(p));
        Assert.InRange(subject, 600, 1200); // about the ellipse area
    }

    [Fact]
    public void RemoveBackground_Follow_ErasesAGradientThatPlainToleranceLeaves()
    {
        var img = GradientBackgroundWithSubject();
        // With a named background color the model is that single color (without a color the border clusters already cover much of a gradient)
        uint bg = img.Pixels[0];
        var plain = Background.Remove(img, bg, 24, true);
        int left = plain.Pixels.Count(p => !Pixel.IsEmpty(p));
        Assert.True(left > 2500);                                   // the plain tolerance leaves most of the gradient

        // limit is a perceptual distance now (the far corner of this gradient is about 130 units from the named color)
        var followed = Background.Remove(img, bg, 24, true, follow: 8, limit: 180);
        Assert.True(Pixel.IsEmpty(followed.Pixels[0]));
        Assert.True(Pixel.IsEmpty(followed.Pixels[59 * 80 + 79]));
        Assert.Equal(Pixel.Rgb(210, 50, 40), followed.Pixels[30 * 80 + 40]);            // the subject and its outline are intact
        Assert.Equal(Pixel.Rgb(30, 10, 10), followed.Pixels[30 * 80 + 40 + 17]);
        Assert.InRange(followed.Pixels.Count(p => !Pixel.IsEmpty(p)), 600, 1200);
    }

    [Fact]
    public void Cutout_KeepRect_ProtectsSubjectAndKeepLargestDropsDebris()
    {
        var img = GradientBackgroundWithSubject();
        // a red junk dot far from the subject, the color is not like the background
        img.Set(3, 3, Pixel.Rgb(220, 220, 40));
        img.Set(4, 3, Pixel.Rgb(220, 220, 40));
        var r = Cutout.Remove(img, 5, 40, 45, keepLargest: true);
        Assert.True(Pixel.IsEmpty(r.Pixels[3 * 80 + 3])); // the separate junk is removed

        var kept = Cutout.Remove(img, 5, 40, 45, keepLargest: false, new PixelRect(0, 0, 10, 10));
        Assert.False(Pixel.IsEmpty(kept.Pixels[3 * 80 + 3])); // inside keepRect pixels are not counted as background
    }

    [Fact]
    public void CutoutOp_IsRegistered_AndParsesKeepRect()
    {
        var img = GradientBackgroundWithSubject();
        var json = System.Text.Json.JsonDocument.Parse("""{"clusters":5,"tolerance":40,"keepLargest":false,"keepRect":[0,0,5,5]}""");
        var r = Operations.Get("cutout").Run(img, new OpArgs(json.RootElement, null));
        Assert.False(Pixel.IsEmpty(r.Pixels[2 * 80 + 2]));
    }
}

public class SmartResizeAndLabTests
{
    private static readonly uint Black = Pixel.Rgb(0, 0, 0), White = Pixel.Rgb(255, 255, 255);

    [Fact]
    public void Smart_KeepsThinDarkLineCrisp_WhereBoxBlursIt()
    {
        var img = new PixelImage(16, 16);
        for (int i = 0; i < img.Pixels.Length; i++) img.Pixels[i] = White;
        for (int x = 0; x < 16; x++) { img.Set(x, 7, Black); img.Set(x, 8, Black); } // a line 2 px thick
        var box = Resampler.Resize(img, 4, 4, ResizeMethod.Box);
        var smart = Resampler.Resize(img, 4, 4, ResizeMethod.Smart);
        Assert.Contains(box.Pixels, p => p != Black && p != White);       // averaging gave gray
        Assert.All(smart.Pixels, p => Assert.True(p == Black || p == White)); // smart downscale: only the source colors
        Assert.Contains(Black, smart.Pixels);                              // the line did not disappear
    }

    [Fact]
    public void Smart_TransparentBlocksStayTransparent_AndMethodNameParses()
    {
        var img = new PixelImage(8, 8);
        img.Set(0, 0, White);
        var r = Resampler.Resize(img, 2, 2, ResizeMethod.Smart);
        Assert.True(Pixel.IsEmpty(r.Pixels[3]));
        var json = System.Text.Json.JsonDocument.Parse("""{"scale":0.5,"method":"smart"}""");
        Assert.Equal(4, Operations.Get("resize").Run(new PixelImage(8, 8), new OpArgs(json.RootElement, null)).Width);
    }

    [Fact]
    public void LabQuantize_KeepsSmallAccentColor_ThatRgbMedianCutMerges()
    {
        // a big green area with a gradient and a small bright red accent
        var img = new PixelImage(40, 40);
        for (int y = 0; y < 40; y++)
            for (int x = 0; x < 40; x++) img.Set(x, y, Pixel.Rgb(20, (byte)(100 + x), (byte)(60 + y)));
        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 3; x++) img.Set(5 + x, 5 + y, Pixel.Rgb(230, 30, 40));

        var lab = PerceptualQuantizer.BuildPalette(img, 4);
        Assert.Contains(lab, c => Pixel.R(c) > 180 && Pixel.G(c) < 80); // the red accent is in the palette
        var applied = PerceptualQuantizer.Apply(img, lab, dither: false);
        Assert.True(applied.DistinctColors() <= 4);
        Assert.True(Pixel.R(applied.Pixels[6 * 40 + 6]) > 180);
    }

    [Fact]
    public void QuantizeAndApplyPalette_AcceptSpaceParameter()
    {
        var img = new PixelImage(10, 10);
        for (int i = 0; i < 100; i++) img.Pixels[i] = Pixel.Rgb((byte)(i * 2), (byte)(255 - i * 2), (byte)(i % 7 * 30));
        var q = System.Text.Json.JsonDocument.Parse("""{"colors":5,"space":"lab"}""");
        Assert.True(Operations.Get("quantize").Run(img, new OpArgs(q.RootElement, null)).DistinctColors() <= 5);
        var p = System.Text.Json.JsonDocument.Parse("""{"palette":["#FF0000","#0000FF"],"space":"lab"}""");
        Assert.All(Operations.Get("applyPalette").Run(img, new OpArgs(p.RootElement, null)).Pixels,
            c => Assert.True(c == Pixel.Rgb(255, 0, 0) || c == Pixel.Rgb(0, 0, 255)));
    }
}

public class ShadedOutlineTests
{
    [Fact]
    public void ShadeOutline_UsesDarkerShadeOfNeighbor_NotOneFlatColor()
    {
        var img = new PixelImage(8, 4);
        for (int y = 1; y < 3; y++)
        {
            for (int x = 1; x < 4; x++) img.Set(x, y, Pixel.Rgb(220, 60, 60));   // a red subject
            for (int x = 4; x < 7; x++) img.Set(x, y, Pixel.Rgb(60, 60, 220));   // a blue subject
        }
        var r = Outline.Apply(img, 0, OutlineMode.Outside, diagonal: false, thickness: 1, shadeDarken: 0.6);
        var above = r.Pixels[0 * 8 + 2]; // above the red
        var aboveBlue = r.Pixels[0 * 8 + 5];
        Assert.False(Pixel.IsEmpty(above));
        Assert.True(Pixel.R(above) > Pixel.B(above));     // the hue is red
        Assert.True(Pixel.B(aboveBlue) > Pixel.R(aboveBlue)); // the hue is blue
        Assert.True(Pixel.R(above) < 220);                // darker than the original
        Assert.NotEqual(above, aboveBlue);
    }

    [Fact]
    public void OutlineOp_ShadeFlag_Works_AndFlatColorStillDefault()
    {
        var img = new PixelImage(5, 5);
        img.Set(2, 2, Pixel.Rgb(200, 120, 40));
        var shaded = Operations.Get("outline").Run(img, new OpArgs(System.Text.Json.JsonDocument.Parse("""{"shade":true,"darken":0.5}""").RootElement, null));
        var flat = Operations.Get("outline").Run(img, new OpArgs(System.Text.Json.JsonDocument.Parse("""{"color":"#000000"}""").RootElement, null));
        Assert.Equal(7, shaded.Width); // auto field
        var n = shaded.Pixels.Where(p => p != Pixel.Rgb(200, 120, 40) && !Pixel.IsEmpty(p)).ToList();
        Assert.NotEmpty(n);
        Assert.All(n, c => Assert.True(Pixel.R(c) > Pixel.B(c))); // dark orange, not black
        Assert.Contains(Pixel.Rgb(0, 0, 0), flat.Pixels);
    }
}

public class PolygonMaskTests
{
    [Fact]
    public void PolygonMask_KeepsInside_AndKeepOutsideCutsHole()
    {
        var img = new PixelImage(20, 20);
        for (int i = 0; i < img.Pixels.Length; i++) img.Pixels[i] = Pixel.Rgb(200, 100, 50);
        var inside = ShapeMask.Apply(img, [(5, 5), (14, 5), (14, 14), (5, 14)]);
        Assert.False(Pixel.IsEmpty(inside.Pixels[10 * 20 + 10]));
        Assert.True(Pixel.IsEmpty(inside.Pixels[0]));
        var hole = ShapeMask.Apply(img, [(5, 5), (14, 5), (14, 14), (5, 14)], keepOutside: true);
        Assert.True(Pixel.IsEmpty(hole.Pixels[10 * 20 + 10]));
        Assert.False(Pixel.IsEmpty(hole.Pixels[0]));
        Assert.Throws<ArgumentException>(() => ShapeMask.Apply(img, [(1, 1), (2, 2)]));
    }

    [Fact]
    public void PolygonMaskOp_ParsesPoints()
    {
        var img = new PixelImage(10, 10);
        for (int i = 0; i < 100; i++) img.Pixels[i] = Pixel.Rgb(1, 2, 3);
        var json = System.Text.Json.JsonDocument.Parse("""{"points":[[2,2],[7,2],[7,7],[2,7]]}""");
        var r = Operations.Get("polygonMask").Run(img, new OpArgs(json.RootElement, null));
        Assert.False(Pixel.IsEmpty(r.Pixels[4 * 10 + 4]));
        Assert.True(Pixel.IsEmpty(r.Pixels[0]));
    }
}

public class PillowStyleFilterTests
{
    private static PixelImage NoisyHalves()
    {
        // two flat halves (dark left, light right) with salt-and-pepper noise
        var img = new PixelImage(30, 20);
        var rnd = new Random(7);
        for (int y = 0; y < 20; y++)
            for (int x = 0; x < 30; x++)
                img.Set(x, y, x < 15 ? Pixel.Rgb(40, 40, 60) : Pixel.Rgb(220, 210, 200));
        for (int i = 0; i < 25; i++) img.Set(rnd.Next(1, 29), rnd.Next(1, 19), rnd.Next(2) == 0 ? Pixel.Rgb(255, 255, 255) : Pixel.Rgb(0, 0, 0));
        return img;
    }

    [Fact]
    public void Median_RemovesSaltAndPepperNoise_ButKeepsTheSharpEdge()
    {
        var img = NoisyHalves();
        var r = Filters.Median(img, 1);
        int noisyBefore = img.Pixels.Count(p => p == Pixel.Rgb(255, 255, 255) || p == Pixel.Rgb(0, 0, 0));
        int noisyAfter = r.Pixels.Count(p => p == Pixel.Rgb(255, 255, 255) || p == Pixel.Rgb(0, 0, 0));
        Assert.True(noisyBefore > 15);
        Assert.True(noisyAfter < noisyBefore / 4);
        Assert.Equal(Pixel.Rgb(40, 40, 60), r.Get(5, 10));              // the flat areas are intact
        Assert.Equal(Pixel.Rgb(220, 210, 200), r.Get(25, 10));
        Assert.Equal(Pixel.Rgb(40, 40, 60), r.Get(14, 10));             // and the edge did not blur into a gradient
        Assert.Equal(Pixel.Rgb(220, 210, 200), r.Get(15, 10));
    }

    [Fact]
    public void Median_LeavesTransparencyAlone()
    {
        var img = new PixelImage(5, 5);
        img.Set(2, 2, Pixel.Rgb(10, 20, 30));
        var r = Filters.Median(img, 1);
        Assert.Equal(Pixel.Rgb(10, 20, 30), r.Get(2, 2));
        Assert.Equal(1, r.Pixels.Count(p => !Pixel.IsEmpty(p)));
    }

    [Fact]
    public void Edges_FindsTheStepAsAOnePixelLine_AndOnlyLinesLeavesNothingElse()
    {
        var img = new PixelImage(30, 20);
        for (int y = 0; y < 20; y++) for (int x = 0; x < 30; x++) img.Set(x, y, x < 15 ? Pixel.Rgb(20, 20, 20) : Pixel.Rgb(235, 235, 235));
        var lines = Filters.EdgeLines(img, 40, 0xFFFF0000, thin: true, onlyLines: true);
        var opaque = lines.Pixels.Select((p, i) => (p, i)).Where(t => !Pixel.IsEmpty(t.p)).Select(t => t.i % 30).Distinct().ToList();
        Assert.True(opaque.Count <= 2 && opaque.All(x => x is 14 or 15));    // a line of one or two pixels exactly at the step
        Assert.True(lines.Pixels.Count(p => !Pixel.IsEmpty(p)) >= 20);
        var overlay = Filters.EdgeLines(img, 40, 0xFFFF0000, thin: true, onlyLines: false);
        Assert.Equal(img.Get(3, 3), overlay.Get(3, 3));                       // the picture stays under the lines
        var plain = new PixelImage(8, 8);
        for (int i = 0; i < plain.Pixels.Length; i++) plain.Pixels[i] = Pixel.Rgb(9, 9, 9);
        var flat = Filters.EdgeLines(plain, 10, 0xFFFF0000, true, true);
        Assert.All(flat.Pixels, p => Assert.True(Pixel.IsEmpty(p)));          // a flat picture has no edges
    }

    [Fact]
    public void AutoLevelsCutoff_IgnoresStrayPixelsWhenMeasuringTheRange()
    {
        var img = new PixelImage(20, 20);
        for (int y = 0; y < 20; y++) for (int x = 0; x < 20; x++) img.Set(x, y, Pixel.Rgb((byte)(100 + x), (byte)(100 + x), (byte)(100 + x)));
        img.Set(0, 0, Pixel.Rgb(0, 0, 0));
        img.Set(19, 19, Pixel.Rgb(255, 255, 255));
        var plain = ColorOps.AutoLevels(img);
        var cut = ColorOps.AutoLevels(img, 2);
        Assert.True(Pixel.R(cut.Get(5, 5)) < Pixel.R(plain.Get(5, 5)) || Pixel.R(cut.Get(15, 5)) > Pixel.R(plain.Get(15, 5)));   // the real range is stretched harder
    }

    [Fact]
    public void CutoutDistance_CutsADarkSubjectOutOfAGradientBackground_ThatTheFloodModeCanMiss()
    {
        // a vignette: bright in the middle, darker toward the border; a dark subject with soft edges in the middle
        var img = new PixelImage(90, 70);
        for (int y = 0; y < 70; y++)
            for (int x = 0; x < 90; x++)
            {
                double edge = Math.Min(Math.Min(x, 89 - x) / 45.0, Math.Min(y, 69 - y) / 35.0);
                byte g = (byte)(120 + 100 * Math.Min(1, edge * 2));
                img.Set(x, y, Pixel.Rgb(g, g, (byte)(g + 8)));
                double d = Math.Sqrt((x - 45) * (x - 45) / 1.6 + (y - 35) * (y - 35));
                if (d < 16) { byte s = (byte)(50 + d * 3); img.Set(x, y, Pixel.Rgb(s, (byte)(s / 2), (byte)(s / 3))); }
            }
        var r = Cutout.RemoveByDistance(img, 4, 0, 1, true, null, follow: 3);
        Assert.True(Pixel.IsEmpty(r.Get(2, 2)));
        Assert.True(Pixel.IsEmpty(r.Get(87, 67)));
        Assert.True(Pixel.IsEmpty(r.Get(45, 3)));
        Assert.False(Pixel.IsEmpty(r.Get(45, 35)));
        Assert.False(Pixel.IsEmpty(r.Get(40, 32)));
        int kept = r.Pixels.Count(p => !Pixel.IsEmpty(p));
        Assert.InRange(kept, 500, 1100);                                      // about the ellipse area (~ pi*16*25 = 1250 at most)
    }
}
