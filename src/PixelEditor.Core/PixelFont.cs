// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
namespace PixelEditor.Core;

/// <summary>
/// One bitmap font. Every glyph is a block of rows of exactly <see cref="CellWidth"/> characters ('#' ink, '.' empty) and
/// <see cref="Height"/> rows (the line box: caps and ascenders occupy the top <see cref="Ascent"/> rows, descenders the rest).
/// Fonts are hand-drawn in code (see <see cref="PixelFontData"/>); the wide face is derived from another face by doubling columns.
/// </summary>
public sealed class PixelFontFace
{
    private readonly Dictionary<char, string[]> _glyphs;
    private readonly Dictionary<char, char> _alias;

    public string Id { get; }
    public string Name { get; }
    public int CellWidth { get; }
    /// <summary>Rows from the top to the baseline (cap height).</summary>
    public int Ascent { get; }
    /// <summary>Rows of the whole line box (ascent plus descenders).</summary>
    public int Height { get; }
    public int SpaceWidth { get; }
    /// <summary>The font has real lowercase letters; otherwise lowercase is drawn as capitals.</summary>
    public bool HasLowercase { get; }

    internal PixelFontFace(string id, string name, int cellWidth, int ascent, int height, int spaceWidth, bool hasLowercase,
        Dictionary<char, string[]> glyphs, Dictionary<char, char> alias)
    {
        Id = id; Name = name; CellWidth = cellWidth; Ascent = ascent; Height = height; SpaceWidth = spaceWidth;
        HasLowercase = hasLowercase; _glyphs = glyphs; _alias = alias;
    }

    /// <summary>All characters that have a glyph (without aliases and without space).</summary>
    public IEnumerable<char> Chars => _glyphs.Keys;

    /// <summary>The glyph rows (Height rows of CellWidth characters), or null if the font has no such character.</summary>
    public string[]? Find(char c, bool fold)
    {
        if (fold) c = char.ToUpperInvariant(c);
        if (_glyphs.TryGetValue(c, out var g)) return g;
        return _alias.TryGetValue(c, out var a) && _glyphs.TryGetValue(a, out g) ? g : null;
    }

    public bool Supports(char c) => c == ' ' || Find(c, !HasLowercase) != null;

    /// <summary>The visible "missing character" box: a hollow rectangle of the cell width and the cap height.</summary>
    public string[] Placeholder()
    {
        var rows = new string[Height];
        for (int y = 0; y < Height; y++)
        {
            if (y >= Ascent) rows[y] = new string('.', CellWidth);
            else if (y == 0 || y == Ascent - 1) rows[y] = new string('#', CellWidth);
            else rows[y] = "#" + new string('.', Math.Max(0, CellWidth - 2)) + (CellWidth > 1 ? "#" : "");
        }
        return rows;
    }

    /// <summary>Doubles every column of another face (a wide variant); lowercase, Cyrillic and spacing follow the source.</summary>
    internal static PixelFontFace Widen(string id, string name, PixelFontFace src, int factor)
    {
        var g = new Dictionary<char, string[]>();
        foreach (var (ch, rows) in src._glyphs)
            g[ch] = rows.Select(r => string.Concat(r.Select(c => new string(c, factor)))).ToArray();
        return new PixelFontFace(id, name, src.CellWidth * factor, src.Ascent, src.Height, src.SpaceWidth * factor, src.HasLowercase, g, src._alias);
    }
}

/// <summary>The registry of built-in fonts (looked up by id or by display name).</summary>
public static class PixelFonts
{
    public const string DefaultId = "5x7";

    private static readonly Lazy<IReadOnlyList<PixelFontFace>> Faces = new(() =>
    {
        var basic = PixelFontData.Build5x7();
        return new List<PixelFontFace>
        {
            basic,
            PixelFontData.BuildTiny(),
            PixelFontFace.Widen("wide", "Wide 10×9", basic, 2),
        };
    });

    public static IReadOnlyList<PixelFontFace> All => Faces.Value;
    public static PixelFontFace Default => All[0];

    /// <summary>By id ("5x7", "3x5", "wide") or by display name, case-insensitive; null if there is no such font.</summary>
    public static PixelFontFace? Find(string? idOrName) =>
        string.IsNullOrWhiteSpace(idOrName) ? null :
        All.FirstOrDefault(f => f.Id.Equals(idOrName.Trim(), StringComparison.OrdinalIgnoreCase) || f.Name.Equals(idOrName.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>By id or name; an unknown name gives the default font.</summary>
    public static PixelFontFace Get(string? idOrName) => Find(idOrName) ?? Default;
}

public enum TextAlign { Left, Center, Right }

/// <summary>Everything that decides how a text looks (the colors are passed separately).</summary>
public sealed record TextStyle(
    string Font = PixelFonts.DefaultId,
    int Scale = 1,
    int LetterSpacing = 1,
    int LineSpacing = 1,
    TextAlign Align = TextAlign.Left,
    bool UpperOnly = false,
    bool Proportional = false,
    bool Outline = false)
{
    public const int MaxScale = 4, MaxSpacing = 8;

    public PixelFontFace Face => PixelFonts.Get(Font);
    public int ScaleClamped => Math.Clamp(Scale, 1, MaxScale);
    public int LetterGap => Math.Clamp(LetterSpacing, 0, MaxSpacing);
    public int LineGap => Math.Clamp(LineSpacing, 0, MaxSpacing);
}

/// <summary>The layout of a text: glyph positions, line boxes, caret and selection geometry (all in unscaled font pixels unless stated).</summary>
public sealed class TextLayout
{
    public sealed record Glyph(char Ch, string[] Rows, int X, int Y, int InkOffset, bool Missing);

    public TextStyle Style { get; }
    public PixelFontFace Face { get; }
    public string Text { get; }
    public IReadOnlyList<string> Lines { get; }
    public IReadOnlyList<Glyph> Glyphs => _glyphs;
    /// <summary>Width of the box (the widest line, at least one cell) and its height, unscaled.</summary>
    public int Width { get; }
    public int Height { get; }
    public int LineHeight { get; }
    public int Pitch => LineHeight + Style.LineGap;
    public IReadOnlyList<char> Missing { get; }

    private readonly List<Glyph> _glyphs = new();
    private readonly List<int[]> _boundary = new();   // per line: x of the start of each character, plus the end (unscaled, before alignment)
    private readonly List<int> _lineWidth = new();
    private readonly List<int> _lineOffset = new();

    public TextLayout(string text, TextStyle style)
    {
        Style = style;
        Face = style.Face;
        Text = Normalize(text);
        Lines = Text.Split('\n');
        bool fold = style.UpperOnly || !Face.HasLowercase;
        LineHeight = fold ? Face.Ascent : Face.Height;
        int gap = style.LetterGap;
        var missing = new List<char>();
        var tmp = new List<(int Line, char Ch, string[] Rows, int X, int Ink, bool Miss)>();

        for (int li = 0; li < Lines.Count; li++)
        {
            int pen = 0;
            var line = Lines[li];
            var bounds = new int[line.Length + 1];
            for (int i = 0; i < line.Length; i++)
            {
                bounds[i] = pen;
                char ch = line[i];
                int adv;
                if (ch == ' ') adv = Face.SpaceWidth;
                else
                {
                    var rows = Face.Find(ch, fold);
                    bool miss = rows == null;
                    if (miss) { rows = Face.Placeholder(); if (!missing.Contains(ch)) missing.Add(ch); }
                    int ink = 0;
                    adv = Face.CellWidth;
                    if (style.Proportional && !miss)
                    {
                        int l = int.MaxValue, r = -1;
                        foreach (var row in rows!) { int a = row.IndexOf('#'), b = row.LastIndexOf('#'); if (a >= 0) { l = Math.Min(l, a); r = Math.Max(r, b); } }
                        if (r >= 0) { ink = l; adv = r - l + 1; }
                    }
                    tmp.Add((li, ch, rows!, pen, ink, miss));
                }
                pen += adv + gap;
            }
            bounds[line.Length] = line.Length == 0 ? 0 : pen - gap;
            _boundary.Add(bounds);
            _lineWidth.Add(line.Length == 0 ? 0 : pen - gap);
        }

        Missing = missing;
        Width = Math.Max(Face.CellWidth, _lineWidth.Max());
        Height = Lines.Count * LineHeight + (Lines.Count - 1) * style.LineGap;
        foreach (var w in _lineWidth)
            _lineOffset.Add(style.Align switch { TextAlign.Center => (Width - w) / 2, TextAlign.Right => Width - w, _ => 0 });
        foreach (var t in tmp)
            _glyphs.Add(new Glyph(t.Ch, t.Rows, t.X + _lineOffset[t.Line], t.Line * Pitch, t.Ink, t.Miss));
    }

    /// <summary>Only '\n' line breaks, no control characters, tabs become two spaces, each surrogate pair becomes one replacement character.</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new System.Text.StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\r') { if (i + 1 < text.Length && text[i + 1] == '\n') continue; sb.Append('\n'); }
            else if (c == '\t') sb.Append("  ");
            else if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) { sb.Append('�'); i++; }
            else if (char.IsControl(c) && c != '\n') continue;
            else sb.Append(c);
        }
        return sb.ToString();
    }

    private int Scale => Style.ScaleClamped;

    /// <summary>Size of the box in final pixels.</summary>
    public (int Width, int Height) ScaledSize => (Width * Scale, Height * Scale);

    /// <summary>(line, column) of a character index (clamped).</summary>
    public (int Line, int Column) LineColumn(int index)
    {
        index = Math.Clamp(index, 0, Text.Length);
        int line = 0, start = 0;
        for (int i = 0; i < index; i++) if (Text[i] == '\n') { line++; start = i + 1; }
        return (line, index - start);
    }

    /// <summary>Index of a (line, column) pair, the column is clamped to the line.</summary>
    public int IndexOf(int line, int column)
    {
        line = Math.Clamp(line, 0, Lines.Count - 1);
        int start = 0;
        for (int i = 0; i < line; i++) start += Lines[i].Length + 1;
        return start + Math.Clamp(column, 0, Lines[line].Length);
    }

    /// <summary>
    /// The caret position for a character index, in final pixels relative to the top left of the box: X is in the gap before the
    /// character (a whole-pixel column at the glyph edge when the letter spacing is zero), Y and Height are the line box.
    /// </summary>
    public (double X, int Y, int Height) Caret(int index)
    {
        var (li, col) = LineColumn(index);
        int bx = _boundary[li][col] + _lineOffset[li];
        double gap = Style.LetterGap / 2.0;
        double x = col == 0 ? bx : bx - gap;
        if (col == Lines[li].Length && col > 0) x = bx + gap;
        return (x * Scale, li * Pitch * Scale, LineHeight * Scale);
    }

    /// <summary>Selection boxes (one per line that has selected characters), final pixels relative to the box.</summary>
    public IReadOnlyList<PixelRect> SelectionRects(int from, int to)
    {
        var result = new List<PixelRect>();
        if (from > to) (from, to) = (to, from);
        from = Math.Clamp(from, 0, Text.Length);
        to = Math.Clamp(to, 0, Text.Length);
        if (from == to) return result;
        var (l0, c0) = LineColumn(from);
        var (l1, c1) = LineColumn(to);
        for (int li = l0; li <= l1; li++)
        {
            int a = li == l0 ? c0 : 0, b = li == l1 ? c1 : Lines[li].Length;
            bool wholeBreak = li < l1;   // the line break itself is selected: extend by one cell
            int x0 = _boundary[li][a] + _lineOffset[li];
            int x1 = _boundary[li][b] + _lineOffset[li] + (wholeBreak ? Face.SpaceWidth : 0);
            if (x1 <= x0) x1 = x0 + 1;
            result.Add(new PixelRect(x0 * Scale, li * Pitch * Scale, (x1 - x0) * Scale, LineHeight * Scale));
        }
        return result;
    }
}

/// <summary>The result of drawing a text: the picture (null if there is no ink at all) and where its corner lies relative to the box.</summary>
public sealed record TextRender(PixelImage? Image, int OffsetX, int OffsetY, TextLayout Layout);

/// <summary>Draws text with a <see cref="TextStyle"/> onto a transparent picture.</summary>
public static class PixelText
{
    public static TextRender Render(string text, uint color, TextStyle? style = null, uint outlineColor = 0)
    {
        style ??= new TextStyle();
        var layout = new TextLayout(text, style);
        int s = style.ScaleClamped;
        var (bw, bh) = layout.ScaledSize;
        bool outline = style.Outline && !Pixel.IsEmpty(outlineColor);
        int m = outline ? 1 : 0;
        var img = new PixelImage(bw + 2 * m, bh + 2 * m);
        int ink = 0;
        foreach (var g in layout.Glyphs)
            for (int gy = 0; gy < g.Rows.Length; gy++)
            {
                var row = g.Rows[gy];
                for (int gx = 0; gx < row.Length; gx++)
                {
                    if (row[gx] != '#') continue;
                    int px = (g.X + gx - g.InkOffset) * s, py = (g.Y + gy) * s;
                    for (int sy = 0; sy < s; sy++)
                        for (int sx = 0; sx < s; sx++)
                        {
                            int x = px + sx + m, y = py + sy + m;
                            if (x < 0 || y < 0 || x >= img.Width || y >= img.Height) continue;
                            img.Set(x, y, color);
                            ink++;
                        }
                }
            }
        if (ink == 0) return new TextRender(null, -m, -m, layout);
        if (outline)
        {
            var src = img.Clone();
            for (int y = 0; y < img.Height; y++)
                for (int x = 0; x < img.Width; x++)
                {
                    if (!Pixel.IsEmpty(src.Get(x, y))) continue;
                    bool near = false;
                    for (int dy = -1; dy <= 1 && !near; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx >= 0 && ny >= 0 && nx < img.Width && ny < img.Height && !Pixel.IsEmpty(src.Get(nx, ny))) { near = true; break; }
                        }
                    if (near) img.Set(x, y, outlineColor);
                }
        }
        return new TextRender(img, -m, -m, layout);
    }
}

/// <summary>
/// Compatibility facade over the default font (5×7 with real lowercase): the old simple API.
/// </summary>
public static class PixelFont
{
    public const int GlyphWidth = 5, GlyphHeight = 7, Spacing = 1, SpaceWidth = 3;

    /// <summary>The character is in the default font (space too).</summary>
    public static bool Supports(char c) => PixelFonts.Default.Supports(c);

    /// <summary>Text width in pixels at the given scale (the longest line).</summary>
    public static int Measure(string text, int scale = 1)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var l = new TextLayout(text, new TextStyle(Scale: scale));
        return l.Lines.Max(x => x.Length) == 0 ? 0 : (l.ScaledSize.Width);
    }

    /// <summary>Draws text in the default font; null for an empty or blank text.</summary>
    public static PixelImage? Render(string text, uint color, int scale = 1) =>
        PixelText.Render(text ?? "", color, new TextStyle(Scale: scale)).Image;
}
