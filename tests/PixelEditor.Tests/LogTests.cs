// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using PixelEditor.Core;

namespace PixelEditor.Tests;

/// <summary>The error log: a file per day, English entries, the exception stack, levels, file parts, cleanup, thread safety.</summary>
/// <summary>The log is global (static): tests that write to it run apart from the others, otherwise entries of other tests get into the read.</summary>
[CollectionDefinition("Log", DisableParallelization = true)]
public class LogCollection;

[Collection("Log")]
public class LogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pixed-log-" + Guid.NewGuid().ToString("N"));
    private readonly string _oldDir = Log.Directory;
    private readonly long _oldMax = Log.MaxFileBytes;
    private readonly LogLevel _oldLevel = Log.MinimumLevel;

    public LogTests() { Log.Directory = _dir; Log.Application = "test"; Log.MinimumLevel = LogLevel.Info; Log.ResetCleanupTimer(); }

    public void Dispose()
    {
        Log.Directory = _oldDir; Log.MaxFileBytes = _oldMax; Log.MinimumLevel = _oldLevel;
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void Error_WritesLevelTimestampApplicationMessageAndExceptionWithStack()
    {
        try { throw new InvalidOperationException("boom", new ArgumentException("inner detail")); }
        catch (Exception ex) { Log.Error(ex, "Failed to save project", "io"); }

        var text = File.ReadAllText(Log.CurrentFile);
        Assert.Matches(@"^\d{4}-\d\d-\d\d \d\d:\d\d:\d\d\.\d{3} \[ERROR\] \[test\] \[io\] Failed to save project", text);
        Assert.Contains("System.InvalidOperationException: boom", text);
        Assert.Contains("System.ArgumentException: inner detail", text);   // the inner exception
        Assert.Contains("at PixelEditor.Tests.LogTests", text);             // the stack
    }

    [Fact]
    public void Levels_AreFiltered_AndMessagesAreEnglishRegardlessOfUiLanguage()
    {
        Log.Debug("hidden");
        Log.Info("visible info");
        Log.Warning("careful");
        var text = File.ReadAllText(Log.CurrentFile);
        Assert.DoesNotContain("hidden", text);
        Assert.Contains("[INFO] [test] visible info", text);
        Assert.Contains("[WARNING] [test] careful", text);
        Log.MinimumLevel = LogLevel.Error;
        Log.Warning("dropped");
        Assert.DoesNotContain("dropped", File.ReadAllText(Log.CurrentFile));

        // the UI language does not affect writing: the calling code writes the text, the log translates nothing
        using (CoreText.Scope("ru")) Log.Error("English only message");
        Assert.Contains("English only message", File.ReadAllText(Log.CurrentFile));
    }

    [Fact]
    public void BigFile_RollsToNextPart_AndOldFilesAreDeleted()
    {
        Log.MaxFileBytes = 200;
        for (int i = 0; i < 12; i++) Log.Info("line number " + i + " with some padding text to grow the file");
        var parts = Directory.GetFiles(_dir, "pixed-*.log");
        Assert.True(parts.Length >= 2, "файл должен разбиваться на части");
        Assert.Contains(parts, p => p.EndsWith(".1.log"));

        var old = Path.Combine(_dir, "pixed-20200101.log");
        File.WriteAllText(old, "old");
        File.SetLastWriteTime(old, DateTime.Now.AddDays(-60));
        Log.ResetCleanupTimer();
        Log.Info("trigger cleanup");
        Assert.False(File.Exists(old));
    }

    [Fact]
    public void WritingNeverThrows_WhenTheFolderIsUnusable_AndWorksFromManyThreads()
    {
        var blocker = Path.Combine(Path.GetTempPath(), "pixed-log-file-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(blocker, "x");                       // the "folder" is occupied by a file
        Log.Directory = Path.Combine(blocker, "sub");
        var ex = Record.Exception(() => Log.Error("cannot be written"));
        Assert.Null(ex);
        File.Delete(blocker);

        Log.Directory = _dir;
        Parallel.For(0, 200, i => Log.Info("parallel " + i));
        var lines = File.ReadAllLines(Log.CurrentFile).Count(l => l.Contains("parallel "));
        Assert.Equal(200, lines);
    }

    [Fact]
    public void Tail_ReturnsLatestEntriesFilteredByLevel_WithOrWithoutStacks_AndListsFiles()
    {
        Log.Info("first info");
        try { throw new InvalidOperationException("kaput"); } catch (Exception ex) { Log.Error(ex, "Operation X failed"); }
        Log.Warning("a warning");
        Log.Info("last info");

        var all = Log.Tail(10, LogLevel.Info);
        Assert.Equal(4, all.Count);
        Assert.Contains("first info", all[0]);
        Assert.Contains("last info", all[^1]);                       // the oldest first

        var errors = Log.Tail(10, LogLevel.Error);
        Assert.Single(errors);
        Assert.Contains("InvalidOperationException: kaput", errors[0]);
        Assert.DoesNotContain("   at ", errors[0]);                  // without the stack by default
        Assert.Contains("   at ", Log.Tail(10, LogLevel.Error, includeStacks: true)[0]);

        var warnings = Log.Tail(10, LogLevel.Warning);
        Assert.Equal(2, warnings.Count);                            // a warning and an error
        Assert.Equal(2, Log.Tail(2, LogLevel.Info).Count);          // a limit on the count, the last ones are taken
        Assert.Contains("last info", Log.Tail(2, LogLevel.Info)[^1]);

        var files = Log.Files();
        Assert.Single(files);
        Assert.StartsWith("pixed-", files[0].Name);
        Assert.True(files[0].Bytes > 0);
    }

    [Fact]
    public void Tail_ReadsAcrossRolledPartsAndSurvivesAnEmptyFolder()
    {
        Assert.Empty(Log.Tail(5));
        Log.MaxFileBytes = 150;
        for (int i = 0; i < 10; i++) Log.Info("entry " + i + " padding padding padding padding");
        var tail = Log.Tail(10, LogLevel.Info);
        Assert.Equal(10, tail.Count);
        Assert.Contains("entry 0 ", tail[0]);                       // entries from the old parts are found too
        Assert.Contains("entry 9 ", tail[^1]);
    }

    [Fact]
    public void AppInfo_IsVersion1_AndNamesTheAuthorAndLicense()
    {
        Assert.Equal("1.0.0", AppInfo.Version);
        Assert.Equal("GPL-3.0-or-later", AppInfo.License);
        Assert.Contains("mrl4an", AppInfo.Copyright);
        Assert.Contains("1.0.0", AppInfo.Banner);
    }
}
