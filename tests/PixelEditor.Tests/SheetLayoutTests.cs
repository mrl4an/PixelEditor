// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using PixelEditor.Core;
using PixelEditor.Core.Ops;

namespace PixelEditor.Tests;

/// <summary>The chat sheet prompt follows a required layout; the example inside is valid for any layout; omitted names and delays come from the layout.</summary>
public class SheetLayoutTests
{
    private static SheetLayoutSpec IdleAttack() => new(16, 24, ["idle", "attack"],
        [new SheetFrame("a", 0.5), new SheetFrame("b2", 0.25), new SheetFrame("windup", 0.1)]);

    private static SheetLayoutSpec Eight() =>
        SheetLayoutSpec.FromTemplate(BuiltInSheetTemplates.All.First(t => t.Id == "system:walk-8"), 20, 28);

    private static string ExampleOf(string prompt)
    {
        var json = PipelineText.Extract(prompt);
        Assert.True(PipelineText.Check(json).IsValid, string.Join("; ", PipelineText.Check(json).Errors));
        Assert.Equal(ChatKind.Sheet, PipelineText.Detect(json));
        return json;
    }

    public static IEnumerable<object[]> Layouts()
    {
        yield return new object[] { IdleAttack() };
        yield return new object[] { Eight() };
        yield return new object[] { SheetLayoutSpec.FromTemplate(BuiltInSheetTemplates.All.First(t => t.Id == "system:platformer"), 8, 8) };
        yield return new object[] { SheetLayoutSpec.FromTemplate(BuiltInSheetTemplates.All.First(t => t.Id == "system:effect"), 1, 1) };           // a 1x1 frame
        yield return new object[] { SheetLayoutSpec.FromTemplate(BuiltInSheetTemplates.All.First(t => t.Id == "system:still"), 64, 48) };
        yield return new object[] { new SheetLayoutSpec(32, 32, ["вперёд", "назад"], [new SheetFrame("стоит", 0.4), new SheetFrame("шаг, раз", 0.15)]) };  // non-ASCII names, a comma
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void TheExampleInTheRequiredPrompt_IsValid_AndHasExactlyTheRequestedLayout(SheetLayoutSpec layout)
    {
        var prompt = ChatPrompt.Build(ChatKind.Sheet, sheetLayout: layout);
        var result = SheetScene.Render(ExampleOf(prompt));
        Assert.Equal((layout.FrameWidth, layout.FrameHeight), (result.FrameWidth, result.FrameHeight));
        Assert.Equal(layout.Rows, result.RowNames);
        Assert.Equal(layout.Frames.Select(f => f.Name), result.FrameNames);
        for (int r = 0; r < layout.Rows.Count; r++)
            Assert.Equal(layout.Frames.Select(f => f.Delay), result.Delays[r]);
        Assert.Empty(layout.Differences(result));
        Assert.Contains("REQUIRED layout", prompt);
        foreach (var f in layout.Frames) Assert.Contains(f.Name, prompt);
        foreach (var row in layout.Rows) Assert.Contains(row, prompt);
    }

    [Fact]
    public void TheRequiredPrompt_StatesSizeRowsFramesAndDelays_AndDropsTheClassicNswLine()
    {
        var prompt = ChatScenePrompt.Sheet(IdleAttack());
        Assert.Contains("\"frameSize\": [16,24]", prompt);
        Assert.Contains("\"idle\", \"attack\"", prompt);
        Assert.Contains("1. \"a\" (delay 0.5)", prompt);
        Assert.Contains("2. \"b2\" (delay 0.25)", prompt);
        Assert.Contains("3. \"windup\" (delay 0.1)", prompt);
        Assert.DoesNotContain("Row order for directions is S, N, E, W", prompt);
        Assert.DoesNotContain("walker", prompt);                                        // the classic example is replaced
        // the free prompt keeps the classic text
        var free = ChatScenePrompt.Sheet();
        Assert.Contains("Row order for directions is S, N, E, W", free);
        Assert.DoesNotContain("REQUIRED layout", free);
    }

    [Fact]
    public void Eight_DirectionPrompt_ListsAllRows_AndTheExampleMirrorsWFromE()
    {
        var layout = Eight();
        var prompt = ChatScenePrompt.Sheet(layout);
        Assert.Contains("\"SE\", \"SW\", \"NE\", \"NW\"", prompt);
        var json = ExampleOf(prompt);
        Assert.Contains("\"mirrorOf\":\"E\"", json.Replace(" ", ""));
        Assert.Equal(8, SheetScene.Render(json).Rows);
    }

    [Fact]
    public void PerRowDelaysThatDiffer_AreListedPerRow()
    {
        var l = IdleAttack() with { Delays = [[0.5, 0.25, 0.1], [0.2, 0.2, 0.2]] };
        Assert.False(l.UniformRows);
        var prompt = ChatScenePrompt.Sheet(l);
        Assert.Contains("delays in seconds per row", prompt);
        Assert.Contains("\"attack\": 0.2, 0.2, 0.2", prompt);
        Assert.Equal(0.2, SheetScene.Render(ExampleOf(prompt)).Delays[1][0]);
    }

    [Fact]
    public void ABindingGivesItsExactLayout()
    {
        var b = new RsiBinding("", "hero", 2, 3, 16, 24, [[0.5, 0.25, 0.1], [0.5, 0.25, 0.1]], ["idle", "attack"], ["a", "b2", ""]);
        var l = SheetLayoutSpec.FromBinding(b);
        Assert.Equal(["idle", "attack"], l.Rows);
        Assert.Equal(["a", "b2", "3"], l.Frames.Select(f => f.Name));            // an unnamed frame is its number
        Assert.Equal([0.5, 0.25, 0.1], l.Frames.Select(f => f.Delay));
        Assert.True(l.UniformRows);

        var rsi = SheetLayoutSpec.FromBinding(new RsiBinding("x.rsi", "walk", 4, 2, 32, 32, RsiState.UniformDelays(4, 2, 0.2)));
        Assert.Equal(["S", "N", "E", "W"], rsi.Rows);
    }

    [Fact]
    public void Complete_TakesOmittedDelaysAndNamesFromTheLayout_ButKeepsWhatTheAnswerGives()
    {
        var layout = IdleAttack();
        // delays: the first frame has its own, the others are omitted; names: only the first is given
        var raw = SheetScene.Render("""
            {"frameSize":[16,24],"rows":[
              {"name":"idle","frames":[{"name":"a","delay":0.9,"items":[]},{"items":[]},{"items":[]}]},
              {"name":"attack","copyOf":"idle"}]}
            """);
        Assert.False(raw.ExplicitDelays![0][1]);
        Assert.True(raw.ExplicitDelays[0][0]);
        var done = layout.Complete(raw);
        Assert.Equal([0.9, 0.25, 0.1], done.Delays[0]);                       // explicit wins, omitted come from the layout
        Assert.Equal([0.9, 0.25, 0.1], done.Delays[1]);                       // the copied row inherits the same facts
        Assert.Equal(new string?[] { "a", "b2", "windup" }, done.FrameNames);
        Assert.Equal(raw.Image.Pixels, done.Image.Pixels);                    // pixels untouched
    }

    [Fact]
    public void ASheetLevelDelay_CountsAsGiven()
    {
        var raw = SheetScene.Render("""{"frameSize":[8,8],"delay":0.7,"rows":[{"name":"idle","frames":[{"items":[]}]}]}""");
        Assert.True(raw.ExplicitDelays![0][0]);
        var done = new SheetLayoutSpec(8, 8, ["idle"], [new SheetFrame("x", 0.1)]).Complete(raw);
        Assert.Equal(0.7, done.Delays[0][0]);
    }

    [Fact]
    public void Differences_ReportExpectedAndReceived()
    {
        var layout = IdleAttack();
        var raw = SheetScene.Render("""
            {"frameSize":[8,8],"rows":[{"name":"S","frames":[{"name":"x","items":[]},{"name":"y","items":[]}]}]}
            """);
        var diff = layout.Differences(raw);
        Assert.Equal(3, diff.Count);
        Assert.Contains(diff, d => d.Contains("16×24") && d.Contains("8×8"));
        Assert.Contains(diff, d => d.Contains("idle, attack") && d.Contains("S"));
        Assert.Contains(diff, d => d.Contains('3') && d.Contains('2'));
        Assert.False(layout.FitsGrid(raw));

        var same = layout.Differences(SheetScene.Render(PipelineText.Extract(ChatScenePrompt.Sheet(layout))));
        Assert.Empty(same);
    }

    [Fact]
    public void FrameNameMismatch_IsReported_ButAnOmittedNameIsNot()
    {
        var layout = IdleAttack();
        string Sheet(string names) => "{\"frameSize\":[16,24],\"rows\":[{\"name\":\"idle\",\"frames\":[" + names + "]},{\"name\":\"attack\",\"copyOf\":\"idle\"}]}";
        var wrong = SheetScene.Render(Sheet("{\"name\":\"q\",\"items\":[]},{\"name\":\"b2\",\"items\":[]},{\"name\":\"windup\",\"items\":[]}"));
        Assert.Single(layout.Differences(wrong));
        var omitted = SheetScene.Render(Sheet("{\"items\":[]},{\"items\":[]},{\"items\":[]}"));
        Assert.Empty(layout.Differences(omitted));
    }

    [Fact]
    public void Parse_BuildsLayoutsFromCommandLineText()
    {
        Assert.Null(SheetLayoutSpec.Parse(null, null, null, null, null, null));
        var t = SheetLayoutSpec.Parse(null, "walk-4", null, null, "24x32", null)!;
        Assert.Equal((24, 32), (t.FrameWidth, t.FrameHeight));
        Assert.Equal(["S", "N", "E", "W"], t.Rows);
        Assert.Equal(3, t.Columns);

        var custom = SheetLayoutSpec.Parse(null, null, "idle, attack", "stand:0.5,swing,end:0.1", "16x16", null)!;
        Assert.Equal(["idle", "attack"], custom.Rows);
        Assert.Equal(["stand", "swing", "end"], custom.Frames.Select(f => f.Name));
        Assert.Equal([0.5, 0.15, 0.1], custom.Frames.Select(f => f.Delay));

        var uniform = SheetLayoutSpec.Parse(null, "attack-4", null, null, null, "0,25")!;
        Assert.All(uniform.Frames, f => Assert.Equal(0.25, f.Delay));
        Assert.Throws<ArgumentException>(() => SheetLayoutSpec.Parse(null, "no-such", null, null, null, null));
        Assert.Throws<ArgumentException>(() => SheetLayoutSpec.Parse(null, null, null, null, "0x5", null));
        Assert.Throws<ArgumentException>(() => SheetLayoutSpec.Parse(null, null, "a,a", null, null, null));
        Assert.Throws<ArgumentException>(() => SheetLayoutSpec.Parse(null, null, null, null, null, "soon"));
    }

    [Fact]
    public void CliLayoutPrompt_IsEnglish_AndItsExampleIsValid()
    {
        using var _ = CoreText.Scope("en");
        var layout = SheetLayoutSpec.Parse(null, null, "idle,attack", "a:0.5,b:0.25", "16x16", null)!;
        var prompt = ChatPrompt.Build(ChatKind.Sheet, sheetLayout: layout);
        var r = SheetScene.Render(ExampleOf(prompt));
        Assert.Empty(layout.Differences(r));
        Assert.Contains("expected", string.Join(' ', layout.Differences(SheetScene.Render(ChatScenePrompt.Walker))));
        Assert.DoesNotMatch("[А-Яа-я]", string.Join(' ', layout.Differences(SheetScene.Render(ChatScenePrompt.Walker))) + layout.Describe());
    }

    [Fact]
    public void WithDelay_ReplacesEveryFrameDelay()
    {
        var l = Eight().WithDelay(0.33);
        Assert.All(l.Frames, f => Assert.Equal(0.33, f.Delay));
        Assert.Contains("(delay 0.33)", ChatScenePrompt.Sheet(l));
        var t = SheetLayoutSpec.FromTemplate(BuiltInSheetTemplates.All.First(t => t.Id == "system:attack-4"), 16, 16, 0.5);
        Assert.All(t.Frames, f => Assert.Equal(0.5, f.Delay));
    }
}
