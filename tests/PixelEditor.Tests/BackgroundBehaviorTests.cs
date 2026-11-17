// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using PixelEditor.Core;

namespace PixelEditor.Tests;

public class BackgroundBehaviorTests
{
    private static BgCase Case(string name) => BgCases.All().First(c => c.Name == name);

    [Fact]
    public void PixelArt_StaysPixelExact_AndEyeWhitesStay()
    {
        var c = Case("pixel_art_exact");
        var r = BgCases.Run("removeBackground", c.Image);
        for (int i = 0; i < r.Pixels.Length; i++)
        {
            if (c.Subject[i]) Assert.Equal(c.Image.Pixels[i], r.Pixels[i]);   // every subject pixel (eye whites included) untouched
            else Assert.True(Pixel.IsEmpty(r.Pixels[i]));
        }
    }

    [Fact]
    public void ToleranceZero_MeansExactColor()
    {
        var img = new PixelImage(6, 1);
        for (int i = 0; i < 6; i++) img.Pixels[i] = Pixel.Rgb(200, 200, 200);
        img.Pixels[2] = Pixel.Rgb(201, 200, 200);
        var r = BgCases.Run("removeBackground", img, """{"tolerance":0}""");
        Assert.True(Pixel.IsEmpty(r.Pixels[0]) && Pixel.IsEmpty(r.Pixels[1]));
        Assert.False(Pixel.IsEmpty(r.Pixels[2]));       // one level off is not the background any more
        Assert.True(Pixel.IsEmpty(r.Pixels[3]) && Pixel.IsEmpty(r.Pixels[5]));   // the right side is reached from the right edge
    }

    [Fact]
    public void Enclosed_RingHoleIsErasedByDefault_ButCanBeKept()
    {
        var c = Case("enclosed_ring");
        Assert.True(Pixel.IsEmpty(BgCases.Run("removeBackground", c.Image).Pixels[40 * 96 + 48]));
        Assert.False(Pixel.IsEmpty(BgCases.Run("removeBackground", c.Image, """{"enclosed":"off"}""").Pixels[40 * 96 + 48]));
    }

    [Fact]
    public void Defringe_CanBeTurnedOff_AndRemovesTheBlendRim()
    {
        var c = Case("flat_antialiased");
        var on = BgCases.Measure(BgCases.Run("removeBackground", c.Image), c.Subject);
        var off = BgCases.Measure(BgCases.Run("removeBackground", c.Image, """{"defringe":false}"""), c.Subject);
        Assert.True(off.Left > on.Left + 20, $"rim pixels left: off={off.Left} on={on.Left}");
    }

    [Fact]
    public void Operations_DoNotMutateInput()
    {
        var c = Case("noisy_flat_jpeg");
        var before = (uint[])c.Image.Pixels.Clone();
        BgCases.Run("removeBackground", c.Image);
        BgCases.Run("cutout", c.Image);
        Assert.Equal(before, c.Image.Pixels);
    }

    [Theory]
    [InlineData("flat_antialiased")]
    [InlineData("noisy_flat_jpeg")]
    [InlineData("gradient_linear")]
    [InlineData("gradient_radial")]
    [InlineData("soft_shadow")]
    [InlineData("two_tone_floor_wall")]
    [InlineData("subject_touches_border")]
    [InlineData("enclosed_ring")]
    public void Cutout_ConvergesInOneCall_SecondRunChangesNothingMeaningful(string name)
    {
        var c = Case(name);
        var once = BgCases.Run("cutout", c.Image);
        var twice = BgCases.Run("cutout", once);
        int changed = 0;
        for (int i = 0; i < once.Pixels.Length; i++) if (once.Pixels[i] != twice.Pixels[i]) changed++;
        Assert.True(changed <= once.Pixels.Length / 500, $"{name}: {changed} pixels changed on the second run");
    }

    [Fact]
    public void Cutout_SubjectTouchingTheBorder_IsKept()
    {
        var c = Case("subject_touches_border");
        var m = BgCases.Measure(BgCases.Run("cutout", c.Image), c.Subject);
        Assert.True(m.SubjIoU > 0.98);      // before the rewrite the subject was taken for background (IoU 0)
    }

    [Fact]
    public void Wand_UsesPerceptualDistance_ToleranceZeroIsExact()
    {
        var l = new Layer(4, 1, "t");
        l.Pixels[0] = Pixel.Rgb(100, 100, 100); l.Pixels[1] = Pixel.Rgb(101, 100, 100); l.Pixels[2] = Pixel.Rgb(100, 100, 100); l.Pixels[3] = Pixel.Rgb(100, 100, 130);
        Assert.Equal(2, SelectionMask.Wand(l, 0, 0, 0, false)!.Count);          // exact only
        Assert.Equal(3, SelectionMask.Wand(l, 0, 0, 6, false)!.Count);          // a one-level step is within a small tolerance, a blue step of 30 is not
        Assert.Equal(4, SelectionMask.Wand(l, 0, 0, 60, false)!.Count);
    }

    [Fact]
    public void Wand_FollowSmooth_SelectsGradientAndStopsAtSharpEdge()
    {
        var l = new Layer(60, 10, "t");
        for (int y = 0; y < 10; y++)
            for (int x = 0; x < 60; x++)
                l.Pixels[y * 60 + x] = (x >= 50 ? Pixel.Rgb(200, 30, 30) : Pixel.Rgb((byte)(60 + x * 2), (byte)(60 + x * 2), (byte)(120 + x)));
        var plain = SelectionMask.Wand(l, 0, 5, 6, true)!;
        var follow = SelectionMask.Wand(l, 0, 5, 6, true, follow: true)!;
        Assert.True(plain.Count < 200);                                          // a fixed tolerance around the clicked color gets only the start
        Assert.True(follow.Contains(15, 5) && follow.Count > plain.Count * 3 && !follow.Contains(55, 5));   // following runs along the ramp (within the drift limit) and never crosses into the red block
    }
}
