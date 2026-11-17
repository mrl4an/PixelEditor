// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Text.Json;
using System.Text.Json.Nodes;
using PixelEditor.Core;
using PixelEditor.Core.Ops;

namespace PixelEditor.Tests;

public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pixed-lib-" + Guid.NewGuid().ToString("N"));
    public TempDir() => Directory.CreateDirectory(Path);
    public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
}

public class PaletteLibraryTests
{
    private static readonly uint Red = Pixel.Rgb(255, 0, 0);
    private static readonly uint Blue = Pixel.Rgb(0, 0, 255);

    [Fact]
    public void BuiltInPalettes_AreWellFormed()
    {
        var all = BuiltInPalettes.All;
        Assert.True(all.Count >= 6);
        Assert.Equal(all.Count, all.Select(p => p.Id).Distinct().Count());
        Assert.Equal(all.Count, all.Select(p => p.Name).Distinct().Count());
        Assert.All(all, p =>
        {
            Assert.True(p.IsSystem);
            Assert.Null(p.FilePath);
            Assert.False(string.IsNullOrWhiteSpace(p.Description));
            Assert.All(p.Colors, c => Assert.Equal(255, Pixel.A(c)));
            Assert.Equal(p.Colors.Count, p.Colors.Distinct().Count()); // no repeats inside the palette
        });
        Assert.Equal(16, all.Single(p => p.Name == "PICO-8").Colors.Count);
        Assert.Equal(16, all.Single(p => p.Name == "DawnBringer 16").Colors.Count);
        Assert.Equal(16, all.Single(p => p.Name == "Sweetie 16").Colors.Count);
        Assert.Equal(32, all.Single(p => p.Name == "Endesga 32").Colors.Count);
        Assert.Equal(4, all.Single(p => p.Name == "Game Boy").Colors.Count);
        Assert.Equal(Pixel.Rgb(0x1D, 0x2B, 0x53), all.Single(p => p.Name == "PICO-8").Colors[1]);
    }

    [Fact]
    public void SaveFindAndList_UserPalette()
    {
        using var dir = new TempDir();
        var lib = new PaletteLibrary(dir.Path);
        var saved = lib.Save("Мой проект", "Тёплые тона", [Red, Blue, Red]);

        Assert.False(saved.IsSystem);
        Assert.Equal("Мой проект", saved.Name);
        Assert.Equal("Тёплые тона", saved.Description);
        Assert.Equal([Red, Blue], saved.Colors); // the repeat is removed, the order is kept
        Assert.True(File.Exists(saved.FilePath));

        var all = lib.All();
        Assert.Equal(BuiltInPalettes.All.Count + 1, all.Count);
        Assert.All(all.Take(BuiltInPalettes.All.Count), p => Assert.True(p.IsSystem)); // system ones first
        Assert.Equal(saved.Id, all[^1].Id);

        Assert.Equal(saved.Id, lib.Find("мой ПРОЕКТ")!.Id);   // by name, case-insensitive
        Assert.Equal(saved.Name, lib.Find(saved.Id)!.Name);   // by id
        Assert.Equal("PICO-8", lib.Find("pico-8")!.Name);     // a system one by name
        Assert.Null(lib.Find("нет такой"));
    }

    [Fact]
    public void Save_RejectsBadInput_AndNameClashes()
    {
        using var dir = new TempDir();
        var lib = new PaletteLibrary(dir.Path);
        Assert.Throws<ArgumentException>(() => lib.Save("  ", "", [Red]));
        Assert.Throws<ArgumentException>(() => lib.Save("Пустая", "", []));
        Assert.Throws<ArgumentException>(() => lib.Save("Прозрачная", "", [Pixel.Empty]));

        var sys = Assert.Throws<ArgumentException>(() => lib.Save("pico-8", "", [Red]));
        Assert.Contains("системной", sys.Message);

        lib.Save("Моя", "", [Red]);
        var dup = Assert.Throws<ArgumentException>(() => lib.Save("моя", "", [Blue]));
        Assert.Contains("уже существует", dup.Message);
    }

    [Fact]
    public void Save_WithExistingId_UpdatesAndRenamesInPlace()
    {
        using var dir = new TempDir();
        var lib = new PaletteLibrary(dir.Path);
        var first = lib.Save("Старая", "", [Red]);
        var updated = lib.Save("Новая", "описание", [Red, Blue], first.Id);

        Assert.Equal(first.FilePath, updated.FilePath);       // the same file
        Assert.Equal("Новая", updated.Name);
        Assert.Equal(2, updated.Colors.Count);
        Assert.Single(lib.All(), p => !p.IsSystem);            // no second one was created
        lib.Save("Новая", "ещё", [Blue], first.Id);           // its own name does not count as a conflict
    }

    [Fact]
    public void SystemPalettes_CannotBeChangedOrDeleted()
    {
        using var dir = new TempDir();
        var lib = new PaletteLibrary(dir.Path);
        var pico = BuiltInPalettes.All[0];
        Assert.False(lib.Delete(pico.Id));
        Assert.Throws<InvalidOperationException>(() => lib.Save("Другое имя", "", [Red], pico.Id));
        Assert.Equal(BuiltInPalettes.All.Count, lib.All().Count);
    }

    [Fact]
    public void Delete_RemovesOnlyUserPalette_AndIsRepeatable()
    {
        using var dir = new TempDir();
        var lib = new PaletteLibrary(dir.Path);
        var a = lib.Save("А", "", [Red]);
        var b = lib.Save("Б", "", [Blue]);
        Assert.True(lib.Delete(a.Id));
        Assert.False(File.Exists(a.FilePath));
        Assert.True(File.Exists(b.FilePath));
        Assert.False(lib.Delete(a.Id));          // deleting again breaks nothing
        Assert.False(lib.Delete("user:нет"));
        Assert.Contains(lib.All(), p => p.Id == b.Id);
    }

    [Fact]
    public void BrokenFiles_AreSkipped_AndSameNamesGetUniqueFileNames()
    {
        using var dir = new TempDir();
        var lib = new PaletteLibrary(dir.Path);
        File.WriteAllText(Path.Combine(dir.Path, "broken.json"), "{ not json");
        File.WriteAllText(Path.Combine(dir.Path, "empty.json"), """{"name":"Пусто","colors":[]}""");
        File.WriteAllText(Path.Combine(dir.Path, "noname.json"), """{"colors":["#FF0000"]}""");
        Assert.Equal(BuiltInPalettes.All.Count, lib.All().Count);

        var a = lib.Save("Тест!", "", [Red]);
        var b = lib.Save("Тест?", "", [Blue]); // different titles but the same file name
        Assert.NotEqual(a.FilePath, b.FilePath);
    }

    [Theory]
    [InlineData("Мой Проект 2", "мой-проект-2")]
    [InlineData("  A/B\\C:D  ", "a-b-c-d")]
    [InlineData("***", "palette")]
    public void Slug_IsFileSafe(string name, string expected) => Assert.Equal(expected, PaletteLibrary.Slug(name));

    [Fact]
    public void DataDirectory_CanBeOverriddenByEnvironment()
    {
        var old = Environment.GetEnvironmentVariable("PIXED_DATA_DIR");
        try
        {
            Environment.SetEnvironmentVariable("PIXED_DATA_DIR", "C:\\somewhere");
            Assert.Equal("C:\\somewhere", AppPaths.DataDirectory);
            Assert.Equal(Path.Combine("C:\\somewhere", "palettes"), AppPaths.PalettesDirectory);
            Assert.Equal(Path.Combine("C:\\somewhere", "pipelines"), AppPaths.PipelinesDirectory);
        }
        finally { Environment.SetEnvironmentVariable("PIXED_DATA_DIR", old); }
    }

    [Fact]
    public void ApplyPalette_AcceptsLibraryNames_AndReportsUnknown()
    {
        var img = new PixelImage(4, 4);
        for (int i = 0; i < 16; i++) img.Pixels[i] = Pixel.Rgb((byte)(i * 16), (byte)(255 - i * 16), (byte)(i * 8));

        var ok = Pipeline.Parse("""{"steps":[{"op":"applyPalette","paletteName":"game boy"}],"checks":[{"check":"onlyPalette","paletteName":"Game Boy"}]}""").Run(img);
        Assert.True(ok.Success, string.Join("; ", ok.Log.Select(l => l.Error).Concat(ok.Checks.Select(c => c.Message))));
        Assert.True(ok.Image.DistinctColors() <= 4);

        var bad = Pipeline.Parse("""{"steps":[{"op":"applyPalette","paletteName":"нет такой палитры"}]}""").Run(img);
        Assert.False(bad.Ok);
        Assert.Contains("не найдена", bad.Log[0].Error);
    }

    [Fact]
    public void Project_PersistsPaletteAndName()
    {
        var doc = new Document(2, 2) { Palette = [Red, Blue], PaletteName = "Моя" };
        using var ms = new MemoryStream();
        ProjectIO.Save(doc, ms);
        ms.Position = 0;
        var back = ProjectIO.Load(ms);
        Assert.Equal([Red, Blue], back.Palette);
        Assert.Equal("Моя", back.PaletteName);

        using var ms2 = new MemoryStream();
        ProjectIO.Save(new Document(2, 2), ms2);
        ms2.Position = 0;
        var plain = ProjectIO.Load(ms2);
        Assert.Null(plain.Palette);
        Assert.Null(plain.PaletteName);
    }
}

public class PipelineLibraryTests
{
    private static PixelImage Sample()
    {
        var img = new PixelImage(40, 30);
        for (int y = 0; y < 30; y++)
            for (int x = 0; x < 40; x++)
                if ((x - 20) * (x - 20) + (y - 15) * (y - 15) < 150)
                    img.Set(x, y, Pixel.Rgb((byte)(x * 6), (byte)(y * 8), (byte)(255 - x * 4)));
        return img;
    }

    private const string Simple = """{"steps":[{"op":"flipH"}],"checks":[{"check":"maxColors","value":99}]}""";

    [Fact]
    public void BuiltInTemplates_AreValidAndRunnable()
    {
        var all = BuiltInTemplates.All;
        Assert.True(all.Count >= 8);
        Assert.Equal(all.Count, all.Select(t => t.Id).Distinct().Count());
        Assert.Equal(all.Count, all.Select(t => t.Name).Distinct().Count());
        foreach (var t in all)
        {
            Assert.True(t.IsSystem);
            Assert.False(string.IsNullOrWhiteSpace(t.Description));
            var pipeline = Pipeline.Parse(t.Json);
            Assert.Empty(pipeline.Validate());
            Assert.True(pipeline.Steps.Count + pipeline.CheckSteps.Count > 0, t.Name);
            var result = pipeline.Run(Sample());
            Assert.True(result.Ok, $"{t.Name}: " + string.Join("; ", result.Log.Where(l => l.Error != null).Select(l => $"{l.Op}: {l.Error}")));
        }
    }

    [Fact]
    public void BuiltInTemplates_ProduceWhatTheyPromise()
    {
        PipelineResult Run(string id, Dictionary<string, object?>? values = null) =>
            Pipeline.Parse(BuiltInTemplates.All.Single(t => t.Id == "system:" + id).Json).Run(Sample(), null, values);

        var down = Run("downscale-pixel-art", new() { ["size"] = 16 });
        Assert.True(down.Image.Width <= 16 && down.Image.Height <= 16);
        Assert.True(down.Success);

        var gb = Run("apply-palette", new() { ["palette"] = "Game Boy", ["dither"] = true });
        Assert.True(gb.Image.DistinctColors() <= 4);
        Assert.True(gb.Success);

        var pico = Run("apply-palette");
        Assert.True(pico.Success);

        var sprite = Run("photo-to-pixel-art", new() { ["size"] = 32, ["outline"] = "black" });
        Assert.True(sprite.Image.Width <= 34 && sprite.Image.Height <= 34);
        Assert.True(sprite.Success, string.Join("; ", sprite.Checks.Where(c => !c.Passed).Select(c => c.Check + ": " + c.Message)));

        var checkOnly = Run("check-ready");
        Assert.Equal(40, checkOnly.Image.Width); // changes nothing
        Assert.Empty(checkOnly.Log);
        Assert.NotEmpty(checkOnly.Checks);
    }

    [Fact]
    public void Save_InjectsNameAndDescription_AndRoundTrips()
    {
        using var dir = new TempDir();
        var lib = new PipelineLibrary(dir.Path);
        var info = lib.Save("Отразить", "Зеркалит по горизонтали", Simple);

        Assert.False(info.IsSystem);
        Assert.Equal("Отразить", info.Name);
        Assert.Equal("Зеркалит по горизонтали", info.Description);
        Assert.Equal((1, 1), (info.StepCount, info.CheckCount));
        var file = JsonNode.Parse(File.ReadAllText(info.FilePath!))!.AsObject();
        Assert.Equal("Отразить", file["name"]!.GetValue<string>());
        Assert.True(Pipeline.Load(info.FilePath!).Run(Sample()).Ok); // the steps run (the color check on a gradient does not have to pass)

        Assert.Equal(info.Id, lib.Find("отразить")!.Id);
        Assert.Equal(BuiltInTemplates.All.Count + 1, lib.All().Count);
        Assert.Equal(info.Id, lib.All()[^1].Id);
    }

    [Fact]
    public void Save_AcceptsBareStepArray_AndOverridesNameInsideJson()
    {
        using var dir = new TempDir();
        var lib = new PipelineLibrary(dir.Path);
        var a = lib.Save("Массив", "", """[{"op":"flipV"}]""");
        Assert.Equal(1, a.StepCount);

        var b = lib.Save("Свой заголовок", "новое", """{"name":"Старое имя","description":"старое","steps":[{"op":"flipV"}]}""");
        Assert.Equal("Свой заголовок", b.Name);
        Assert.Equal("новое", b.Description);
    }

    [Fact]
    public void Save_RejectsBrokenTemplates()
    {
        using var dir = new TempDir();
        var lib = new PipelineLibrary(dir.Path);
        Assert.Throws<ArgumentException>(() => lib.Save("", "", Simple));
        Assert.ThrowsAny<JsonException>(() => lib.Save("Мусор", "", "not json"));
        Assert.Throws<FormatException>(() => lib.Save("Форма", "", """{"nothing":1}"""));

        var unknown = Assert.Throws<ArgumentException>(() => lib.Save("Ошибка", "", """{"steps":[{"op":"нет"}]}"""));
        Assert.Contains("Неизвестная операция", unknown.Message);
        var unknownCheck = Assert.Throws<ArgumentException>(() => lib.Save("Ошибка2", "", """{"checks":[{"check":"нет"}]}"""));
        Assert.Contains("Неизвестная проверка", unknownCheck.Message);

        var empty = Assert.Throws<ArgumentException>(() => lib.Save("Пусто", "", """{"steps":[]}"""));
        Assert.Contains("ни шагов, ни проверок", empty.Message);
        Assert.Empty(Directory.GetFiles(dir.Path)); // nothing was written
    }

    [Fact]
    public void Save_NameClashes_AndUpdate()
    {
        using var dir = new TempDir();
        var lib = new PipelineLibrary(dir.Path);
        var sys = Assert.Throws<ArgumentException>(() => lib.Save("почистить спрайт", "", Simple));
        Assert.Contains("системным", sys.Message);

        var first = lib.Save("Мой", "", Simple);
        Assert.Throws<ArgumentException>(() => lib.Save("мой", "", Simple));

        var updated = lib.Save("Мой переименованный", "d", """{"steps":[{"op":"flipV"}]}""", first.Id);
        Assert.Equal(first.FilePath, updated.FilePath);
        Assert.Single(lib.All(), t => !t.IsSystem);
        Assert.Equal("Мой переименованный", updated.Name);
        Assert.Equal(0, updated.CheckCount);

        Assert.Throws<InvalidOperationException>(() => lib.Save("Копия", "", Simple, BuiltInTemplates.All[0].Id));
    }

    [Fact]
    public void Delete_UserOnly()
    {
        using var dir = new TempDir();
        var lib = new PipelineLibrary(dir.Path);
        var mine = lib.Save("Мой", "", Simple);
        Assert.False(lib.Delete(BuiltInTemplates.All[0].Id));
        Assert.True(lib.Delete(mine.Id));
        Assert.False(File.Exists(mine.FilePath));
        Assert.False(lib.Delete(mine.Id));
        Assert.Equal(BuiltInTemplates.All.Count, lib.All().Count);
    }

    [Fact]
    public void ExternalFiles_AreListed_NameFallsBackToFileName_BrokenSkipped()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "no-name.json"), """{"steps":[{"op":"trim"}]}""");
        File.WriteAllText(Path.Combine(dir.Path, "broken.json"), "[[[");
        var lib = new PipelineLibrary(dir.Path);
        var user = lib.All().Where(t => !t.IsSystem).ToList();
        Assert.Single(user);
        Assert.Equal("no-name", user[0].Name);
    }

    [Fact]
    public void Validate_ListsUnknownOperationsAndChecks()
    {
        var p = Pipeline.Parse("""{"steps":[{"op":"flipH"},{"op":"zzz"}],"checks":[{"check":"qqq"}]}""");
        var problems = p.Validate();
        Assert.Equal(2, problems.Count);
        Assert.StartsWith("шаг 2", problems[0]);
        Assert.StartsWith("проверка 1", problems[1]);
        Assert.Empty(Pipeline.Parse(Simple).Validate());
    }

    [Fact]
    public void PhotoTo128Template_ShrinksABigPhotoToAtMost128AndPassesItsChecks()
    {
        var photo = new PixelImage(400, 300);
        for (int y = 0; y < 300; y++)
            for (int x = 0; x < 400; x++)
            {
                double d = Math.Sqrt((x - 200) * (x - 200) + (y - 150) * (y - 150));
                photo.Set(x, y, d < 110 ? Pixel.Rgb((byte)(200 - d), (byte)(90 + x / 8), 60) : Pixel.Rgb(230, 230, 232));
            }
        var (template, preset) = new PipelineLibrary(Path.Combine(Path.GetTempPath(), "pixed-none")).FindWithPreset("photo-to-sprite-128")!.Value;
        var result = Pipeline.Parse(template.Json).WithPreset(preset).Run(photo);
        Assert.True(result.Ok);
        Assert.True(result.ChecksPassed);
        Assert.InRange(result.Image.Width, 100, 130);
        Assert.True(result.Image.Height <= 130);
    }
}
