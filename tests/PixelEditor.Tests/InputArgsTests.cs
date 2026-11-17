// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Text;
using PixelEditor.Core;

namespace PixelEditor.Tests;

public class InputArgsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pixed-args-" + Guid.NewGuid().ToString("N"));

    public InputArgsTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private string Touch(string name, string text = "x")
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllText(p, text);
        return p;
    }

    [Fact]
    public void PlainArguments_AreReturnedUntouched()
    {
        var args = new InputArgs(() => new StringReader("never read"));
        foreach (var s in new[] { "scene.json", "{\"a\":1}", "Clean up sprite", "email@x", "--", "" })
        {
            var r = args.Resolve(s);
            Assert.False(r.Expanded);
            Assert.Equal(s, r.Value);
        }
        Assert.False(InputArgs.IsSpecial("@"));   // a lone @ is not a file reference
    }

    [Fact]
    public void AtFile_ReadsTheFile_AndToleratesBomAndKeepsBaseDir()
    {
        var path = Path.Combine(_dir, "p.json");
        File.WriteAllBytes(path, [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes("{\"name\":\"Привет\"}")]);
        var r = new InputArgs().Resolve("@" + path);
        Assert.True(r.Expanded);
        Assert.Equal("{\"name\":\"Привет\"}", r.Value);
        Assert.Equal(Path.GetFullPath(_dir), r.BaseDir);
    }

    [Fact]
    public void AtFile_Missing_IsAClearLocalizedError()
    {
        var ex = Assert.Throws<FileNotFoundException>(() => new InputArgs().Resolve("@" + Path.Combine(_dir, "nope.json")));
        Assert.Contains("не найден", ex.Message);
        Assert.Contains("nope.json", ex.Message);
        using (CoreText.Scope("en"))
            Assert.Contains("not found", Assert.Throws<FileNotFoundException>(() => new InputArgs().Resolve("@" + Path.Combine(_dir, "nope.json"))).Message);
    }

    [Fact]
    public void Stdin_IsReadOnce_StripsBom_AndSecondUseIsAnError()
    {
        var args = new InputArgs(() => new StringReader("﻿{\"x\":1}"));
        var r = args.Resolve("-");
        Assert.True(r.Expanded);
        Assert.Equal("{\"x\":1}", r.Value);
        Assert.Null(r.SourceFile);
        Assert.Null(r.BaseDir);
        Assert.Throws<ArgumentException>(() => args.Resolve("-"));
    }

    [Fact]
    public void EmptyInput_IsRejected()
    {
        var empty = Touch("empty.json", "  \n");
        Assert.Throws<ArgumentException>(() => new InputArgs().Resolve("@" + empty));
        Assert.Throws<ArgumentException>(() => new InputArgs(() => new StringReader("")).Resolve("-"));
    }

    // ---- glob ----

    [Fact]
    public void Glob_WithoutWildcards_ReturnsTheArgumentEvenIfMissing()
    {
        Assert.Equal(["nope\\a.png"], FileGlob.ExpandOne("nope\\a.png"));
        Assert.False(FileGlob.HasWildcard("dir*x\\a.png"));
        Assert.True(FileGlob.HasWildcard("dir\\a?.png"));
    }

    [Fact]
    public void Glob_ExpandsInNaturalOrder_CaseInsensitive_AndKeepsTheDirectoryPart()
    {
        foreach (var n in new[] { "frame10.png", "frame2.png", "frame1.png", "Frame3.PNG", "frame2.txt", "other.png" }) Touch(n);
        var found = FileGlob.ExpandOne(_dir + "\\frame*.png");
        Assert.Equal(["frame1.png", "frame2.png", "Frame3.PNG", "frame10.png"], found.Select(Path.GetFileName));
        Assert.All(found, f => Assert.StartsWith(_dir, f));

        Assert.Equal(["frame1.png", "frame2.png"], FileGlob.ExpandOne(_dir + "/frame?.png").Select(Path.GetFileName).Take(2));
        // mixed list: plain names stay in place, patterns expand in position
        var all = FileGlob.Expand(["first.png", _dir + "\\frame1?.png", "last.png"]);
        Assert.Equal(["first.png", "frame10.png", "last.png"], all.Select(Path.GetFileName));
    }

    [Fact]
    public void Glob_NoMatch_MissingFolderAndWildcardInFolder_AreErrors()
    {
        Touch("a.png");
        var ex = Assert.Throws<FileNotFoundException>(() => FileGlob.ExpandOne(_dir + "\\zzz*.png"));
        Assert.Contains("zzz*.png", ex.Message);
        Assert.Throws<DirectoryNotFoundException>(() => FileGlob.ExpandOne(Path.Combine(_dir, "missing") + "\\*.png"));
        Assert.Throws<ArgumentException>(() => FileGlob.ExpandOne(_dir + "*\\a.png?"));
    }

    [Fact]
    public void NaturalCompare_OrdersNumbersByValue()
    {
        var names = new[] { "f10", "f2", "f1", "f02", "f001", "g1", "F3" }.ToList();
        names.Sort(FileGlob.NaturalCompare);
        Assert.Equal(["f001", "f1", "f02", "f2", "F3", "f10", "g1"], names);
    }
}
