// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Text.Json.Nodes;
using PixelEditor.Core;

namespace PixelEditor.Tests;

public class RsiFolderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pixed-rsi-" + Guid.NewGuid().ToString("N"));

    /// <summary>A trivial raw "PNG" (width, height, pixels): Core has no real codec, the folder logic only needs load and save.</summary>
    private static readonly RsiImageCodec Codec = new(
        path =>
        {
            using var r = new BinaryReader(File.OpenRead(path));
            var img = new PixelImage(r.ReadInt32(), r.ReadInt32());
            for (int i = 0; i < img.Pixels.Length; i++) img.Pixels[i] = r.ReadUInt32();
            return img;
        },
        (img, path) =>
        {
            using var w = new BinaryWriter(File.Create(path));
            w.Write(img.Width); w.Write(img.Height);
            foreach (var p in img.Pixels) w.Write(p);
        });

    public RsiFolderTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private string Rsi => Path.Combine(_dir, "thing.rsi");

    /// <summary>A sheet whose cell (row, col) is filled with a unique color, so layout mistakes show up.</summary>
    private string MakeSheet(string name, int fw, int fh, int dirs, int frames)
    {
        var img = new PixelImage(fw * frames, fh * dirs);
        for (int y = 0; y < img.Height; y++)
            for (int x = 0; x < img.Width; x++)
                img.Pixels[y * img.Width + x] = Pixel.Rgb((byte)(x / fw * 20 + 10), (byte)(y / fh * 20 + 10), 99);
        var path = Path.Combine(_dir, name + ".png");
        Codec.SavePng(img, path);
        return path;
    }

    [Fact]
    public void New_CreatesMeta_WithOptionalLicense_AndRefusesToOverwrite()
    {
        var r = RsiFolder.Create(Rsi, 32, 24, null, null);
        Assert.Equal((32, 24, "", ""), (r.FrameWidth, r.FrameHeight, r.License, r.Copyright));
        var meta = RsiMeta.Parse(File.ReadAllText(Path.Combine(Rsi, "meta.json")));
        Assert.Equal((32, 24), (meta.FrameWidth, meta.FrameHeight));
        Assert.Empty(meta.States);

        var before = File.ReadAllText(Path.Combine(Rsi, "meta.json"));
        Assert.Throws<InvalidOperationException>(() => RsiFolder.Create(Rsi, 8, 8, "CC0-1.0", "me"));
        Assert.Equal(before, File.ReadAllText(Path.Combine(Rsi, "meta.json")));

        var withLicense = RsiFolder.Create(Path.Combine(_dir, "b.rsi"), 16, 16, "CC0-1.0", "me");
        Assert.Equal(("CC0-1.0", "me"), (withLicense.License, withLicense.Copyright));
        Assert.Throws<ArgumentException>(() => RsiFolder.Create(Path.Combine(_dir, "c.rsi"), 0, 16, null, null));
    }

    [Fact]
    public void RoundTrip_New_State_Info_Export()
    {
        RsiFolder.Create(Rsi, 8, 8, "CC0-1.0", "me");
        var sheet = MakeSheet("idle", 8, 8, 1, 1);
        var st = RsiFolder.SetState(Rsi, "icon", sheet, null, null, null, Codec);
        Assert.Equal((1, 1, true, "sheet"), (st.Directions, st.Frames, st.SizeConsistent, st.Layout));
        Assert.True(File.Exists(Path.Combine(Rsi, "icon.png")));

        var info = RsiFolder.Info(Rsi, Codec);
        Assert.Equal("CC0-1.0", info.License);
        var s = Assert.Single(info.States);
        Assert.Equal(("icon", 1, 1, true), (s.Name, s.Directions, s.Frames, s.PngExists));

        var outFile = Path.Combine(_dir, "out", "icon-sheet.png");
        var exp = RsiFolder.ExportState(Rsi, "icon", outFile, Codec);
        Assert.False(exp.Repacked);
        Assert.Equal((8, 8), (Codec.Load(outFile).Width, Codec.Load(outFile).Height));
    }

    [Fact]
    public void FourDirectionAnimatedState_KeepsLayoutDelaysAndOtherStates()
    {
        RsiFolder.Create(Rsi, 8, 8, "CC0-1.0", "me");
        RsiFolder.SetState(Rsi, "icon", MakeSheet("icon", 8, 8, 1, 1), 1, null, null, Codec);
        var sheet = MakeSheet("walk", 8, 8, 4, 3);
        var st = RsiFolder.SetState(Rsi, "walk", sheet, 4, null, "0.1,0.2,0.3", Codec);
        Assert.Equal((4, 3, "sheet"), (st.Directions, st.Frames, st.Layout));

        // meta.json: both states, delays per direction, the first state untouched
        var node = JsonNode.Parse(File.ReadAllText(Path.Combine(Rsi, "meta.json")))!;
        var states = node["states"]!.AsArray();
        Assert.Equal(2, states.Count);
        Assert.Equal("icon", states[0]!["name"]!.GetValue<string>());
        Assert.Equal(4, states[1]!["directions"]!.GetValue<int>());
        var delays = states[1]!["delays"]!.AsArray();
        Assert.Equal(4, delays.Count);
        Assert.Equal(0.3, delays[3]![2]!.GetValue<double>(), 3);

        // re-writing a state replaces only that state and keeps the order
        RsiFolder.SetState(Rsi, "walk", sheet, null, 0.5, null, Codec);     // directions inferred from the sheet height
        var after = RsiMeta.Parse(File.ReadAllText(Path.Combine(Rsi, "meta.json")));
        Assert.Equal(["icon", "walk"], after.States.Select(x => x.Name));
        Assert.Equal(0.5, after.Find("walk")!.Delays[2][1], 3);

        // export gives the same rows x frames sheet back
        var outFile = Path.Combine(_dir, "walk-out.png");
        var exp = RsiFolder.ExportState(Rsi, "walk", outFile, Codec);
        Assert.Equal((24, 32, 4, 3), (exp.Width, exp.Height, exp.Directions, exp.Frames));
        Assert.Equal(["S", "N", "E", "W"], exp.Rows);
        Assert.Equal(Codec.Load(sheet).Pixels, Codec.Load(outFile).Pixels);
    }

    [Fact]
    public void PerDirectionDelays_AreParsed()
    {
        var d = RsiFolder.ParseDelays("0.1,0.2;0.3,0.4;0.5,0.6;0.7,0.8", 4, 2);
        Assert.Equal(0.7, d[3][0], 3);
        Assert.Equal([0.1, 0.2], RsiFolder.ParseDelays("0.1, 0.2", 4, 2)[2]);
        Assert.Throws<ArgumentException>(() => RsiFolder.ParseDelays("0.1", 4, 2));                  // wrong frame count
        Assert.Throws<ArgumentException>(() => RsiFolder.ParseDelays("0.1,0.2;0.1,0.2", 4, 2));      // two groups for four directions
        Assert.Throws<ArgumentException>(() => RsiFolder.ParseDelays("0.1,-1", 4, 2));
        Assert.Throws<ArgumentException>(() => RsiFolder.ParseDelays("0.1,abc", 4, 2));
    }

    [Fact]
    public void BadSizesAndArguments_AreRejected_AndNothingIsWritten()
    {
        RsiFolder.Create(Rsi, 8, 8, null, null);
        var metaBefore = File.ReadAllText(Path.Combine(Rsi, "meta.json"));

        var wide = MakeSheet("wide", 12, 8, 1, 1);                       // width 12 is not a multiple of 8
        Assert.Throws<ArgumentException>(() => RsiFolder.SetState(Rsi, "a", wide, null, null, null, Codec));
        var four = MakeSheet("four", 8, 8, 4, 2);
        Assert.Throws<ArgumentException>(() => RsiFolder.SetState(Rsi, "a", four, 1, null, null, Codec));   // 4 rows but directions=1
        Assert.Throws<ArgumentException>(() => RsiFolder.SetState(Rsi, "a", four, 3, null, null, Codec));   // not 1/4/8
        var five = MakeSheet("five", 8, 8, 5, 1);
        Assert.Throws<ArgumentException>(() => RsiFolder.SetState(Rsi, "a", five, null, null, null, Codec)); // cannot infer
        Assert.Throws<ArgumentException>(() => RsiFolder.SetState(Rsi, "a/b", four, 4, null, null, Codec));  // bad state name
        Assert.Throws<ArgumentException>(() => RsiFolder.SetState(Rsi, "a", four, 4, 0.1, "0.1,0.1", Codec)); // delay and delays together
        Assert.Throws<ArgumentException>(() => RsiFolder.SetState(Rsi, "a", four, 4, -1, null, Codec));
        Assert.Throws<FileNotFoundException>(() => RsiFolder.SetState(Rsi, "a", Path.Combine(_dir, "nope.png"), null, null, null, Codec));
        Assert.Throws<DirectoryNotFoundException>(() => RsiFolder.SetState(Path.Combine(_dir, "none.rsi"), "a", four, 4, null, null, Codec));
        Directory.CreateDirectory(Path.Combine(_dir, "nometa.rsi"));
        Assert.Throws<FileNotFoundException>(() => RsiFolder.SetState(Path.Combine(_dir, "nometa.rsi"), "a", four, 4, null, null, Codec));

        Assert.Equal(metaBefore, File.ReadAllText(Path.Combine(Rsi, "meta.json")));
        Assert.False(File.Exists(Path.Combine(Rsi, "a.png")));
    }

    [Fact]
    public void Info_ReportsMissingAndMismatchedPngs()
    {
        Directory.CreateDirectory(Rsi);
        File.WriteAllText(Path.Combine(Rsi, "meta.json"),
            """{"version":1,"license":"x","copyright":"y","size":{"x":8,"y":8},"states":[{"name":"ok"},{"name":"gone"},{"name":"bad","directions":4}]}""");
        Codec.SavePng(new PixelImage(8, 8), Path.Combine(Rsi, "ok.png"));
        Codec.SavePng(new PixelImage(8, 8), Path.Combine(Rsi, "bad.png"));       // a 4-direction state needs 8x32

        var info = RsiFolder.Info(Rsi, Codec);
        Assert.Equal(["sheet", "missing", "mismatch"], info.States.Select(s => s.Layout));
        Assert.Equal([true, false, false], info.States.Select(s => s.SizeConsistent));
        Assert.Equal([true, false, true], info.States.Select(s => s.PngExists));
        Assert.Equal((8, 32), (info.States[2].ExpectedWidth, info.States[2].ExpectedHeight));
        Assert.Throws<ArgumentException>(() => RsiFolder.ExportState(Rsi, "bad", Path.Combine(_dir, "x.png"), Codec));
        Assert.Throws<FileNotFoundException>(() => RsiFolder.ExportState(Rsi, "gone", Path.Combine(_dir, "x.png"), Codec));
        Assert.Throws<ArgumentException>(() => RsiFolder.ExportState(Rsi, "nope", Path.Combine(_dir, "x.png"), Codec));
    }

    [Fact]
    public void Export_RepacksAForeignWrappedPng()
    {
        // a foreign RSI: 4 directions x 2 frames wrapped to a 2-column PNG (cells in direction-frame order, 4 columns of 8 px = 32 px wide, 2 rows)
        Directory.CreateDirectory(Rsi);
        File.WriteAllText(Path.Combine(Rsi, "meta.json"),
            """{"version":1,"license":"x","copyright":"y","size":{"x":8,"y":8},"states":[{"name":"walk","directions":4,"delays":[[0.1,0.1],[0.1,0.1],[0.1,0.1],[0.1,0.1]]}]}""");
        var wrapped = new PixelImage(32, 16);   // 4 columns x 2 rows = 8 cells
        for (int cell = 0; cell < 8; cell++)
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                    wrapped.Pixels[(cell / 4 * 8 + y) * 32 + cell % 4 * 8 + x] = Pixel.Rgb((byte)(cell + 1), 0, 0);
        Codec.SavePng(wrapped, Path.Combine(Rsi, "walk.png"));

        Assert.Equal("wrapped", RsiFolder.Info(Rsi, Codec).States[0].Layout);
        var outFile = Path.Combine(_dir, "walk-sheet.png");
        var exp = RsiFolder.ExportState(Rsi, "walk", outFile, Codec);
        Assert.True(exp.Repacked);
        var sheet = Codec.Load(outFile);
        Assert.Equal((16, 32), (sheet.Width, sheet.Height));
        // direction d, frame f is cell d*2+f: red channel = cell+1
        Assert.Equal(Pixel.Rgb((byte)(3 * 2 + 1 + 1), 0, 0), sheet.Pixels[(3 * 8) * 16 + 8]);
        Assert.Equal(Pixel.Rgb(1, 0, 0), sheet.Pixels[0]);
    }
}
