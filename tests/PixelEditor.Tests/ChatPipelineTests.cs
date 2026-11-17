// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using PixelEditor.Core;
using PixelEditor.Core.Ops;

namespace PixelEditor.Tests;

/// <summary>Working with a template from an ordinary chat: the JSON is pulled out of an answer with explanations, validated and run.</summary>
public class ChatPipelineTests
{
    private const string Pipe = """{"name":"t","steps":[{"op":"trim"},{"op":"resize","width":8}],"checks":[{"check":"maxColors","value":16}]}""";

    [Fact]
    public void Extract_FindsJsonInFencedBlocks_WithProseAround()
    {
        var answer = "Sure! Here is a pipeline that shrinks the picture.\n\n```json\n" + Pipe + "\n```\n\nHope it helps. {not json}";
        Assert.Equal(Pipe, PipelineText.Extract(answer));
        // a block without a language and an answer without a block
        Assert.Equal(Pipe, PipelineText.Extract("text\n```\n" + Pipe + "\n```"));
        Assert.Equal(Pipe, PipelineText.Extract("Just the object: " + Pipe + " thanks"));
    }

    [Fact]
    public void Extract_ToleratesTrailingCommasCommentsAndSmartQuotes()
    {
        var messy = "```json\n{ “name”: “t”, // comment\n  “steps”: [ {“op”: “trim”,}, ], }\n```";
        var json = PipelineText.Extract(messy);
        var p = Pipeline.Parse(json);
        Assert.Single(p.Steps);
        Assert.True(PipelineText.Check(json).IsValid);
    }

    [Fact]
    public void Extract_PicksThePipelineBlock_WhenOtherBlocksComeFirst_AndBracesInStringsAreSafe()
    {
        var text = "Example call: ```bash\npixed run x\n```\n```json\n{\"name\":\"a } b\",\"steps\":[{\"op\":\"trim\"}]}\n```";
        var json = PipelineText.Extract(text);
        Assert.Equal("a } b", Pipeline.Parse(json).Name);
        // a plain array of steps fits too
        Assert.Single(Pipeline.Parse(PipelineText.Extract("[{\"op\":\"trim\"}]")).Steps);
    }

    [Fact]
    public void Extract_FailsClearly_OnEmptyTextTextWithoutJsonAndBrokenJson()
    {
        using var _ = CoreText.Scope("en");
        Assert.Contains("empty", Assert.Throws<FormatException>(() => PipelineText.Extract("  ")).Message);
        Assert.Contains("No answer JSON", Assert.Throws<FormatException>(() => PipelineText.Extract("I cannot do that.")).Message);
        Assert.Contains("cannot be parsed", Assert.Throws<FormatException>(() => PipelineText.Extract("{\"steps\": [ {\"op\": } ]}")).Message);
        Assert.Throws<FormatException>(() => PipelineText.Extract("{\"hello\": 1}"));          // there is JSON but it is not a template
    }

    [Fact]
    public void Check_FindsUnknownOperationsChecksAndParameters_WithSuggestions()
    {
        using var _ = CoreText.Scope("en");
        var bad = """{"steps":[{"op":"resise","width":8},{"op":"trim","bogus":1},{"op":"quantize","colours":8}],"checks":[{"check":"maxColor","value":3},{"check":"binaryAlpha","required":false}]}""";
        var r = PipelineText.Check(bad);
        Assert.False(r.IsValid);
        Assert.Contains(r.Errors, e => e.Contains("Step 1") && e.Contains("resise") && e.Contains("«resize»"));
        Assert.Contains(r.Errors, e => e.Contains("Check 1") && e.Contains("«maxColors»"));
        Assert.Contains(r.Warnings, w => w.Contains("bogus"));
        Assert.Contains(r.Warnings, w => w.Contains("colours") && w.Contains("«colors»"));
        Assert.DoesNotContain(r.Warnings, w => w.Contains("required"));                         // required is allowed on a check
        Assert.True(PipelineText.Check(Pipe).IsValid);

        var fix = PipelineText.FixRequest(r);
        Assert.Contains("resise", fix);
        Assert.Contains("corrected JSON", fix);
    }

    [Fact]
    public void ChatPrompt_ListsEveryLiveOperationAndCheck_InEnglish_AndWorksWithAndWithoutImage()
    {
        using var _ = CoreText.Scope("ru");                      // even in a Russian UI the chat prompt is English
        var full = ChatPrompt.Build(new ChatPromptImage(480, 320, 12345, false));
        Assert.Contains("480x320", full);
        foreach (var o in Operations.All) Assert.Contains($"- {o.Name}(", full);
        foreach (var c in Checks.All) Assert.Contains($"- {c.Name}(", full);
        Assert.Contains("PICO-8", full);
        Assert.Contains("```json", full);
        // plugins of other tests register operations with their own descriptions: their texts are not ours, only the built-in ones are checked
        var plugins = Operations.All.Where(o => o.Source != "builtin").Select(o => $"- {o.Name}(").Concat(Checks.All.Where(c => c.Source != "builtin").Select(c => $"- {c.Name}(")).ToList();
        var russian = full.Split('\n').Where(l => !plugins.Any(l.StartsWith) && System.Text.RegularExpressions.Regex.IsMatch(l, "[А-Яа-яЁё]")).ToList();
        Assert.True(russian.Count == 0, string.Join(" | ", russian.Select(l => l.Length > 80 ? l[..80] : l)));
        var compact = ChatPrompt.Build(null, compact: true);
        Assert.True(compact.Length < full.Length);
        Assert.DoesNotContain("My image", compact);
    }

    [Fact]
    public void AnswerExtractedFromChat_RunsOnAnImage_AndExamplesInThePromptAreValid()
    {
        var img = new PixelImage(16, 16);
        for (int y = 4; y < 12; y++) for (int x = 4; x < 12; x++) img.Set(x, y, Pixel.Rgb(200, 50, 50));
        var result = Pipeline.Parse(PipelineText.Extract("Here:\n```json\n" + Pipe + "\n```")).Run(img);
        Assert.True(result.Ok);
        Assert.Equal(8, result.Image.Width);

        // both examples from the prompt must pass their own validation
        var prompt = ChatPrompt.Build();
        int pos = 0, found = 0;
        prompt = prompt.Replace("\r\n", "\n");
        while ((pos = prompt.IndexOf("\n```json\n", pos, StringComparison.Ordinal)) >= 0)   // the block with the example starts on a new line
        {
            int end = prompt.IndexOf("\n```", pos + 9, StringComparison.Ordinal);
            var block = prompt[(pos + 9)..end];
            Assert.True(PipelineText.Check(PipelineText.Extract(block)).IsValid, block);
            found++;
            pos = end + 4;
        }
        Assert.Equal(2, found);
    }
}
