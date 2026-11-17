// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Text.RegularExpressions;
using PixelEditor.ViewModels;
using Xunit;

namespace PixelEditor.ViewModels.Tests;

/// <summary>
/// Localization. The shared Loc.Instance instance stays Russian in tests (the other tests depend on it),
/// so language switching is checked on separate Loc instances with a temporary user language folder.
/// </summary>
public class LocalizationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pixed-loc-tests-" + Guid.NewGuid().ToString("N"));

    public LocalizationTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private Loc NewLoc() => new(_dir);

    [Fact]
    public void Default_IsRussian_AndSharedInstanceIsRussian()
    {
        Assert.Equal("ru", Loc.Instance.CurrentLanguage);
        Assert.Equal("Пипетка: #FF0000", Loc.T("canvas.eyedropper", "#FF0000"));
        Assert.Equal("Файл", NewLoc()["menu.file"].TrimStart('_'));
    }

    [Fact]
    public void Format_SubstitutesArguments()
    {
        var loc = NewLoc();
        Assert.Equal("Холст обрезан по выделению: 12×7", loc.Get("canvas.canvas_cropped_to_selection", 12, 7));
        Assert.True(loc.SetLanguage("en"));
        Assert.Equal("Canvas cropped to selection: 12×7", loc.Get("canvas.canvas_cropped_to_selection", 12, 7));
    }

    [Fact]
    public void MissingKey_FallsBackToRussian_ThenToKey()
    {
        File.WriteAllText(Path.Combine(_dir, "xx.json"), "{ \"language.name\": \"Test\", \"menu.new\": \"XX-new\" }");
        var loc = NewLoc();
        Assert.Contains(loc.Available, l => l.Code == "xx" && l.Name == "Test");
        Assert.True(loc.SetLanguage("xx"));
        Assert.Equal("XX-new", loc["menu.new"]);                     // present in the language
        Assert.Equal("Открыть проект…", loc["menu.open_project"]);   // absent in xx: Russian
        Assert.Equal("no.such.key", loc["no.such.key"]);             // absent everywhere: the key itself
        Assert.Equal("no.such.key", loc.Get("no.such.key", 1, 2));
    }

    [Fact]
    public void UserFile_CanOverrideBuiltInLanguage()
    {
        File.WriteAllText(Path.Combine(_dir, "en.json"), "{ \"menu.new\": \"Fresh\" }");
        var loc = NewLoc();
        loc.SetLanguage("en");
        Assert.Equal("Fresh", loc["menu.new"]);
        Assert.Equal("Open project…", loc["menu.open_project"]); // the rest comes from the built-in en
    }

    [Fact]
    public void SetLanguage_RaisesIndexerPropertyChanged_AndEvent()
    {
        var loc = NewLoc();
        var changed = new List<string?>();
        int events = 0;
        loc.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        loc.LanguageChanged += () => events++;

        Assert.True(loc.SetLanguage("en"));
        Assert.Contains("Item[]", changed);
        Assert.Equal(1, events);
        Assert.Equal("en", loc.CurrentLanguage);

        changed.Clear();
        Assert.True(loc.SetLanguage("en")); // the same language: nothing is raised
        Assert.Empty(changed);
        Assert.Equal(1, events);
    }

    [Fact]
    public void UnknownLanguage_IsRejected()
    {
        var loc = NewLoc();
        Assert.False(loc.SetLanguage("zz"));
        Assert.Equal("ru", loc.CurrentLanguage);
        Assert.True(loc.SetLanguage(null)); // empty: Russian by default
    }

    [Fact]
    public void Available_ListsBuiltInLanguages_RussianFirst()
    {
        var list = NewLoc().Available;
        Assert.Equal("ru", list[0].Code);
        Assert.Contains(list, l => l.Code == "en" && l.Name == "English");
    }

    [Fact]
    public void RussianAndEnglish_HaveTheSameKeys()
    {
        var loc = NewLoc();
        var ru = loc.Keys("ru").ToHashSet();
        var en = loc.Keys("en").ToHashSet();
        Assert.True(ru.Count > 300);
        Assert.Empty(ru.Except(en).OrderBy(k => k).ToList()); // keys without an English translation
        Assert.Empty(en.Except(ru).OrderBy(k => k).ToList()); // extra keys in English
    }

    [Fact]
    public void RussianAndEnglish_HaveTheSamePlaceholders()
    {
        var loc = NewLoc();
        var bad = new List<string>();
        foreach (var key in loc.Keys("ru"))
        {
            loc.SetLanguage("ru"); var r = loc[key];
            loc.SetLanguage("en"); var e = loc[key];
            if (!Placeholders(r).SequenceEqual(Placeholders(e))) bad.Add(key);
            if (string.IsNullOrWhiteSpace(e)) bad.Add(key + " (empty)");
        }
        Assert.Empty(bad);
    }

    private static string[] Placeholders(string s) =>
        Regex.Matches(s, @"\{(\d+)").Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x).ToArray();

    [Fact]
    public void MainViewModel_LanguageMenu_SwitchesAndSavesToSettings()
    {
        using var rig = new Rig();
        var loc = NewLoc();
        var vm = new MainViewModel(rig.Services, rig.Settings, loc);

        Assert.Equal(["ru", "en"], vm.Languages.Select(l => l.Code).Take(2));
        Assert.True(vm.Languages[0].IsChecked);

        vm.SetLanguageCommand.Execute("en");
        Assert.Equal("en", loc.CurrentLanguage);
        Assert.Equal("en", rig.Settings.Language);
        Assert.Equal("en", AppSettings.Load(Path.Combine(rig.Dir, "settings")).Language);
        Assert.True(vm.Languages.First(l => l.Code == "en").IsChecked);
        Assert.False(vm.Languages.First(l => l.Code == "ru").IsChecked);
    }

    [Fact]
    public void Settings_Language_DefaultsToRussian_AndRoundTrips()
    {
        var s = AppSettings.Load(Path.Combine(_dir, "s"));
        Assert.Equal("ru", s.Language);
        s.Language = "en";
        s.Save();
        Assert.Equal("en", AppSettings.Load(Path.Combine(_dir, "s")).Language);
    }
}
