// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Text.RegularExpressions;
using PixelEditor.Core;
using PixelEditor.Core.Ops;

namespace PixelEditor.Tests;

public class CoreLocalizationTests
{
    private static readonly Regex Cyr = new("[А-Яа-яЁё]");

    [Fact]
    public void EnglishCatalogs_HaveNoRussian()
    {
        using var _ = CoreText.Scope("en");
        var texts = new List<string>();
        foreach (var o in Operations.All.Where(o => o.Source == "builtin")) { texts.Add(o.Description); texts.AddRange(o.Parameters); }
        foreach (var c in Checks.All.Where(c => c.Source == "builtin")) { texts.Add(c.Description); texts.AddRange(c.Parameters); }
        foreach (var t in BuiltInTemplates.All) { texts.Add(t.Name); texts.Add(t.Description); texts.Add(t.Json); }
        foreach (var p in BuiltInPalettes.All) { texts.Add(p.Name); texts.Add(p.Description); }
        foreach (var s in BuiltInSheetTemplates.All) { texts.Add(s.Name); texts.Add(s.Description); texts.AddRange(s.Rows); texts.AddRange(s.Frames.Select(f => f.Name)); }
        var bad = texts.Where(t => Cyr.IsMatch(t)).Take(5).ToList();
        Assert.True(bad.Count == 0, "Не переведено: " + string.Join(" | ", bad.Select(b => b.Length > 80 ? b[..80] : b)));
    }

    [Fact]
    public void TemplatesAndPalettes_AreFoundByNameInAnyLanguage()
    {
        var lib = new PipelineLibrary(Path.Combine(Path.GetTempPath(), "pixed-nolib-" + Guid.NewGuid().ToString("N")));
        using (CoreText.Scope("en"))
        {
            Assert.Equal("Clean up sprite", lib.Find("Clean up sprite")!.Name);
            Assert.Equal("Clean up sprite", lib.Find("Почистить спрайт")!.Name);           // a Russian name is found in the English mode too
        }
        using (CoreText.Scope("ru"))
        {
            Assert.Equal("Почистить спрайт", lib.Find("clean up sprite")!.Name);          // and vice versa, case-insensitive
            Assert.Equal("Градации серого (8)", PaletteLibrary.Default.Find("Grayscale (8)")!.Name);
        }
    }

    [Fact]
    public void ErrorsAndChecks_SwitchLanguage()
    {
        using (CoreText.Scope("ru"))
            Assert.Contains("Неизвестная операция", Assert.Throws<ArgumentException>(() => Operations.Get("nope")).Message);
        using (CoreText.Scope("en"))
        {
            Assert.Contains("Unknown operation", Assert.Throws<ArgumentException>(() => Operations.Get("nope")).Message);
            var r = Checks.Get("maxColors").Run(new PixelImage(2, 2), new OpArgs(System.Text.Json.JsonDocument.Parse("{\"value\":4}").RootElement, null));
            Assert.Contains("colors", r.Message);
        }
    }

    [Fact]
    public void EveryKeyInTheTable_IsUsedByTheSources_OrAtLeastTranslated()
    {
        // the table must not contain empty translations or "lost" Russian letters
        using var s = typeof(CoreText).Assembly.GetManifestResourceStream("PixelEditor.Core.Locales.core.en.json")!;
        var map = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(s)!;
        Assert.NotEmpty(map);
        Assert.All(map, p => { Assert.NotEmpty(p.Value); Assert.False(Cyr.IsMatch(p.Value), p.Key); });
    }
}
