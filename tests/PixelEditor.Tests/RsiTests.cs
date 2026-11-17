// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Text.Json.Nodes;
using PixelEditor.Core;

namespace PixelEditor.Tests;

public class RsiMetaTests
{
    [Fact]
    public void Meta_RoundTrips_AndOmitsDefaults()
    {
        var meta = new RsiMeta { License = "CC0-1.0", Copyright = "me", FrameWidth = 32, FrameHeight = 32 };
        meta.States.Add(new RsiState { Name = "icon" });
        meta.States.Add(new RsiState { Name = "walk", Directions = 4, Frames = 3, Delays = RsiState.UniformDelays(4, 3, 0.2) });
        var json = meta.ToJson();

        var node = JsonNode.Parse(json)!;
        Assert.Equal(1, node["version"]!.GetValue<int>());
        Assert.Equal(32, node["size"]!["x"]!.GetValue<int>());
        Assert.Null(node["states"]![0]!["directions"]);   // one view: the field is not written
        Assert.Null(node["states"]![0]!["delays"]);       // one frame: there are no delays
        Assert.Equal(4, node["states"]![1]!["directions"]!.GetValue<int>());
        Assert.Equal(4, node["states"]![1]!["delays"]!.AsArray().Count);

        var back = RsiMeta.Parse(json);
        Assert.Equal("CC0-1.0", back.License);
        Assert.Equal(2, back.States.Count);
        Assert.Equal((4, 3), (back.States[1].Directions, back.States[1].Frames));
        Assert.Equal(0.2, back.States[1].Delays[2][1], 3);
    }

    [Fact]
    public void Parse_AcceptsRealWorldMeta_WithFlagsAndRejectsBadDirections()
    {
        var meta = RsiMeta.Parse("""
        {"version":1,"license":"CC-BY-SA-3.0","copyright":"x","size":{"x":32,"y":32},
         "states":[{"name":"closed","directions":4,"flags":{"foo":1},"delays":[[1],[1],[1],[1]]},{"name":"a"}]}
        """);
        Assert.Equal(2, meta.States.Count);
        Assert.Equal(1, meta.States[0].Frames);
        Assert.Equal(1, meta.States[1].Directions);
        Assert.Throws<FormatException>(() => RsiMeta.Parse("""{"size":{"x":32,"y":32},"states":[{"name":"b","directions":3}]}"""));
        Assert.Throws<FormatException>(() => RsiMeta.Parse("""{"states":[]}"""));
    }

    [Fact]
    public void Upsert_ReplacesStateWithSameName_KeepingOrder()
    {
        var meta = new RsiMeta();
        meta.Upsert(new RsiState { Name = "a" });
        meta.Upsert(new RsiState { Name = "b" });
        meta.Upsert(new RsiState { Name = "A", Directions = 4, Delays = RsiState.UniformDelays(4, 1, 1) });
        Assert.Equal(["A", "b"], meta.States.Select(s => s.Name));
        Assert.Equal(4, meta.States[0].Directions);
    }
}

public class RsiLayoutTests
{
    private static readonly RsiBinding Binding = new("f.rsi", "walk", 4, 3, 16, 16, RsiState.UniformDelays(4, 3, 0.1));

    [Fact]
    public void Zones_FollowRsiDirectionOrder_AndFillTheSheet()
    {
        Assert.Equal(["S", "N", "E", "W"], RsiLayout.DirectionNames(4));
        Assert.Equal(8, RsiLayout.DirectionNames(8).Count);
        var zones = RsiLayout.Zones(Binding);
        Assert.Equal(12, zones.Count);
        Assert.Equal((48, 64), (Binding.SheetWidth, Binding.SheetHeight));
        Assert.Equal(new PixelRect(16, 16, 16, 16), zones.First(z => z.Direction == 1 && z.Frame == 1).Rect);
        Assert.Equal("N · 2", zones.First(z => z.Direction == 1 && z.Frame == 1).Label);
        Assert.Equal("S", RsiLayout.Zones(Binding with { Frames = 1, Directions = 1 })[0].Label);
    }

    [Fact]
    public void Repack_ReadsWrappedForeignSheet_IntoDirectionRowsAndFrameColumns()
    {
        // a foreign sheet: 2 frames for 2 directions, but the picture is 2 cells wide (wrapping): the order S0 S1 / N0 N1 matches; check 4 cells in a column of width 1
        var src = new PixelImage(8, 32);
        for (int cell = 0; cell < 4; cell++)
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++) src.Set(x, cell * 8 + y, Pixel.Rgb((byte)(cell * 50 + 10), 0, 0));
        var sheet = RsiLayout.Repack(src, 8, 8, directions: 2, frames: 2);
        Assert.Equal((16, 16), (sheet.Width, sheet.Height));
        Assert.Equal(Pixel.Rgb(10, 0, 0), sheet.Pixels[0]);            // S frame 0
        Assert.Equal(Pixel.Rgb(60, 0, 0), sheet.Pixels[8]);            // S frame 1
        Assert.Equal(Pixel.Rgb(110, 0, 0), sheet.Pixels[8 * 16]);      // N frame 0
        Assert.Equal(Pixel.Rgb(160, 0, 0), sheet.Pixels[8 * 16 + 8]);  // N frame 1
    }

    [Fact]
    public void SafeName_And_Titles()
    {
        Assert.Equal("my_state", RsiLayout.SafeName("my state"));
        Assert.Equal("state", RsiLayout.SafeName("  "));
        Assert.Contains("юг", RsiLayout.DirectionTitle("S"));
    }

    [Fact]
    public void ProjectIO_PersistsRsiBinding()
    {
        var doc = new Document(48, 64);
        doc.SheetCellWidth = doc.SheetCellHeight = 16;
        doc.Rsi = Binding;
        using var ms = new MemoryStream();
        ProjectIO.Save(doc, ms);
        ms.Position = 0;
        var back = ProjectIO.Load(ms);
        Assert.NotNull(back.Rsi);
        Assert.Equal(("walk", 4, 3, 16, 16), (back.Rsi!.State, back.Rsi.Directions, back.Rsi.Frames, back.Rsi.FrameWidth, back.Rsi.FrameHeight));
        Assert.Equal(0.1, back.Rsi.Delays[3][2], 3);
        Assert.Null(ProjectIO.Load(RoundTrip(new Document(4, 4))).Rsi);
    }

    private static MemoryStream RoundTrip(Document d)
    {
        var ms = new MemoryStream();
        ProjectIO.Save(d, ms);
        ms.Position = 0;
        return ms;
    }
}

public class AnimationExportTests
{
    private static readonly double[][] Delays = RsiState.UniformDelays(4, 3, 0.1);

    [Fact]
    public void Clips_OnePerDirection_NamedByDirection()
    {
        var clips = AnimationExport.Clips("walk", 4, 3, Delays);
        Assert.Equal(["walk_S", "walk_N", "walk_E", "walk_W"], clips.Select(c => c.Name));
        Assert.Equal([0, 1, 2, 3], clips.Select(c => c.Row));
        Assert.Equal("idle", AnimationExport.Clips("idle", 1, 2, RsiState.UniformDelays(1, 2, 0.5))[0].Name);
    }

    [Fact]
    public void AsepriteJson_HasFramesWithDurationsAndTagsPerDirection()
    {
        var clips = AnimationExport.Clips("walk", 4, 3, Delays);
        var json = JsonNode.Parse(AnimationExport.AsepriteJson("walk.png", 96, 128, 32, 32, 3, clips))!;
        Assert.Equal(12, json["frames"]!.AsArray().Count);
        Assert.Equal(100, json["frames"]![0]!["duration"]!.GetValue<int>());
        Assert.Equal(32, json["frames"]![4]!["frame"]!["x"]!.GetValue<int>());  // frame 1 of direction N (index 4)
        Assert.Equal(32, json["frames"]![4]!["frame"]!["y"]!.GetValue<int>());
        var tags = json["meta"]!["frameTags"]!.AsArray();
        Assert.Equal(4, tags.Count);
        Assert.Equal(("walk_N", 3, 5), (tags[1]!["name"]!.GetValue<string>(), tags[1]!["from"]!.GetValue<int>(), tags[1]!["to"]!.GetValue<int>()));
        Assert.Equal("walk.png", json["meta"]!["image"]!.GetValue<string>());
    }

    [Fact]
    public void GodotSpriteFrames_ListsAtlasRegionsAndAnimations()
    {
        var clips = AnimationExport.Clips("walk", 4, 3, Delays);
        var tres = AnimationExport.GodotSpriteFrames("res://art/walk.png", 32, 32, 3, clips);
        Assert.StartsWith("[gd_resource type=\"SpriteFrames\" load_steps=14 format=3]", tres);
        Assert.Contains("path=\"res://art/walk.png\"", tres);
        Assert.Contains("region = Rect2(32, 32, 32, 32)", tres);            // N frame 1
        Assert.Equal(12, tres.Split("[sub_resource").Length - 1);
        Assert.Contains("\"name\": &\"walk_W\"", tres);
        Assert.Contains("\"duration\": 1,", tres);                          // 0.1 s × 10
        Assert.Contains("\"speed\": 10", tres);
    }

    [Fact]
    public void SimpleJson_DescribesGrid()
    {
        var clips = AnimationExport.Clips("walk", 4, 3, Delays);
        var json = JsonNode.Parse(AnimationExport.SimpleJson("walk.png", 32, 32, 3, 4, clips))!;
        Assert.Equal((3, 4), (json["columns"]!.GetValue<int>(), json["rows"]!.GetValue<int>()));
        Assert.Equal(4, json["animations"]!.AsArray().Count);
        Assert.Equal(0.1, json["animations"]![2]!["frames"]![1]!["delay"]!.GetValue<double>(), 3);
    }
}
