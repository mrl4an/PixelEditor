// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using PixelEditor.Core;
using PixelEditor.Core.Ops;
using PixelEditor.Core.Plugins;

namespace PixelEditor.Tests;

// Types to test plugin loading: the test assembly plays the role of a plugin DLL.
public sealed class TestAlwaysFailCheck : IPixelCheck
{
    public string Name => "testAlwaysFail";
    public string Description => "всегда проваливается";
    public IReadOnlyList<string> Parameters => [];
    public CheckResult Run(PixelImage image, OpArgs args) => CheckResult.Fail("специально");
}

public sealed class TestFlipOperation : IPixelOperation
{
    public string Name => "testFlip";
    public string Description => "отражает по горизонтали";
    public IReadOnlyList<string> Parameters => [];
    public PixelImage Run(PixelImage input, OpArgs args) => Core.Ops.Transform.FlipH(input);
}

public class ChecksTests
{
    private static readonly uint Red = Pixel.Rgb(255, 0, 0);
    private static readonly uint Blue = Pixel.Rgb(0, 0, 255);

    private static CheckResult Run(string check, PixelImage img, string argsJson = "{}")
    {
        using var doc = System.Text.Json.JsonDocument.Parse(argsJson);
        return Checks.Get(check).Run(img, new OpArgs(doc.RootElement.Clone(), null));
    }

    [Fact]
    public void MaxColors_PassesAndFails()
    {
        var img = new PixelImage(2, 1, [Red, Blue]);
        Assert.True(Run("maxColors", img, """{"value":2}""").Passed);
        var fail = Run("maxColors", img, """{"value":1}""");
        Assert.False(fail.Passed);
        Assert.Contains("2", fail.Message);
    }

    [Fact]
    public void MissingRequiredParameter_Throws()
    {
        var img = new PixelImage(1, 1);
        Assert.Throws<ArgumentException>(() => Run("maxColors", img));
    }

    [Fact]
    public void NoIslands_DetectsStrayPixel()
    {
        var img = new PixelImage(6, 6);
        img.Set(0, 0, Red);
        for (int y = 3; y < 6; y++) for (int x = 3; x < 6; x++) img.Set(x, y, Red);
        Assert.False(Run("noIslands", img).Passed);
        Assert.True(Run("noIslands", Cleanup.RemoveIslands(img, 1)).Passed);
    }

    [Fact]
    public void OnlyPalette_ReportsForeignColors()
    {
        var img = new PixelImage(2, 1, [Red, Blue]);
        Assert.True(Run("onlyPalette", img, """{"palette":["#FF0000","#0000FF"]}""").Passed);
        var r = Run("onlyPalette", img, """{"palette":["#FF0000"]}""");
        Assert.False(r.Passed);
        Assert.Contains("#0000FF", r.Message);
    }

    [Fact]
    public void BinaryAlpha_And_Trimmed_And_Connected()
    {
        var soft = new PixelImage(1, 1, [Pixel.Argb(128, 1, 2, 3)]);
        Assert.False(Run("binaryAlpha", soft).Passed);

        var padded = new PixelImage(3, 3);
        padded.Set(1, 1, Red);
        Assert.False(Run("trimmed", padded).Passed);
        Assert.True(Run("trimmed", new PixelImage(1, 1, [Red])).Passed);

        var two = new PixelImage(5, 1, [Red, 0, 0, 0, Red]);
        Assert.False(Run("connected", two).Passed);
        Assert.True(Run("connected", new PixelImage(2, 1, [Red, Red])).Passed);
    }

    [Fact]
    public void Pipeline_ChecksFailAndRequiredFlag()
    {
        var img = new PixelImage(4, 4);
        for (int i = 0; i < 16; i++) img.Pixels[i] = i % 2 == 0 ? Red : Blue;

        var strict = Pipeline.Parse("""{"steps":[], "checks":[{"check":"maxColors","value":1}]}""").Run(img);
        Assert.True(strict.Ok);
        Assert.False(strict.ChecksPassed);
        Assert.False(strict.Success);

        var warn = Pipeline.Parse("""{"steps":[], "checks":[{"check":"maxColors","value":1,"required":false}]}""").Run(img);
        Assert.True(warn.ChecksPassed);
        Assert.False(warn.Checks[0].Passed);
    }

    [Fact]
    public void Pipeline_UnknownCheck_IsReportedNotThrown()
    {
        var res = Pipeline.Parse("""{"checks":[{"check":"nope"}]}""").Run(new PixelImage(1, 1));
        Assert.False(res.ChecksPassed);
        Assert.Contains("Неизвестная проверка", res.Checks[0].Message);
    }

    [Fact]
    public void Pipeline_StepError_SkipsChecks()
    {
        var res = Pipeline.Parse("""{"steps":[{"op":"nope"}], "checks":[{"check":"trimmed"}]}""").Run(new PixelImage(1, 1));
        Assert.False(res.Ok);
        Assert.Empty(res.Checks);
    }

    [Fact]
    public void Plugins_AreRegisteredAndUsableInPipeline()
    {
        var info = PluginLoader.RegisterAssembly(typeof(TestAlwaysFailCheck).Assembly, "tests");
        Assert.Contains("testAlwaysFail", info.Checks);
        Assert.Contains("testFlip", info.Operations);
        Assert.Null(info.Error);

        var img = new PixelImage(2, 1, [Red, Blue]);
        var res = Pipeline.Parse("""{"steps":[{"op":"testFlip"}], "checks":[{"check":"testAlwaysFail"}]}""").Run(img);
        Assert.Equal(Blue, res.Image.Pixels[0]);
        Assert.False(res.ChecksPassed);
        Assert.Equal("специально", res.Checks[0].Message);

        // registering the same names again gives a load error, not a crash
        var again = PluginLoader.RegisterAssembly(typeof(TestAlwaysFailCheck).Assembly, "tests-again");
        Assert.NotNull(again.Error);
    }
}
