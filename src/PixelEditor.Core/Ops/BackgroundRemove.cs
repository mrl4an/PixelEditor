// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
namespace PixelEditor.Core.Ops;

/// <summary>
/// A flood fill of the background from the picture edge. A pixel joins the background if it matches the model, or (following, step &gt; 0) differs little
/// from the mean color of the background already accepted around it (a smooth gradient, noise) and is within the limit of the model. A sharp edge of the subject
/// stops the fill. Transparent pixels always pass. blocked: pixels that never join (for example a rectangle that surely holds the subject).
/// </summary>
internal sealed class BackgroundFlood
{
    private readonly PixelImage _src;
    private readonly LabColor[] _labs;
    private readonly Func<int, bool> _match, _withinLimit;
    private readonly Func<int, bool>? _blocked;
    private readonly double _step;
    private readonly int _w, _h;
    private readonly Stack<int> _stack = new();

    public bool[] Removed { get; }

    public BackgroundFlood(PixelImage src, LabColor[] labs, Func<int, bool> match, Func<int, bool> withinLimit, double step, Func<int, bool>? blocked = null)
    {
        _src = src; _labs = labs; _match = match; _withinLimit = withinLimit; _step = step; _blocked = blocked;
        _w = src.Width; _h = src.Height;
        Removed = new bool[_w * _h];
    }

    private void Add(int i, int from)
    {
        if (Removed[i] || (_blocked != null && _blocked(i))) return;
        bool ok;
        if (Pixel.IsEmpty(_src.Pixels[i])) ok = true;
        else ok = _match(i) || (_step > 0 && from >= 0 && !Pixel.IsEmpty(_src.Pixels[from]) && _withinLimit(i)
                                && ColorMetric.Distance(_labs[i], LocalBackground(i, from)) <= _step);
        if (!ok) return;
        Removed[i] = true; _stack.Push(i);
    }

    // The mean color of the already accepted background around a pixel: comparing with it instead of one neighbor halves the noise of the step.
    private LabColor LocalBackground(int i, int from)
    {
        int x = i % _w, y = i / _w, n = 0;
        double l = 0, a = 0, b = 0;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if ((dx == 0 && dy == 0) || nx < 0 || ny < 0 || nx >= _w || ny >= _h) continue;
                int j = ny * _w + nx;
                if (!Removed[j] || Pixel.IsEmpty(_src.Pixels[j])) continue;
                l += _labs[j].L; a += _labs[j].A; b += _labs[j].B; n++;
            }
        return n == 0 ? _labs[from] : new LabColor((float)(l / n), (float)(a / n), (float)(b / n));
    }

    public void SeedRing() { foreach (int i in BackgroundModel.Ring(_w, _h)) Add(i, -1); }

    public void Run()
    {
        while (_stack.Count > 0)
        {
            int cur = _stack.Pop();
            int cx = cur % _w, cy = cur / _w;
            if (cx > 0) Add(cur - 1, cur);
            if (cx < _w - 1) Add(cur + 1, cur);
            if (cy > 0) Add(cur - _w, cur);
            if (cy < _h - 1) Add(cur + _w, cur);
        }
    }

    /// <summary>Background-like areas that the fill did not reach (enclosed by the subject): the connected groups of at least minArea candidates are added and grown.</summary>
    public void AddEnclosed(bool[] candidate, int minArea)
    {
        foreach (var comp in Background.Components(candidate, Removed, _w, _h))
        {
            if (comp.Count < minArea) continue;
            foreach (int i in comp) { Removed[i] = true; _stack.Push(i); }
            Run();
        }
    }
}

public static partial class Background
{
    /// <summary>
    /// Removes the background. The background is modeled from the picture border as a few Lab color clusters (<see cref="BackgroundModel"/>) and compared with a
    /// perceptual distance (<see cref="ColorMetric"/>), so a photo, JPEG noise, a gradient or a two-tone floor and wall work without hand tuning.
    /// color: the background color (null: modeled from the border). tolerance: how far a color may be from the model (null: found from the spread of the
    /// border colors; 0 is an exact match). edgesOnly: erase only what is connected to the picture edge; otherwise all similar pixels are erased.
    /// follow: the largest color step between neighbors for the fill to follow a smooth change (gradient, noise); null is automatic when the tolerance is
    /// automatic and off otherwise, 0 is off. limit: how far a followed pixel may be from the model. defringe: also removes the antialiased rim (blends of the
    /// background and the subject) left at the edge; skipped for crisp pixel art. enclosed: "off", "auto" (a background-like area that the subject encloses is
    /// erased when it is at least 3% of the picture and the picture is not crisp pixel art, so eye whites stay) or "all".
    /// </summary>
    public static PixelImage Remove(PixelImage src, uint? color, int? tolerance, bool edgesOnly, int? follow = null, int limit = 90,
        bool defringe = true, string enclosed = "auto")
    {
        var dst = src.Clone();
        int w = src.Width, h = src.Height;
        if (w == 0 || h == 0) return dst;
        var labs = ColorMetric.ToLab(src.Pixels);
        var model = BackgroundModel.Build(src, labs, color, tolerance);
        if (model == null) return dst;               // the background is already transparent
        bool crisp = model.Flat && DistinctColorsAtMost(src, 24);
        double tol = model.Tolerance;
        double step = follow ?? (tolerance == null ? model.Step : 0);

        var md = new float[w * h];
        for (int i = 0; i < md.Length; i++) md[i] = Pixel.IsEmpty(src.Pixels[i]) ? 0 : model.DistanceToModel(labs[i]);
        bool Match(int i) => model.ExactColor is uint e ? ((src.Pixels[i] ^ e) & 0xFFFFFFu) == 0 : md[i] <= tol;

        var removed = new bool[w * h];
        if (!edgesOnly)
        {
            for (int i = 0; i < removed.Length; i++)
                if (!Pixel.IsEmpty(src.Pixels[i]) && Match(i)) { removed[i] = true; dst.Pixels[i] = Pixel.Empty; }
        }
        else
        {
            var fill = new BackgroundFlood(src, labs, Match, i => md[i] <= limit, step);
            fill.SeedRing();
            fill.Run();

            string mode = (enclosed ?? "auto").Trim().ToLowerInvariant();
            if (mode == "all" || (mode == "auto" && !crisp))
            {
                var cand = EnclosedCandidates(src, labs, fill.Removed, model, tol);
                fill.AddEnclosed(cand, mode == "all" ? 1 : EnclosedMinArea(w, h));
            }
            for (int i = 0; i < removed.Length; i++) if (fill.Removed[i]) dst.Pixels[i] = Pixel.Empty;
        }

        if (defringe && !crisp) dst = CleanRim(src, dst, Math.Max(6, tol));
        return dst;
    }

    /// <summary>The smallest enclosed background area (in pixels) that "auto" erases: 3% of the picture.</summary>
    internal static int EnclosedMinArea(int w, int h) => Math.Max(24, (int)(w * h * 0.03));

    /// <summary>Crisp pixel art: a flat exact border and very few colors. Such a picture is processed exactly (no rim cleanup, enclosed areas stay).</summary>
    internal static bool IsCrisp(PixelImage src, BackgroundModel model) => model.Flat && DistinctColorsAtMost(src, 24);

    /// <summary>The rim cleanup of a finished cut: the antialiased blend pixels (<see cref="Defringe"/>) and the tiny specks of leftover background.</summary>
    internal static PixelImage CleanRim(PixelImage original, PixelImage cut, double tolerance)
        => Cleanup.RemoveIslands(Defringe(original, cut, tolerance), Math.Min(2, original.Width * original.Height / 2500));

    private static bool DistinctColorsAtMost(PixelImage src, int max)
    {
        var set = new HashSet<uint>();
        foreach (var p in src.Pixels) if (!Pixel.IsEmpty(p) && set.Add(p) && set.Count > max) return false;
        return true;
    }

    /// <summary>Not yet removed pixels whose color is close to some already removed background color (a coarse Lab grid of the removed colors).</summary>
    internal static bool[] EnclosedCandidates(PixelImage src, LabColor[] labs, bool[] removed, BackgroundModel model, double tol)
    {
        int n = src.Pixels.Length;
        var cand = new bool[n];
        if (model.ExactColor is uint e)
        {
            for (int i = 0; i < n; i++) cand[i] = !removed[i] && !Pixel.IsEmpty(src.Pixels[i]) && ((src.Pixels[i] ^ e) & 0xFFFFFFu) == 0;
            return cand;
        }
        double cell = Math.Max(3.0, tol * 0.6);
        (int, int, int) Key(in LabColor c) => ((int)Math.Floor(c.L / cell), (int)Math.Floor(c.A / cell), (int)Math.Floor(c.B / cell));
        var sums = new Dictionary<(int, int, int), (double L, double A, double B, int N)>();
        for (int i = 0; i < n; i++)
        {
            if (!removed[i] || Pixel.IsEmpty(src.Pixels[i])) continue;
            var k = Key(labs[i]);
            sums.TryGetValue(k, out var s);
            sums[k] = (s.L + labs[i].L, s.A + labs[i].A, s.B + labs[i].B, s.N + 1);
        }
        for (int i = 0; i < n; i++)
        {
            if (removed[i] || Pixel.IsEmpty(src.Pixels[i])) continue;
            var (kl, ka, kb) = Key(labs[i]);
            for (int dl = -1; dl <= 1 && !cand[i]; dl++)
                for (int da = -1; da <= 1 && !cand[i]; da++)
                    for (int db = -1; db <= 1; db++)
                    {
                        if (!sums.TryGetValue((kl + dl, ka + da, kb + db), out var s)) continue;
                        var mean = new LabColor((float)(s.L / s.N), (float)(s.A / s.N), (float)(s.B / s.N));
                        if (ColorMetric.Distance(labs[i], mean) <= tol) { cand[i] = true; break; }
                    }
        }
        return cand;
    }

    /// <summary>4-connected components of the candidate pixels that are not removed.</summary>
    internal static List<List<int>> Components(bool[] candidate, bool[] removed, int w, int h)
    {
        var seen = new bool[w * h];
        var res = new List<List<int>>();
        for (int s = 0; s < candidate.Length; s++)
        {
            if (seen[s] || removed[s] || !candidate[s]) continue;
            var comp = new List<int>();
            var st = new Stack<int>();
            st.Push(s); seen[s] = true;
            while (st.Count > 0)
            {
                int cur = st.Pop(); comp.Add(cur);
                int cx = cur % w, cy = cur / w;
                void Try(int j) { if (!seen[j] && !removed[j] && candidate[j]) { seen[j] = true; st.Push(j); } }
                if (cx > 0) Try(cur - 1);
                if (cx < w - 1) Try(cur + 1);
                if (cy > 0) Try(cur - w);
                if (cy < h - 1) Try(cur + w);
            }
            res.Add(comp);
        }
        return res;
    }

    /// <summary>
    /// Removes the antialiased rim left after a cut. An opaque pixel that touches the removed area is a blend of the local background color B and an interior
    /// color I (one of its inner neighbors) when it lies on the segment B..I in Lab (the residual is within tolerance). The share alpha of the subject in
    /// the blend is read from the position on that segment: below 0.5 the pixel is mostly background and is erased, from 0.5 to 0.92 it is repainted
    /// with the interior color (its background tint is removed), above that it stays. A second pass peels a rim two pixels wide. Pixels that are not a
    /// blend (a real outline, a different color) and edges with too little contrast are not touched.
    /// original: the picture before the cut (the colors of the removed pixels give B), cut: the picture after it.
    /// </summary>
    public static PixelImage Defringe(PixelImage original, PixelImage cut, double tolerance = 8, int passes = 2)
    {
        int w = cut.Width, h = cut.Height;
        var img = cut.Clone();
        var solid = new bool[w * h];                 // the removed background that gives the local background color
        var olabs = new LabColor[w * h];
        for (int i = 0; i < solid.Length; i++)
            if (Pixel.IsEmpty(cut.Pixels[i]) && !Pixel.IsEmpty(original.Pixels[i])) { solid[i] = true; olabs[i] = ColorMetric.ToLab(original.Pixels[i]); }

        bool Empty(int x, int y) => Pixel.IsEmpty(img.Pixels[y * w + x]);
        bool IsEdge(int x, int y) =>
            (x > 0 && Empty(x - 1, y)) || (x < w - 1 && Empty(x + 1, y)) || (y > 0 && Empty(x, y - 1)) || (y < h - 1 && Empty(x, y + 1));

        for (int pass = 0; pass < Math.Clamp(passes, 1, 4); pass++)
        {
            var edits = new List<(int Index, uint Color)>();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (Pixel.IsEmpty(img.Pixels[i]) || !IsEdge(x, y)) continue;

                    double bl = 0, ba = 0, bb = 0; int bn = 0;
                    for (int dy = -2; dy <= 2; dy++)
                        for (int dx = -2; dx <= 2; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= w || ny >= h || !solid[ny * w + nx]) continue;
                            var l = olabs[ny * w + nx]; bl += l.L; ba += l.A; bb += l.B; bn++;
                        }
                    if (bn < 2) continue;
                    var B = new LabColor((float)(bl / bn), (float)(ba / bn), (float)(bb / bn));
                    var P = ColorMetric.ToLab(img.Pixels[i]);

                    double bestResid = double.MaxValue, bestAlpha = 0, bestLen = 0; uint bestColor = 0;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if ((dx == 0 && dy == 0) || nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                            if (Empty(nx, ny) || IsEdge(nx, ny)) continue;     // only inner pixels are subject colors
                            uint ic = img.Pixels[ny * w + nx];
                            var I = ColorMetric.ToLab(ic);
                            double ux = I.L - B.L, uy = I.A - B.A, uz = I.B - B.B, len2 = ux * ux + uy * uy + uz * uz;
                            if (len2 < 18 * 18) continue;                       // too little contrast to tell a blend from noise
                            double px = P.L - B.L, py = P.A - B.A, pz = P.B - B.B;
                            double alpha = (px * ux + py * uy + pz * uz) / len2;
                            double rx = px - alpha * ux, ry = py - alpha * uy, rz = pz - alpha * uz;
                            double resid = Math.Sqrt(rx * rx + ry * ry + rz * rz);
                            if (resid < bestResid) { bestResid = resid; bestAlpha = alpha; bestLen = Math.Sqrt(len2); bestColor = ic; }
                        }
                    if (bestResid == double.MaxValue || bestResid > Math.Max(tolerance * 0.8, 0.18 * bestLen)) continue;
                    if (bestAlpha >= -0.15 && bestAlpha < 0.5) edits.Add((i, Pixel.Empty));
                    else if (bestAlpha >= 0.5 && bestAlpha < 0.92) edits.Add((i, bestColor));
                }
            if (edits.Count == 0) break;
            foreach (var (index, color) in edits) img.Pixels[index] = color;
        }
        return img;
    }
}
