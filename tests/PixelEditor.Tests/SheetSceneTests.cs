// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using PixelEditor.Core;
using PixelEditor.Core.Ops;

namespace PixelEditor.Tests;

/// <summary>Sprites and sprite sheets drawn from primitives through a chat: the sheet renderer, the answer kinds, the prompts and their examples.</summary>
public class SheetSceneTests
{
    private const string TwoByTwo = """
        {"frameSize":[8,8],"palette":{"a":"#FF0000","b":"#0000FF"},"delay":0.2,
         "base":[{"rect":[0,0,8,2],"color":"a"}],
         "rows":[
          {"name":"E","frames":[
            {"name":"stand","delay":0.5,"items":[{"rect":[5,4,3,3],"color":"b"}]},
            {"name":"step","items":[{"rect":[5,4,3,3],"color":"b"}],"offset":[0,1]},
            {"name":"again","copyOf":"stand"}]},
          {"name":"W","mirrorOf":"E"},
          {"name":"S","copyOf":"E"}]}
        """;

    [Fact]
    public void Sheet_LaysFramesOutByRowsAndColumns_WithNamesAndDelays()
    {
        var r = SheetScene.Render(TwoByTwo);
        Assert.Equal(3, r.Columns);
        Assert.Equal(new[] { "E", "W", "S" }, r.RowNames);
        Assert.Equal(24, r.Image.Width);                              // 3 columns × 8
        Assert.Equal(24, r.Image.Height);                             // 3 rows × 8
        Assert.Equal(new string?[] { "stand", "step", "again" }, r.FrameNames);
        Assert.Equal(0.5, r.Delays[0][0]);
        Assert.Equal(0.2, r.Delays[0][1]);                            // the sheet default
        Assert.Equal(0.5, r.Delays[1][0]);                            // the mirrored row keeps the delays of its source
        Assert.Equal(0.2, r.Delays[1][2]);                            // a copyOf frame has its own delay (here the default)
        // the base is in every frame, the frame items only where they are listed
        Assert.Equal(Pixel.Rgb(255, 0, 0), r.Image.Get(1, 0));
        Assert.Equal(Pixel.Rgb(255, 0, 0), r.Image.Get(8 + 1, 1));            // (the "step" frame is shifted down by one, base included)
        Assert.Equal(Pixel.Rgb(0, 0, 255), r.Image.Get(6, 5));
        Assert.Equal(Pixel.Rgb(0, 0, 255), r.Image.Get(8 + 6, 6));    // the step frame is shifted down by one
        Assert.True(Pixel.IsEmpty(r.Image.Get(8 + 6, 4)));
    }

    [Fact]
    public void MirrorOf_IsAnExactLeftRightFlipOfTheSourceRow_AndCopyOfAnExactCopy()
    {
        var r = SheetScene.Render(TwoByTwo);
        for (int c = 0; c < 3; c++)
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                {
                    Assert.Equal(r.Image.Get(c * 8 + (7 - x), y), r.Image.Get(c * 8 + x, 8 + y));     // W is E mirrored
                    Assert.Equal(r.Image.Get(c * 8 + x, y), r.Image.Get(c * 8 + x, 16 + y));          // S is a copy of E
                }
        Assert.Equal(r.Image.Get(6, 5), r.Image.Get(16 + 6, 5));                                       // frame "again" repeats "stand"
    }

    [Fact]
    public void Sheet_ReportsWhereTheProblemIs()
    {
        var bad = """{"frameSize":[8,8],"rows":[{"name":"S","frames":[{"items":[{"rect":[0,0,4,4],"color":"#FF0000"}]},{"items":[{"circle":[1,1,3,3],"color":"#FF0000"}]}]}]}""";
        var ex = Assert.Throws<ArgumentException>(() => SheetScene.Render(bad));
        Assert.Contains("S", ex.Message);
        Assert.Contains("2", ex.Message);                                                               // the row and the frame
        Assert.Throws<ArgumentException>(() => SheetScene.Render("""{"frameSize":[8,8],"rows":[{"name":"W","mirrorOf":"nope"}]}"""));
        Assert.Throws<ArgumentException>(() => SheetScene.Render("""{"frameSize":[8,8],"rows":[{"name":"A","frames":[{"items":[]}]},{"name":"a","copyOf":"A"}]}"""));   // repeated name
        Assert.Throws<ArgumentException>(() => SheetScene.Render("""{"frameSize":[999,8],"rows":[{"name":"A","frames":[{"items":[]}]}]}"""));
    }

    [Fact]
    public void Meta_DescribesTheSheet()
    {
        var meta = System.Text.Json.JsonDocument.Parse(SheetScene.Render(TwoByTwo).ToMetaJson()).RootElement;
        Assert.Equal(3, meta.GetProperty("columns").GetInt32());
        Assert.Equal(3, meta.GetProperty("rows").GetArrayLength());
        Assert.Equal("stand", meta.GetProperty("frameNames")[0].GetString());
    }

    [Fact]
    public void DetectKind_TellsPipelinesSpritesAndSheetsApart()
    {
        Assert.Equal(ChatKind.Pipeline, PipelineText.Detect("""{"steps":[{"op":"trim"}]}"""));
        Assert.Equal(ChatKind.Sprite, PipelineText.Detect(ChatScenePrompt.Person));
        Assert.Equal(ChatKind.Sheet, PipelineText.Detect(TwoByTwo));
        // an answer with explanations around a sheet is found like a pipeline is
        Assert.Equal(ChatKind.Sheet, PipelineText.Detect(PipelineText.Extract("Here you go:\n```json\n" + TwoByTwo + "\n```")));
    }

    [Fact]
    public void BothExamplesInThePrompts_AreValid_AndDrawSomething()
    {
        foreach (var json in new[] { ChatScenePrompt.Person, ChatScenePrompt.Chest, ChatScenePrompt.Walker })
        {
            var p = PipelineText.Check(PipelineText.Extract("```json\n" + json + "\n```"));
            Assert.True(p.IsValid, string.Join("; ", p.Errors));
        }
        var person = Scene.Render(ChatScenePrompt.Person);
        Assert.Equal(24, person.Width);
        Assert.True(person.Pixels.Count(px => !Pixel.IsEmpty(px)) > 150);
        var walker = SheetScene.Render(ChatScenePrompt.Walker);
        Assert.Equal(4, walker.Rows);
        Assert.Equal(3, walker.Columns);
        Assert.Equal(72, walker.Image.Width);
        Assert.Equal(128, walker.Image.Height);
        Assert.All(Enumerable.Range(0, 4), row => Assert.True(Enumerable.Range(0, 3).All(c =>
            Enumerable.Range(0, 32 * 24).Any(i => !Pixel.IsEmpty(walker.Image.Get(c * 24 + i % 24, row * 32 + i / 24))))));        // no empty frame
    }

    [Fact]
    public void Check_FindsATypoInAShapeKey_WithAHint_ForSpritesAndSheets()
    {
        var sprite = PipelineText.Check("""{"size":[16,16],"items":[{"elipse":[2,2,8,8],"color":"#FF0000"}]}""");
        Assert.False(sprite.IsValid);
        Assert.Contains(sprite.Errors, e => e.Contains("elipse") || e.Contains("ellipse"));
        var sheet = PipelineText.Check("""{"frameSize":[8,8],"rows":[{"name":"S","frames":[{"items":[{"rec":[0,0,4,4],"color":"#FF0000"}]}]}]}""");
        Assert.False(sheet.IsValid);
        Assert.Contains(sheet.Errors, e => e.Contains("S") && e.Contains("1"));
        Assert.True(PipelineText.Check(ChatScenePrompt.Person).IsValid);
        Assert.Contains(PipelineText.Check("""{"size":[8,8],"items":[],"colour":"x"}""").Warnings, w => w.Contains("colour"));
    }

    [Fact]
    public void Prompts_ForSpritesAndSheets_ExplainTheFormatAndAreEnglishWhateverTheUiLanguage()
    {
        var sprite = ChatPrompt.Build(ChatKind.Sprite);
        var sheet = ChatPrompt.Build(ChatKind.Sheet);
        foreach (var text in new[] { sprite, sheet })
        {
            Assert.Contains("\"rect\"", text);
            Assert.Contains("\"ellipse\"", text);
            Assert.Contains("palette", text);
            Assert.DoesNotContain(text, c => c is >= 'А' and <= 'я');
        }
        Assert.Contains("\"items\"", sprite);
        Assert.Contains("mirrorOf", sheet);
        Assert.Contains("\"base\"", sheet);
        Assert.Contains("S, N, E, W", sheet);
        Assert.Contains("- resize(", ChatPrompt.Build(ChatKind.Pipeline));                               // the pipeline prompt is unchanged
    }
}

public class PrettyJsonTests
{
    [Fact]
    public void Pretty_KeepsShortObjectsAndArraysOnOneLine_SplitsLongOnes_AndStaysEqualJson()
    {
        var pretty = PipelineText.Pretty(ChatScenePrompt.Person);
        Assert.Contains("{\"ellipse\": [7, 2, 10, 9], \"color\": \"skin\"}", pretty);     // one shape per line
        Assert.Contains("\"size\": [24, 32]", pretty);
        Assert.True(pretty.Split('\n').Length > 8);                                         // but the whole is split by lines
        // the same data: rendering gives the identical sprite
        Assert.Equal(Scene.Render(ChatScenePrompt.Person).Pixels, Scene.Render(pretty).Pixels);
        Assert.Equal(pretty, PipelineText.Pretty(pretty));                                  // formatting is stable
        Assert.Equal("not json", PipelineText.Pretty("not json"));
    }
}
