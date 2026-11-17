// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using PixelEditor.Core;
using PixelEditor.Core.Ops;

namespace PixelEditor.Tests;

public class SheetTemplateTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pixed-sheet-tpl-" + Guid.NewGuid().ToString("N"));
    private SheetTemplateLibrary Lib => new(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void BuiltIns_AreWellFormed_AndKnowWhichFitRsi()
    {
        var all = BuiltInSheetTemplates.All;
        Assert.All(all, t => { Assert.True(t.IsSystem); Assert.NotEmpty(t.Rows); Assert.NotEmpty(t.Frames); Assert.All(t.Frames, f => Assert.InRange(f.Delay, 0.001, 60)); });
        Assert.Equal(all.Count, all.Select(t => t.Id).Distinct().Count());
        var walk = all.First(t => t.Id == "system:walk-4");
        Assert.Equal(["стоит", "шаг 1", "шаг 2"], walk.Frames.Select(f => f.Name));
        Assert.True(walk.IsRsiCompatible);
        Assert.False(all.First(t => t.Id == "system:platformer").IsRsiCompatible); // the rows idle, run… are not directions
        Assert.True(all.First(t => t.Id == "system:walk-8").IsRsiCompatible);
    }

    [Fact]
    public void Save_FindDelete_UserTemplate_AndRejectBadInput()
    {
        var saved = Lib.Save("Моя ходьба", "для героя", ["S", "N", "E", "W"], [new SheetFrame("стоит", 0.5), new SheetFrame("шаг", 0.1)]);
        Assert.False(saved.IsSystem);
        Assert.StartsWith("user:", saved.Id);
        Assert.Equal(["стоит", "шаг"], Lib.Find("моя ходьба")!.Frames.Select(f => f.Name));
        Assert.Equal(BuiltInSheetTemplates.All.Count + 1, Lib.All().Count);

        Assert.Throws<ArgumentException>(() => Lib.Save("", "", ["a"], [new SheetFrame("x", 1)]));
        Assert.Throws<ArgumentException>(() => Lib.Save("X", "", [], [new SheetFrame("x", 1)]));
        Assert.Throws<ArgumentException>(() => Lib.Save("X", "", ["a", "A"], [new SheetFrame("x", 1)]));
        Assert.Throws<ArgumentException>(() => Lib.Save("X", "", ["a"], [new SheetFrame("x", 0)]));
        Assert.Throws<ArgumentException>(() => Lib.Save("Моя ходьба", "", ["a"], [new SheetFrame("x", 1)])); // taken
        Assert.Throws<ArgumentException>(() => Lib.Save("Один кадр", "", ["a"], [new SheetFrame("x", 1)]));  // taken by a system one

        Assert.True(Lib.Delete(saved.Id));
        Assert.Null(Lib.Find("Моя ходьба"));
        Assert.False(Lib.Delete("system:walk-4"));
    }

    [Fact]
    public void Update_KeepsFile_AndBrokenFilesAreSkipped()
    {
        var t = Lib.Save("A", "", ["x"], [new SheetFrame("k", 1)]);
        var upd = Lib.Save("A2", "новое", ["x", "y"], [new SheetFrame("k", 1)], t.Id);
        Assert.Equal(t.FilePath, upd.FilePath);
        Assert.Single(Lib.All().Where(x => !x.IsSystem));
        File.WriteAllText(Path.Combine(_dir, "broken.json"), "{ not json");
        Assert.Single(Lib.All().Where(x => !x.IsSystem));
    }
}

public class RotationTests
{
    private static readonly uint Red = Pixel.Rgb(255, 0, 0);

    private static PixelImage Bar()
    {
        var img = new PixelImage(9, 9);
        for (int x = 1; x < 8; x++) img.Set(x, 4, Red);   // a horizontal stroke in the center
        return img;
    }

    [Fact]
    public void Rotate90_TurnsHorizontalBarVertical_ClockwiseAndWithoutNewColors()
    {
        var r = Rotation.Rotate(Bar(), 90, smooth: false);
        Assert.Equal(Red, r.Pixels[1 * 9 + 4]);
        Assert.Equal(Red, r.Pixels[7 * 9 + 4]);
        Assert.True(Pixel.IsEmpty(r.Pixels[4 * 9 + 1]));
        Assert.All(Rotation.Rotate(Bar(), 37, smooth: true).Pixels, p => Assert.True(Pixel.IsEmpty(p) || p == Red));
    }

    [Fact]
    public void Rotate_Expand_GrowsCanvas_AndNonExpandKeepsSize_AndZeroIsCopy()
    {
        var img = new PixelImage(10, 4);
        for (int i = 0; i < img.Pixels.Length; i++) img.Pixels[i] = Red;
        var grown = Rotation.Rotate(img, 45, smooth: false, expand: true);
        Assert.True(grown.Height > 4 && grown.Width >= 9); // 10×4 at 45° takes about 10×10
        var same = Rotation.Rotate(img, 45, smooth: false, expand: false);
        Assert.Equal((10, 4), (same.Width, same.Height));
        Assert.Equal(img.Pixels, Rotation.Rotate(img, 360).Pixels);
        Assert.Equal(img.Pixels, Rotation.Rotate(img, 0).Pixels);
    }

    [Fact]
    public void Rotate_MinusAngle_IsCounterClockwise_AndRotateOpWorks()
    {
        var img = new PixelImage(9, 9);
        for (int x = 4; x < 8; x++) img.Set(x, 4, Red); // a small stroke to the right of the center
        var cw = Rotation.Rotate(img, 90, smooth: false);
        var ccw = Rotation.Rotate(img, -90, smooth: false);
        Assert.Equal(Red, cw.Pixels[7 * 9 + 4]);   // clockwise: right becomes down
        Assert.Equal(Red, ccw.Pixels[1 * 9 + 4]);  // counterclockwise: right becomes up
        var json = System.Text.Json.JsonDocument.Parse("""{"angle":90,"smooth":false}""");
        Assert.Equal(cw.Pixels, Operations.Get("rotate").Run(img, new OpArgs(json.RootElement, null)).Pixels);
    }
}
