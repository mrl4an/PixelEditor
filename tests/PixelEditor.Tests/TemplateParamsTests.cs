// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Text.Json;
using PixelEditor.Core;
using PixelEditor.Core.Ops;

namespace PixelEditor.Tests;

public class TemplateParamsTests
{
    private static PixelImage Gradient(int w, int h)
    {
        var img = new PixelImage(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                img.Set(x, y, Pixel.Rgb((byte)(x * 255 / Math.Max(1, w - 1)), (byte)(y * 255 / Math.Max(1, h - 1)), 128));
        return img;
    }

    private const string Parameterized = """
        {
          "name": "p",
          "params": [
            { "name": "size", "type": "int", "default": 8, "min": 2, "max": 64 },
            { "name": "mode", "type": "choice", "default": "box", "choices": ["box", "nearest"] },
            { "name": "flag", "type": "bool", "default": true }
          ],
          "steps": [
            { "op": "resize", "width": "$size", "height": "$size", "method": "$mode" },
            { "op": "pad", "amount": 1, "when": "$flag" }
          ],
          "checks": [ { "check": "size", "maxWidth": "$size+2", "maxHeight": "$size+2" } ]
        }
        """;

    [Fact]
    public void OldTemplatesWithoutParams_RunUnchanged()
    {
        var p = Pipeline.Parse("""{"steps":[{"op":"resize","width":4,"height":4}],"checks":[{"check":"size","width":4,"height":4}]}""");
        Assert.Empty(p.Params);
        Assert.Empty(p.Validate());
        var r = p.Run(Gradient(16, 16));
        Assert.True(r.Success);
        Assert.Equal(4, r.Image.Width);
        Assert.Empty(Pipeline.Parse("""[{"op":"trim"}]""").Params);
    }

    [Fact]
    public void Defaults_AreUsed_AndOverridesWin()
    {
        var p = Pipeline.Parse(Parameterized);
        Assert.Empty(p.Validate());
        var d = p.Run(Gradient(32, 32));
        Assert.True(d.Success);
        Assert.Equal(10, d.Image.Width);                                   // size 8 + pad 1 on each side
        var o = p.Run(Gradient(32, 32), null, new Dictionary<string, object?> { ["size"] = 16, ["flag"] = "false", ["MODE"] = "nearest" });
        Assert.Equal(16, o.Image.Width);                                   // keys are case-insensitive, strings are coerced by type
        Assert.True(o.Success);
        Assert.Equal(12, p.Run(Gradient(32, 32), null, new Dictionary<string, object?> { ["size"] = JsonDocument.Parse("10").RootElement }).Image.Width);
    }

    [Fact]
    public void BadOverrides_AreRejected_WithTheParameterName()
    {
        var p = Pipeline.Parse(Parameterized);
        var img = Gradient(8, 8);
        Assert.Contains("nope", Assert.Throws<ArgumentException>(() => p.Run(img, null, new Dictionary<string, object?> { ["nope"] = 1 })).Message);
        Assert.Contains("size", Assert.Throws<ArgumentException>(() => p.Run(img, null, new Dictionary<string, object?> { ["size"] = 1000 })).Message);   // out of range
        Assert.Contains("size", Assert.Throws<ArgumentException>(() => p.Run(img, null, new Dictionary<string, object?> { ["size"] = "abc" })).Message);
        Assert.Contains("size", Assert.Throws<ArgumentException>(() => p.Run(img, null, new Dictionary<string, object?> { ["size"] = 4.5 })).Message);   // not an integer
        Assert.Contains("mode", Assert.Throws<ArgumentException>(() => p.Run(img, null, new Dictionary<string, object?> { ["mode"] = "wavy" })).Message);
        Assert.Contains("palette", Assert.Throws<ArgumentException>(() => BuiltIn("apply-palette").Run(img, null, new Dictionary<string, object?> { ["palette"] = "No such palette" })).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_ChecksDeclarationsReferencesAndTypes()
    {
        Assert.Contains(Pipeline.Parse("""{"steps":[{"op":"resize","width":"$size"}]}""").Validate(), m => m.Contains("size") && m.Contains("params"));
        const string head = """{"params":[{"name":"s","type":"string","default":"x"},{"name":"n","type":"int","default":3}],"steps":[""";
        Assert.Contains(Pipeline.Parse(head + """{"op":"resize","width":"$s"}]}""").Validate(), m => m.Contains("width") && m.Contains("string"));     // a string where a number is needed
        Assert.Contains(Pipeline.Parse(head + """{"op":"trim","when":"$zzz"}]}""").Validate(), m => m.Contains("zzz"));
        Assert.Contains(Pipeline.Parse(head + """{"op":"trim","when":"$n ~ 2"}]}""").Validate(), m => m.Contains("when"));
        Assert.Contains(Pipeline.Parse(head + """{"op":"resize","width":"$s+1"}]}""").Validate(), m => m.Contains("arithmetic") || m.Contains("арифметик"));
        Assert.Contains(Pipeline.Parse(head + """{"op":"resize","width":"$n$"}]}""").Validate(), m => m.Contains("$n$"));
        Assert.Empty(Pipeline.Parse(head + """{"op":"resize","width":"$n*2","method":"$s"}]}""").Validate());
        // broken definitions
        Assert.NotEmpty(Pipeline.Parse("""{"params":[{"name":"a","type":"int","default":99,"max":5}],"steps":[{"op":"trim"}]}""").Validate());
        Assert.NotEmpty(Pipeline.Parse("""{"params":[{"name":"a","type":"weird","default":1}],"steps":[{"op":"trim"}]}""").Validate());
        Assert.NotEmpty(Pipeline.Parse("""{"params":[{"name":"a","type":"int","default":1},{"name":"A","type":"int","default":2}],"steps":[{"op":"trim"}]}""").Validate());
        Assert.NotEmpty(Pipeline.Parse("""{"params":[{"name":"a","type":"choice","default":"z","choices":["x","y"]}],"steps":[{"op":"trim"}]}""").Validate());
        Assert.NotEmpty(Pipeline.Parse("""{"params":[{"name":"a","type":"int"}],"steps":[{"op":"trim"}]}""").Validate());
    }

    [Fact]
    public void When_Comparisons_AndAlternatives()
    {
        const string json = """
            {"params":[{"name":"n","type":"int","default":5},{"name":"c","type":"choice","default":"b","choices":["a","b","c"]}],
             "steps":[
               {"op":"pad","amount":1,"when":"$n>=5"},
               {"op":"pad","amount":10,"when":"$n<5"},
               {"op":"pad","amount":100,"when":"$c==a|c"},
               {"op":"pad","amount":1000,"when":"$c!=a|c"},
               {"op":"pad","amount":10000,"when":"!$n"}]}
            """;
        var p = Pipeline.Parse(json);
        Assert.Empty(p.Validate());
        var img = new PixelImage(1, 1);
        Assert.Equal(1 + 2 * (1 + 1000), p.Run(img).Image.Width);                                                    // n>=5 and c!=a|c are true
        Assert.Equal(1 + 2 * (10 + 100), p.Run(img, null, new Dictionary<string, object?> { ["n"] = 2, ["c"] = "c" }).Image.Width);
        Assert.Equal(1 + 2 * (10 + 1000 + 10000), p.Run(img, null, new Dictionary<string, object?> { ["n"] = 0 }).Image.Width);
    }

    [Fact]
    public void Checks_CanBeSkippedByWhen_AndFailForTheRightValue()
    {
        var p = Pipeline.Parse("""{"params":[{"name":"m","type":"int","default":2}],"checks":[{"check":"maxColors","value":"$m","when":"$m>1"}]}""");
        var img = Gradient(8, 8);
        Assert.False(p.RunChecks(img).Single().Passed);                                                              // more than 2 colors
        Assert.True(p.RunChecks(img, new Dictionary<string, object?> { ["m"] = 1000 }).Single().Passed);
        Assert.Empty(p.RunChecks(img, new Dictionary<string, object?> { ["m"] = 1 }));                                // the check is switched off
    }

    [Fact]
    public void ChatTextValidation_UnderstandsParams()
    {
        var good = PipelineText.Check(Parameterized);
        Assert.True(good.IsValid, string.Join("; ", good.Errors));
        Assert.Empty(good.Warnings);                                                                                  // "when" is not an unknown parameter
        var bad = PipelineText.Check("""{"steps":[{"op":"resize","width":"$nope"}]}""");
        Assert.False(bad.IsValid);
        Assert.Contains(bad.Errors, e => e.Contains("nope"));
        Assert.Contains("params", ChatPrompt.Build());
    }

    [Fact]
    public void SaveToLibrary_KeepsParams()
    {
        using var dir = new TempDir();
        var lib = new PipelineLibrary(dir.Path);
        var saved = lib.Save("Mine", "d", Parameterized);
        Assert.Equal(["size", "mode", "flag"], saved.Params.Select(x => x.Name));
        Assert.Equal(8, saved.Params[0].Default);
        Assert.Throws<ArgumentException>(() => lib.Save("Bad", "", """{"steps":[{"op":"resize","width":"$q"}]}"""));
    }

    // ---- new operation features ----

    [Fact]
    public void Resize_NoUpscale_KeepsSmallPicturesAsTheyAre()
    {
        var small = Gradient(8, 8);
        Assert.Equal(8, BgCases.Run("resize", small, """{"width":64,"height":64,"noUpscale":true}""").Width);
        Assert.Equal(64, BgCases.Run("resize", small, """{"width":64,"height":64}""").Width);
        Assert.Equal(16, BgCases.Run("resize", Gradient(100, 50), """{"width":16,"height":16,"noUpscale":true}""").Width);
    }

    [Fact]
    public void ResizeCanvas_MultipleOfAndOnlyGrow_AndSizeCheckMultipleOf()
    {
        var img = Gradient(10, 13);
        var m = BgCases.Run("resizeCanvas", img, """{"multipleOf":8}""");
        Assert.Equal((16, 16), (m.Width, m.Height));
        var g = BgCases.Run("resizeCanvas", img, """{"width":8,"height":32,"onlyGrow":true}""");
        Assert.Equal((10, 32), (g.Width, g.Height));                                                                  // nothing is cropped
        Assert.True(Checks.Get("size").Run(m, new OpArgs(JsonDocument.Parse("""{"multipleOf":8}""").RootElement, null)).Passed);
        Assert.False(Checks.Get("size").Run(img, new OpArgs(JsonDocument.Parse("""{"multipleOf":8}""").RootElement, null)).Passed);
    }

    // ---- built-in templates and aliases ----

    private static Pipeline BuiltIn(string id) => Pipeline.Parse(BuiltInTemplates.All.Single(t => t.Id == "system:" + id).Json);

    [Fact]
    public void BuiltInTemplates_AreValid_HaveLabelsAndRunWithDefaults()
    {
        Assert.InRange(BuiltInTemplates.All.Count, 6, 9);
        foreach (var t in BuiltInTemplates.All)
        {
            var p = Pipeline.Parse(t.Json);
            Assert.True(p.Params.Count > 0, t.Name + " has no params");
            Assert.Empty(p.Validate());
            Assert.All(p.Params, x => Assert.False(string.IsNullOrWhiteSpace(x.Label) || string.IsNullOrWhiteSpace(x.Hint), t.Name + "." + x.Name));
            Assert.True(p.Run(Gradient(40, 30)).Ok, t.Name);
            // the declared default of every parameter is accepted by its own validation
            Assert.Equal(p.Params.Count, p.ResolveParams().Count);
        }
    }

    [Fact]
    public void OldNamesAndIds_StillResolve_InBothLanguages()
    {
        var lib = new PipelineLibrary(Path.Combine(Path.GetTempPath(), "pixed-alias-" + Guid.NewGuid().ToString("N")));
        foreach (var a in TemplateAliases.All)
        {
            var viaId = lib.FindWithPreset("system:" + a.OldId);
            Assert.NotNull(viaId);
            foreach (var name in a.Names)
            {
                var found = lib.FindWithPreset(name);
                Assert.NotNull(found);
                Assert.Equal("system:" + a.TargetId, found!.Value.Template.Id);
            }
            var pipeline = Pipeline.Parse(viaId!.Value.Template.Json).WithPreset(viaId.Value.Preset);
            Assert.Empty(pipeline.Validate());
            pipeline.ResolveParams();                                                                                 // the preset values are valid for the target
            Assert.True(pipeline.Run(Gradient(40, 40)).Ok, a.OldId);
        }
        // a preset reproduces the old behavior and an explicit override beats it
        var thirtyTwo = lib.FindWithPreset("Фото в спрайт 32×32")!.Value;
        var p = Pipeline.Parse(thirtyTwo.Template.Json).WithPreset(thirtyTwo.Preset);
        Assert.Equal(32, p.ResolveParams()["size"]);
        Assert.Equal(48, p.ResolveParams(new Dictionary<string, object?> { ["size"] = 48 })["size"]);
        Assert.Equal("black", p.ResolveParams()["outline"]);
        // the old English names too, and the removed single-purpose templates keep their behavior
        Assert.NotNull(lib.Find("Photo to a 32×32 sprite"));
        Assert.Equal(16, Pipeline.Parse(lib.Find("Limit to 16 colors")!.Json).Run(Gradient(40, 40)).Image.DistinctColors());
        Assert.Equal(42, Pipeline.Parse(lib.Find("Чёрный контур 1 px")!.Json).Run(Gradient(40, 40)).Image.Width);
        // current names are not aliases: the library does not list the old ones
        Assert.DoesNotContain(lib.All(), t => t.Id is "system:photo-to-sprite" or "system:outline-1px");
    }
}
