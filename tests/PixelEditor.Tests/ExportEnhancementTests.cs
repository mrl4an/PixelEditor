// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Text.Json.Nodes;
using PixelEditor.Core;

namespace PixelEditor.Tests;

/// <summary>Lossless meta.json, per-clip loop, the Unity importer script and the export profile in .pixproj.</summary>
public class ExportEnhancementTests
{
    private const string ForeignMeta = """
    {
      "version": 1,
      "license": "CC-BY-SA-3.0",
      "copyright": "someone",
      "size": { "x": 32, "y": 32 },
      "load": { "srgb": false },
      "x-custom": [1, 2, { "k": "v" }],
      "states": [
        { "name": "alpha", "flags": { "names": ["fire"] }, "directions": 4, "delays": [[0.1, 0.1], [0.1, 0.1], [0.1, 0.1], [0.1, 0.1]], "extra": 1.0 },
        { "name": "beta", "flags": { "names": ["dead"] }, "directions": 4, "delays": [[0.5], [0.5], [0.5], [0.5]], "note": "keep" },
        { "name": "gamma", "select": ["a"] }
      ]
    }
    """;

    [Fact]
    public void SaveOneState_KeepsEverythingElse()
    {
        var original = JsonNode.Parse(ForeignMeta)!.AsObject();
        var meta = RsiMeta.Parse(ForeignMeta);
        meta.Upsert(new RsiState { Name = "beta", Directions = 4, Frames = 2, Delays = RsiState.UniformDelays(4, 2, 0.25) });
        var after = JsonNode.Parse(meta.ToJson())!.AsObject();

        // top level: same keys in the same order, unknown ones untouched
        Assert.Equal(original.Select(kv => kv.Key), after.Select(kv => kv.Key));
        Assert.True(JsonNode.DeepEquals(original["load"], after["load"]));
        Assert.True(JsonNode.DeepEquals(original["x-custom"], after["x-custom"]));
        Assert.True(JsonNode.DeepEquals(original["size"], after["size"]));

        // states: same order; the other ones identical to the original text, including unknown fields and number spelling
        var os = original["states"]!.AsArray();
        var ns = after["states"]!.AsArray();
        Assert.Equal(["alpha", "beta", "gamma"], ns.Select(n => n!["name"]!.GetValue<string>()));
        Assert.Equal(os[0]!.ToJsonString(), ns[0]!.ToJsonString());
        Assert.Equal(os[2]!.ToJsonString(), ns[2]!.ToJsonString());

        // the saved state: new delays, but flags and note survive
        Assert.Equal(0.25, ns[1]!["delays"]![2]![1]!.GetValue<double>());
        Assert.Equal("dead", ns[1]!["flags"]!["names"]![0]!.GetValue<string>());
        Assert.Equal("keep", ns[1]!["note"]!.GetValue<string>());
    }

    [Fact]
    public void SaveState_DropsDirectionsAndDelaysOnlyWhenDefault()
    {
        var meta = RsiMeta.Parse(ForeignMeta);
        meta.Upsert(new RsiState { Name = "alpha", Directions = 1, Frames = 1, Delays = RsiState.UniformDelays(1, 1, 1.0) });
        var a = JsonNode.Parse(meta.ToJson())!["states"]![0]!.AsObject();
        Assert.False(a.ContainsKey("directions"));
        Assert.False(a.ContainsKey("delays"));
        Assert.True(a.ContainsKey("flags"));
    }

    [Fact]
    public void NewState_IsAppended_AndUnchangedMetaIsStable()
    {
        var meta = RsiMeta.Parse(ForeignMeta);
        meta.Upsert(new RsiState { Name = "delta", Directions = 1, Frames = 2, Delays = RsiState.UniformDelays(1, 2, 0.3) });
        var after = JsonNode.Parse(meta.ToJson())!;
        Assert.Equal(4, after["states"]!.AsArray().Count);
        Assert.Equal("delta", after["states"]![3]!["name"]!.GetValue<string>());
        Assert.Equal(JsonNode.Parse(ForeignMeta)!["load"]!.ToJsonString(), after["load"]!.ToJsonString());

        // nothing saved: the document is semantically the same
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(ForeignMeta), JsonNode.Parse(RsiMeta.Parse(ForeignMeta).ToJson())));
    }

    [Fact]
    public void LicenseAndCopyright_CanBeEmpty_AndMetaEditsKeepForeignFields()
    {
        var meta = RsiMeta.Parse(ForeignMeta);
        meta.License = "";
        meta.Copyright = "";
        var node = JsonNode.Parse(meta.ToJson())!;
        Assert.Equal("", node["license"]!.GetValue<string>());
        Assert.Equal("", node["copyright"]!.GetValue<string>());
        Assert.NotNull(node["x-custom"]);

        var fresh = new RsiMeta { License = "", Copyright = "" };
        fresh.States.Add(new RsiState { Name = "icon" });
        var back = RsiMeta.Parse(fresh.ToJson());
        Assert.Equal(("", ""), (back.License, back.Copyright));
        Assert.Equal("icon", back.States.Single().Name);
    }

    [Fact]
    public void Clips_NoLoopRows_AndEngineOutputs()
    {
        var delays = RsiState.UniformDelays(2, 2, 0.1);
        var clips = AnimationExport.Clips("hero", 2, 2, delays, ["idle", "attack"], ["Attack"]);
        Assert.Equal([true, false], clips.Select(c => c.Loop));
        Assert.True(new AnimationExport.Clip("x", 0, [0.1]).Loop);   // the default keeps old call sites looping

        var tres = AnimationExport.GodotSpriteFrames("res://h.png", 16, 16, 2, clips);
        Assert.Contains("\"loop\": true,\n\"name\": &\"hero_idle\"", tres.Replace("\r\n", "\n"));
        Assert.Contains("\"loop\": false,\n\"name\": &\"hero_attack\"", tres.Replace("\r\n", "\n"));
        // the global flag still turns everything off
        Assert.DoesNotContain("\"loop\": true", AnimationExport.GodotSpriteFrames("res://h.png", 16, 16, 2, clips, loop: false));

        var json = JsonNode.Parse(AnimationExport.SimpleJson("h.png", 16, 16, 2, 2, clips))!;
        Assert.True(json["animations"]![0]!["loop"]!.GetValue<bool>());
        Assert.False(json["animations"]![1]!["loop"]!.GetValue<bool>());
    }

    [Fact]
    public void UnityScript_MentionsEveryFieldOfTheSimpleJson()
    {
        var clips = AnimationExport.Clips("hero", 2, 2, RsiState.UniformDelays(2, 2, 0.1));
        var json = JsonNode.Parse(AnimationExport.SimpleJson("h.png", 16, 16, 2, 2, clips))!.AsObject();
        var names = new HashSet<string>(json.Select(kv => kv.Key));
        var anim = json["animations"]![0]!.AsObject();
        names.UnionWith(anim.Select(kv => kv.Key));
        names.UnionWith(anim["frames"]![0]!.AsObject().Select(kv => kv.Key));

        var script = AnimationExport.UnityImporterScript;
        foreach (var name in names)
            Assert.Matches($@"\b{System.Text.RegularExpressions.Regex.Escape(name)}\b", script);
        Assert.Contains("TextureImporter", script);
        Assert.Contains("MenuItem", script);
        Assert.Equal("PixelEditorSheetImporter.cs", AnimationExport.UnityScriptFileName);
    }

    [Fact]
    public void Project_StoresExportProfile_AndOldProjectsStillOpen()
    {
        var doc = new Document(4, 4);
        var profile = new AnimationExportProfile("C:/out", "hero", true, false, true, true, true, "res://hero.png", ["attack", "die"]);
        doc.ExportProfile = profile;
        using var ms = new MemoryStream();
        ProjectIO.Save(doc, ms);
        ms.Position = 0;
        var back = ProjectIO.Load(ms).ExportProfile!;
        Assert.Equal(("C:/out", "hero", true, false, true, true, true, "res://hero.png"), (back.Folder, back.BaseName, back.Aseprite, back.Godot, back.Gif, back.Simple, back.Unity, back.GodotTexturePath));
        Assert.Equal(["attack", "die"], back.NoLoop);

        // an old project: doc.json has no export field at all
        using var ms2 = new MemoryStream();
        ProjectIO.Save(doc, ms2);
        using (var zip = new System.IO.Compression.ZipArchive(ms2, System.IO.Compression.ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = zip.GetEntry("doc.json")!;
            JsonNode root;
            using (var rs = entry.Open()) root = JsonNode.Parse(rs)!;
            Assert.True(root.AsObject().Remove("Export"));
            entry.Delete();
            using var ws = zip.CreateEntry("doc.json").Open();
            ws.Write(System.Text.Encoding.UTF8.GetBytes(root.ToJsonString()));
        }
        ms2.Position = 0;
        Assert.Null(ProjectIO.Load(ms2).ExportProfile);
    }
}
