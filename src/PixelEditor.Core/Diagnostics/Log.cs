// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Text;

namespace PixelEditor.Core;

public enum LogLevel { Debug = 0, Info = 1, Warning = 2, Error = 3 }

/// <summary>
/// Error and event log. Entries are always in English (not localized) so they are convenient to send in bug reports.
/// One file per day: <c>%AppData%\PixelEditor\logs\pixed-yyyyMMdd.log</c> (the data folder is overridden by PIXED_DATA_DIR).
/// A file larger than <see cref="MaxFileBytes"/> switches to the next part (.1, .2), files older than <see cref="KeepDays"/> days are deleted.
/// Thread safe; writing never throws: the log must not crash the program.
/// </summary>
public static class Log
{
    private static readonly object Gate = new();
    private static string? _directory;
    private static DateTime _lastCleanup = DateTime.MinValue;

    /// <summary>How many bytes a file may hold before switching to the next part.</summary>
    public static long MaxFileBytes { get; set; } = 2 * 1024 * 1024;

    /// <summary>How many days to keep old files.</summary>
    public static int KeepDays { get; set; } = 14;

    /// <summary>Entries below this level are dropped.</summary>
    public static LogLevel MinimumLevel { get; set; } = LogLevel.Info;

    /// <summary>The program name at the start of the file (pixed, pixed-mcp, PixelEditor).</summary>
    public static string Application { get; set; } = "PixelEditor";

    /// <summary>Every written line (for the diagnostics window and tests).</summary>
    public static event Action<string>? Written;

    /// <summary>The log folder; by default logs in the application data folder.</summary>
    public static string Directory
    {
        get => _directory ?? Path.Combine(AppPaths.DataDirectory, "logs");
        set => _directory = value;
    }

    /// <summary>The file currently being written to.</summary>
    public static string CurrentFile => PathFor(DateTime.Now, 0);

    public static void Debug(string message, string? component = null) => Write(LogLevel.Debug, component, message, null);
    public static void Info(string message, string? component = null) => Write(LogLevel.Info, component, message, null);
    public static void Warning(string message, string? component = null, Exception? ex = null) => Write(LogLevel.Warning, component, message, ex);
    public static void Error(string message, Exception? ex = null, string? component = null) => Write(LogLevel.Error, component, message, ex);

    /// <summary>An error with context: "Failed to save project" and the exception details with the stack.</summary>
    public static void Error(Exception ex, string context, string? component = null) => Write(LogLevel.Error, component, context, ex);

    public static void Write(LogLevel level, string? component, string message, Exception? ex)
    {
        try
        {
            if (level < MinimumLevel) return;
            var sb = new StringBuilder();
            sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append(" [").Append(level.ToString().ToUpperInvariant()).Append("] ");
            sb.Append('[').Append(Application).Append(']');
            if (!string.IsNullOrEmpty(component)) sb.Append(" [").Append(component).Append(']');
            sb.Append(' ').Append(message);
            for (var e = ex; e != null; e = e.InnerException)
            {
                sb.AppendLine().Append("  ").Append(e.GetType().FullName).Append(": ").Append(e.Message);
                if (!string.IsNullOrEmpty(e.StackTrace)) sb.AppendLine().Append(e.StackTrace);
            }
            var line = sb.ToString();
            lock (Gate)
            {
                System.IO.Directory.CreateDirectory(Directory);
                File.AppendAllText(ChoosePart(DateTime.Now), line + Environment.NewLine, Encoding.UTF8);
                Cleanup();
            }
            Written?.Invoke(line);
        }
        catch
        {
            // the log must not crash the program: no rights, disk full and so on
        }
    }

    /// <summary>The first file of the day that is not full yet.</summary>
    private static string ChoosePart(DateTime day)
    {
        for (int part = 0; part < 100; part++)
        {
            var path = PathFor(day, part);
            if (!File.Exists(path) || new FileInfo(path).Length < MaxFileBytes) return path;
        }
        return PathFor(day, 99);
    }

    private static string PathFor(DateTime day, int part) =>
        Path.Combine(Directory, $"pixed-{day:yyyyMMdd}" + (part == 0 ? "" : "." + part) + ".log");

    /// <summary>Deletes files older than KeepDays; checked at most once an hour.</summary>
    private static void Cleanup()
    {
        if (DateTime.Now - _lastCleanup < TimeSpan.FromHours(1)) return;
        _lastCleanup = DateTime.Now;
        foreach (var file in System.IO.Directory.GetFiles(Directory, "pixed-*.log"))
            if (File.GetLastWriteTime(file) < DateTime.Now.AddDays(-Math.Max(1, KeepDays)))
                try { File.Delete(file); } catch { }
    }

    /// <summary>Log files from newest to oldest: name, size, modification time.</summary>
    /// <summary>"pixed-20261117.log" is part 0, "pixed-20261117.3.log" is part 3: sort by day, then by number.</summary>
    private static (string Day, int Part) PartKey(string name)
    {
        var core = Path.GetFileNameWithoutExtension(name)["pixed-".Length..];      // 20261117 or 20261117.3
        int dot = core.IndexOf('.');
        return dot < 0 ? (core, 0) : (core[..dot], int.TryParse(core[(dot + 1)..], out var n) ? n : 0);
    }

    public static IReadOnlyList<LogFileInfo> Files()
    {
        try
        {
            if (!System.IO.Directory.Exists(Directory)) return [];
            return System.IO.Directory.GetFiles(Directory, "pixed-*.log").Select(f => new FileInfo(f))
                .OrderByDescending(f => PartKey(f.Name))   // newest first: by day and part number (the write time of quickly created files is the same)
                .Select(f => new LogFileInfo(f.Name, f.Length, f.LastWriteTime)).ToList();
        }
        catch { return []; }
    }

    /// <summary>
    /// The latest log entries (oldest first). minimum: only this level and above; includeStacks: with the stack trace
    /// (by default the "   at ..." lines are dropped: the exception type and message stay, and the answer is shorter for an AI).
    /// Reads files from newest to oldest until maxEntries entries are collected.
    /// </summary>
    public static IReadOnlyList<string> Tail(int maxEntries = 50, LogLevel minimum = LogLevel.Info, bool includeStacks = false)
    {
        maxEntries = Math.Clamp(maxEntries, 1, 1000);
        var result = new List<string>();
        try
        {
            foreach (var file in Files())
            {
                var entries = new List<string>();
                string[] lines;
                lock (Gate) lines = File.ReadAllLines(Path.Combine(Directory, file.Name), Encoding.UTF8);
                StringBuilder? current = null;
                LogLevel level = LogLevel.Info;
                void Flush() { if (current != null && level >= minimum) entries.Add(current.ToString()); current = null; }
                foreach (var line in lines)
                {
                    if (TryParseLevel(line, out var lv)) { Flush(); current = new StringBuilder(line); level = lv; }
                    else if (current != null && (includeStacks || !line.TrimStart().StartsWith("at ", StringComparison.Ordinal))) current.AppendLine().Append(line);
                }
                Flush();
                result.InsertRange(0, entries);
                if (result.Count >= maxEntries) break;
            }
        }
        catch { /* the log could not be read: return what we have */ }
        return result.Count > maxEntries ? result.GetRange(result.Count - maxEntries, maxEntries) : result;
    }

    private static bool TryParseLevel(string line, out LogLevel level)
    {
        level = LogLevel.Info;
        // «2026-11-16 12:00:00.123 [ERROR] ...»
        if (line.Length < 30 || line[4] != '-' || line[10] != ' ' || line[23] != ' ' || line[24] != '[') return false;
        int end = line.IndexOf(']', 25);
        return end > 0 && Enum.TryParse(line[25..end], true, out level);
    }

    /// <summary>For tests: reset the cleanup timer.</summary>
    public static void ResetCleanupTimer() => _lastCleanup = DateTime.MinValue;
}

public sealed record LogFileInfo(string Name, long Bytes, DateTime Modified);
