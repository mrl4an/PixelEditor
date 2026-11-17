// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.IO.Compression;
using System.Text.Json;

namespace PixelEditor.Core;

/// <summary>A .pixproj project: a zip with doc.json and raw layers (BGRA uint LE) in layers/N.bin.</summary>
public static class ProjectIO
{
    private sealed record LayerMeta(string Name, bool Visible, bool Locked, string File);
    private sealed record DocMeta(int Version, int Width, int Height, List<LayerMeta> Layers, int? SheetW = null, int? SheetH = null,
        List<string>? Palette = null, string? PaletteName = null, RsiMeta2? Rsi = null, AnimationExportProfile? Export = null);
    private sealed record RsiMeta2(string Folder, string State, int Directions, int Frames, int FrameW, int FrameH, List<List<double>> Delays, List<string>? Rows = null, List<string>? FrameNames = null);

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static void Save(Document doc, string path)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        Save(doc, fs);
    }

    public static void Save(Document doc, Stream stream)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        var meta = new DocMeta(2, doc.Width, doc.Height, new List<LayerMeta>(),
            doc.HasSheetGrid ? doc.SheetCellWidth : null, doc.HasSheetGrid ? doc.SheetCellHeight : null,
            doc.Palette?.Select(Pixel.ToHex).ToList(), doc.PaletteName,
            doc.Rsi == null ? null : new RsiMeta2(doc.Rsi.Folder, doc.Rsi.State, doc.Rsi.Directions, doc.Rsi.Frames, doc.Rsi.FrameWidth, doc.Rsi.FrameHeight,
                doc.Rsi.Delays.Select(r => r.ToList()).ToList(), doc.Rsi.Rows?.ToList(), doc.Rsi.FrameNames?.ToList()),
            doc.ExportProfile);
        for (int i = 0; i < doc.Layers.Count; i++)
        {
            var l = doc.Layers[i];
            var file = $"layers/{i}.bin";
            meta.Layers.Add(new LayerMeta(l.Name, l.Visible, l.Locked, file));
            var bytes = new byte[l.Pixels.Length * 4];
            Buffer.BlockCopy(l.Pixels, 0, bytes, 0, bytes.Length);
            var entry = zip.CreateEntry(file, CompressionLevel.Optimal);
            using var es = entry.Open();
            es.Write(bytes);
        }
        var docEntry = zip.CreateEntry("doc.json");
        using var ds = docEntry.Open();
        JsonSerializer.Serialize(ds, meta, Json);
    }

    public static Document Load(string path)
    {
        using var fs = File.OpenRead(path);
        return Load(fs);
    }

    public static Document Load(Stream stream)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var docEntry = zip.GetEntry("doc.json") ?? throw new InvalidDataException(CoreText.T("Нет doc.json"));
        DocMeta meta;
        using (var ds = docEntry.Open())
            meta = JsonSerializer.Deserialize<DocMeta>(ds, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? throw new InvalidDataException(CoreText.T("Пустой doc.json"));

        var list = new List<Layer>();
        foreach (var lm in meta.Layers)
        {
            var entry = zip.GetEntry(lm.File) ?? throw new InvalidDataException(CoreText.F("Нет {0}", lm.File));
            using var es = entry.Open();
            using var ms = new MemoryStream();
            es.CopyTo(ms);
            var bytes = ms.ToArray();
            if (bytes.Length != meta.Width * meta.Height * 4) throw new InvalidDataException(CoreText.T("Неверный размер слоя"));
            var px = new uint[meta.Width * meta.Height];
            Buffer.BlockCopy(bytes, 0, px, 0, bytes.Length);
            list.Add(new Layer(meta.Width, meta.Height, lm.Name, px) { Visible = lm.Visible, Locked = lm.Locked });
        }
        var result = Document.FromLayers(meta.Width, meta.Height, list);
        result.SheetCellWidth = meta.SheetW ?? 0;
        result.SheetCellHeight = meta.SheetH ?? 0;
        if (meta.Palette is { Count: > 0 })
        {
            result.Palette = meta.Palette.Select(h => Pixel.TryParseHex(h, out var c) ? c : Pixel.Empty).Where(c => !Pixel.IsEmpty(c)).ToList();
            result.PaletteName = meta.PaletteName;
        }
        if (meta.Rsi is { } r)
            result.Rsi = new RsiBinding(r.Folder, r.State, r.Directions, r.Frames, r.FrameW, r.FrameH, r.Delays.Select(x => x.ToArray()).ToArray(), r.Rows?.ToArray(), r.FrameNames?.ToArray());
        result.ExportProfile = meta.Export;
        return result;
    }
}
