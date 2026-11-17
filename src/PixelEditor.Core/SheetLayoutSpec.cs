// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Globalization;

namespace PixelEditor.Core;

/// <summary>
/// The layout a sprite sheet must have: frame size, row names in order, frame names and delays. It comes from a sheet template, from the
/// current animation sheet / RSI state (<see cref="RsiBinding"/>) or from the command line, goes into the chat prompt as the REQUIRED target
/// and is used to check and complete the answer (<see cref="Differences"/>, <see cref="Complete"/>).
/// </summary>
/// <param name="Delays">Optional delays per row and frame (seconds); null: every row uses the delays of <paramref name="Frames"/>.</param>
public sealed record SheetLayoutSpec(int FrameWidth, int FrameHeight, IReadOnlyList<string> Rows, IReadOnlyList<SheetFrame> Frames, double[][]? Delays = null)
{
    public int Columns => Frames.Count;

    /// <summary>The delay of one frame of one row.</summary>
    public double DelayAt(int row, int col) =>
        Delays != null && row < Delays.Length && col < Delays[row].Length ? Delays[row][col] : Frames[col].Delay;

    /// <summary>The rows of a template with a frame size; <paramref name="delay"/> (if given) replaces every frame delay.</summary>
    public static SheetLayoutSpec FromTemplate(SheetTemplate t, int frameWidth, int frameHeight, double? delay = null) =>
        new(frameWidth, frameHeight, t.Rows.ToList(), t.Frames.Select(f => delay is { } d ? f with { Delay = d } : f).ToList());

    /// <summary>The layout of an animation sheet or RSI state: its row labels, frame names (or numbers) and per-frame delays.</summary>
    public static SheetLayoutSpec FromBinding(RsiBinding b)
    {
        var frames = Enumerable.Range(0, b.Frames).Select(i =>
            new SheetFrame(b.FrameName(i) ?? (i + 1).ToString(CultureInfo.InvariantCulture), b.Delays.Length > 0 && i < b.Delays[0].Length ? b.Delays[0][i] : 0.1)).ToList();
        return new SheetLayoutSpec(b.FrameWidth, b.FrameHeight, b.RowLabels.ToList(), frames, b.Delays.Select(r => r.ToArray()).ToArray());
    }

    /// <summary>
    /// A layout from command-line style text, or null when nothing was given (the free prompt). <paramref name="template"/> is a template name or id,
    /// <paramref name="rows"/> "idle,attack", <paramref name="frames"/> "stand:0.5,step:0.15" (the delay after the colon is optional),
    /// <paramref name="size"/> "32x32", <paramref name="delay"/> seconds for every frame (replaces all delays). Explicit rows and frames replace the template's.
    /// </summary>
    public static SheetLayoutSpec? Parse(SheetTemplateLibrary? library, string? template, string? rows, string? frames, string? size, string? delay)
    {
        if (string.IsNullOrWhiteSpace(template) && string.IsNullOrWhiteSpace(rows) && string.IsNullOrWhiteSpace(frames) && string.IsNullOrWhiteSpace(size) && string.IsNullOrWhiteSpace(delay))
            return null;
        SheetTemplate? t = null;
        if (!string.IsNullOrWhiteSpace(template))
        {
            var name = template.Trim();
            t = (library ?? new SheetTemplateLibrary(Path.Combine(AppPaths.DataDirectory, "sheettemplates"))).Find(name)
                ?? BuiltInSheetTemplates.All.FirstOrDefault(x => x.Id.Equals("system:" + name, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException(CoreText.F("Нет шаблона листа «{0}»", name));
        }
        double? common = null;
        if (!string.IsNullOrWhiteSpace(delay))
        {
            if (!double.TryParse(delay.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) || !(d > 0 && d <= 60))
                throw new ArgumentException(CoreText.T("Задержка кадра от 0 до 60 секунд"));
            common = d;
        }
        int w = 32, h = 32;
        if (!string.IsNullOrWhiteSpace(size))
        {
            var p = size.Trim().ToLowerInvariant().Split(['x', '×', ','], StringSplitOptions.TrimEntries);
            if (p.Length != 2 || !int.TryParse(p[0], out w) || !int.TryParse(p[1], out h) || w < 1 || h < 1 || w > 256 || h > 256)
                throw new ArgumentException(CoreText.T("Размер кадра: ШИРИНАxВЫСОТА от 1 до 256, например 32x32"));
        }
        var rowList = string.IsNullOrWhiteSpace(rows) ? t?.Rows.ToList() ?? ["S", "N", "E", "W"]
            : rows.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        if (rowList.Count is < 1 or > 16 || rowList.Select(r => r.ToLowerInvariant()).Distinct().Count() != rowList.Count)
            throw new ArgumentException(CoreText.T("Строки: от 1 до 16 названий без повторов"));
        List<SheetFrame> frameList;
        if (string.IsNullOrWhiteSpace(frames))
            frameList = t?.Frames.ToList() ?? [new SheetFrame("1", common ?? 0.15)];
        else
        {
            frameList = [];
            foreach (var part in frames.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                int colon = part.LastIndexOf(':');
                double fd = common ?? 0.15;
                string fname = part;
                if (colon > 0 && double.TryParse(part[(colon + 1)..].Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && parsed > 0 && parsed <= 60)
                { fname = part[..colon].Trim(); fd = parsed; }
                frameList.Add(new SheetFrame(fname, fd));
            }
        }
        if (frameList.Count is < 1 or > 32) throw new ArgumentException(CoreText.T("Кадры: от 1 до 32"));
        var spec = new SheetLayoutSpec(w, h, rowList, frameList);
        return common is { } c ? spec.WithDelay(c) : spec;
    }

    public SheetLayoutSpec WithDelay(double delay) =>
        this with { Frames = Frames.Select(f => f with { Delay = delay }).ToList(), Delays = null };

    /// <summary>true if every row has the same delays as <see cref="Frames"/>.</summary>
    public bool UniformRows => Delays == null || Enumerable.Range(0, Rows.Count).All(r => Enumerable.Range(0, Columns).All(c => Math.Abs(DelayAt(r, c) - Frames[c].Delay) < 1e-9));

    /// <summary>A one-line description for a status line or a tooltip: size, rows, frames.</summary>
    public string Describe() => CoreText.F("кадр {0}×{1}, строки: {2}; кадры: {3}", FrameWidth, FrameHeight, string.Join(", ", Rows),
        string.Join(", ", Frames.Select(f => $"{f.Name} {f.Delay.ToString("0.###", CultureInfo.InvariantCulture)}s")));

    /// <summary>The sheet has the same grid (frame size, number of rows and of frames) so it can be drawn over the sheet this layout came from.</summary>
    public bool FitsGrid(SheetResult r) => r.FrameWidth == FrameWidth && r.FrameHeight == FrameHeight && r.Rows == Rows.Count && r.Columns == Columns;

    /// <summary>What differs between the answer and this layout (empty: it matches). Texts follow <see cref="CoreText"/>.</summary>
    public IReadOnlyList<string> Differences(SheetResult r)
    {
        var list = new List<string>();
        if (r.FrameWidth != FrameWidth || r.FrameHeight != FrameHeight)
            list.Add(CoreText.F("размер кадра: ожидался {0}×{1}, получен {2}×{3}", FrameWidth, FrameHeight, r.FrameWidth, r.FrameHeight));
        bool sameRows = r.Rows == Rows.Count && r.RowNames.Zip(Rows).All(p => string.Equals(p.First, p.Second, StringComparison.OrdinalIgnoreCase));
        if (!sameRows)
            list.Add(CoreText.F("строки: ожидались {0} ({1}), получены {2} ({3})", Rows.Count, string.Join(", ", Rows), r.Rows, string.Join(", ", r.RowNames)));
        if (r.Columns != Columns)
            list.Add(CoreText.F("кадров в строке: ожидалось {0}, получено {1}", Columns, r.Columns));
        else
        {
            var wrong = Enumerable.Range(0, Columns).Where(c => !string.IsNullOrWhiteSpace(r.FrameNames[c]) &&
                !string.Equals(r.FrameNames[c], Frames[c].Name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (wrong.Count > 0)
                list.Add(CoreText.F("названия кадров: ожидались {0}, получены {1}", string.Join(", ", Frames.Select(f => f.Name)),
                    string.Join(", ", r.FrameNames.Select((n, i) => string.IsNullOrWhiteSpace(n) ? Frames[i].Name : n))));
        }
        return list;
    }

    /// <summary>
    /// The rule for what is applied: a frame name or a delay the answer gives wins; one it omits is taken from this layout (the user's choice),
    /// for the cells the layout has. Pixels, row names and the grid are never changed.
    /// </summary>
    public SheetResult Complete(SheetResult r)
    {
        var names = r.FrameNames.Select((n, c) => string.IsNullOrWhiteSpace(n) && c < Columns ? Frames[c].Name : n).ToArray();
        var delays = new double[r.Delays.Length][];
        for (int row = 0; row < delays.Length; row++)
        {
            delays[row] = r.Delays[row].ToArray();
            for (int c = 0; c < delays[row].Length; c++)
            {
                bool given = r.ExplicitDelays is { } e && row < e.Length && c < e[row].Length ? e[row][c] : true;
                if (!given && row < Rows.Count && c < Columns) delays[row][c] = DelayAt(row, c);
            }
        }
        return r with { FrameNames = names, Delays = delays, ExplicitDelays = null };
    }
}
