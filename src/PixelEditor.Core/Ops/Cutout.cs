// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
namespace PixelEditor.Core.Ops;

/// <summary>
/// Cutting a subject out of a complex (non-uniform) background without neural networks. Idea: the background colors are taken from the picture border (several clusters),
/// the background is filled from the edges but does not cross strong boundaries (the subject outline), then the largest connected area is kept.
/// Works for studio and blurred backgrounds, gradients, several colors at the edge; on busy scenes with the subject right at the border, hint keepRect.
/// </summary>
public static class Cutout
{
    /// <summary>
    /// clusters: how many background colors to look for on the border (1…8). tolerance: how much a color may differ from the background color (the largest channel difference).
    /// edgeStop: the fill does not pass through pixels where the difference from the neighbor is larger than this value (0 turns the barrier off).
    /// keepLargest: keep only the largest connected area of the subject (removes background leftovers and junk).
    /// keepRect: [x, y, w, h] a rectangle that surely contains the subject (pixels inside it are not counted as background).
    /// </summary>
    public static PixelImage Remove(PixelImage src, int clusters, int tolerance, int edgeStop, bool keepLargest, PixelRect? keepRect = null)
    {
        int w = src.Width, h = src.Height;
        if (w < 3 || h < 3) return src.Clone();
        clusters = Math.Clamp(clusters, 1, 8);

        var centers = BorderClusters(src, clusters);
        if (centers.Count == 0) return src.Clone();

        bool NearBackground(uint p)
        {
            foreach (var c in centers)
                if (SelectionMask.SimilarColor(p, c, tolerance)) return true;
            return false;
        }

        // difference from the neighbor: a barrier for the fill
        int Gradient(int x, int y)
        {
            uint p = src.Pixels[y * w + x];
            int best = 0;
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                uint q = src.Pixels[ny * w + nx];
                best = Math.Max(best, Math.Max(Math.Abs(Pixel.R(p) - Pixel.R(q)), Math.Max(Math.Abs(Pixel.G(p) - Pixel.G(q)), Math.Abs(Pixel.B(p) - Pixel.B(q)))));
            }
            return best;
        }

        var bg = new bool[w * h];
        var stack = new Stack<(int X, int Y)>();
        bool Protected(int x, int y) => keepRect is PixelRect r && r.Contains(x, y);
        void Seed(int x, int y)
        {
            int i = y * w + x;
            if (bg[i] || Protected(x, y) || Pixel.IsEmpty(src.Pixels[i])) return;
            if (!NearBackground(src.Pixels[i])) return;
            bg[i] = true;
            stack.Push((x, y));
        }
        for (int x = 0; x < w; x++) { Seed(x, 0); Seed(x, h - 1); }
        for (int y = 0; y < h; y++) { Seed(0, y); Seed(w - 1, y); }
        while (stack.Count > 0)
        {
            var (cx, cy) = stack.Pop();
            foreach (var (dx, dy) in Cleanup.Neighbors(false))
            {
                int nx = cx + dx, ny = cy + dy;
                if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                int j = ny * w + nx;
                if (bg[j] || Protected(nx, ny) || Pixel.IsEmpty(src.Pixels[j]) || !NearBackground(src.Pixels[j])) continue;
                if (edgeStop > 0 && Gradient(nx, ny) > edgeStop) continue; // the subject boundary
                bg[j] = true;
                stack.Push((nx, ny));
            }
        }

        var dst = src.Clone();
        for (int i = 0; i < bg.Length; i++) if (bg[i]) dst.Pixels[i] = Pixel.Empty;
        if (src.Pixels.Any(Pixel.IsEmpty)) { /* the source transparency is kept as it is */ }

        return keepLargest ? KeepLargestComponent(dst) : dst;
    }


    /// <summary>
    /// A second way to cut a subject out, much steadier on photos than the barrier fill. The background is modeled from the border (a few Lab clusters,
    /// see <see cref="BackgroundModel"/>; a subject touching the border does not become background). Every pixel gets its perceptual (Lab) distance to the
    /// nearest background color, the distance map is smoothed (a box blur that ignores transparent pixels) and split by a threshold
    /// (threshold = 0: found automatically by stability of the subject area). The background is what is below the threshold AND connected to the picture
    /// edge, so light patches inside the subject stay; the fill also follows a smooth color change (gradient, noise), so a vignette or a floor needs no tuning.
    /// The result is refined until it stops changing (at most 4 rounds): the background colors found so far refit the model, which covers parts of the
    /// background that the border colors alone describe poorly. Then the antialiased rim is removed (defringe), enclosed background areas of at least 3% of the
    /// picture are erased (enclosed: off, auto, all), specks and edge noise are cleaned. A picture whose border is already mostly transparent is returned as is,
    /// so running the operation again changes nothing.
    /// smooth: blur radius of the distance map in pixels (0..6). keepRect: a rectangle that surely contains the subject.
    /// follow: the largest Lab step (in dE) between neighbors for the fill to follow a smooth change; negative is automatic, 0 turns it off.
    /// </summary>
    public static PixelImage RemoveByDistance(PixelImage src, int clusters, int threshold, int smooth, bool keepLargest, PixelRect? keepRect = null, double follow = -1,
        bool defringe = true, string enclosed = "auto")
    {
        int w = src.Width, h = src.Height;
        if (w < 3 || h < 3) return src.Clone();
        clusters = Math.Clamp(clusters, 1, 8);
        var labs = ColorMetric.ToLab(src.Pixels);
        var model = BackgroundModel.Build(src, labs, null, null, clusters);
        if (model == null) return src.Clone();
        bool crisp = Background.IsCrisp(src, model);
        double scale = ColorMetric.Scale;
        double step = follow < 0 ? model.Step : follow * scale;       // in tolerance units
        bool Protected(int i) => keepRect is PixelRect r && r.Contains(i % w, i / w);

        float[] DistanceMap(IReadOnlyList<LabColor> centers)
        {
            var map = new float[w * h];
            for (int i = 0; i < map.Length; i++)
            {
                if (Pixel.IsEmpty(src.Pixels[i])) { map[i] = float.NaN; continue; }
                float best = float.MaxValue;
                foreach (var c in centers) best = Math.Min(best, ColorMetric.Distance(labs[i], c));
                map[i] = best / (float)scale;                          // dE units
            }
            return BoxBlur(map, w, h, Math.Clamp(smooth, 0, 6));
        }

        // the background for a threshold t (dE): what is within t of the model AND connected to the picture edge, plus smooth continuations of it
        bool[] BackgroundFor(float[] dist, double t)
        {
            var fill = new BackgroundFlood(src, labs, i => dist[i] <= t, i => dist[i] <= Math.Max(t * 3, 36), step, Protected);
            fill.SeedRing();
            fill.Run();
            return fill.Removed;
        }

        var centersNow = model.Centers;
        var map1 = DistanceMap(centersNow);
        // the automatic threshold stays near the spread of the border colors: following handles what lies beyond it, and a larger threshold would swallow a low-contrast subject
        double tolDe = model.Tolerance / scale;
        double t = threshold > 0 ? threshold : Math.Clamp(StableThreshold(tt => BackgroundFor(map1, tt), w * h), tolDe, Math.Max(1.15 * tolDe, 4));
        var bg = BackgroundFor(map1, t);

        // refinement to a fixed point: refit the model on the background found so far, grow the background, stop when it no longer changes
        for (int round = 0; round < 4; round++)
        {
            var sample = new List<LabColor>();
            int total = bg.Count(b => b), stride = Math.Max(1, total / 3000);
            for (int i = 0, k = 0; i < bg.Length; i++)
                if (bg[i] && !Pixel.IsEmpty(src.Pixels[i]) && k++ % stride == 0) sample.Add(labs[i]);
            if (sample.Count < 8) break;
            var refit = BackgroundModel.Cluster(sample, clusters, Math.Max(8, model.Tolerance), out _, out _);
            var next = BackgroundFor(DistanceMap(refit), t);
            int added = 0;
            for (int i = 0; i < bg.Length; i++) if (next[i] && !bg[i]) { added++; bg[i] = true; }
            if (added <= w * h * 0.002) break;
        }

        string mode = (enclosed ?? "auto").Trim().ToLowerInvariant();
        if (mode == "all" || (mode == "auto" && !crisp))
        {
            var dist = map1;
            var cand = new bool[w * h];
            for (int i = 0; i < cand.Length; i++) cand[i] = !bg[i] && !Pixel.IsEmpty(src.Pixels[i]) && !Protected(i) && dist[i] <= t;
            var fill = new BackgroundFlood(src, labs, i => dist[i] <= t, i => dist[i] <= Math.Max(t * 3, 36), step, Protected);
            for (int i = 0; i < bg.Length; i++) if (bg[i]) fill.Removed[i] = true;
            fill.AddEnclosed(cand, mode == "all" ? 1 : Background.EnclosedMinArea(w, h));
            bg = fill.Removed;
        }

        var dst = src.Clone();
        for (int i = 0; i < bg.Length; i++) if (bg[i]) dst.Pixels[i] = Pixel.Empty;
        if (defringe && !crisp) dst = Background.Defringe(src, dst, Math.Max(6, model.Tolerance));
        dst = Cleanup.RemoveIslands(dst, Math.Max(2, w * h / 4000));       // specks of subject
        dst = Background.CleanEdges(dst, null, 40, 1, specks: true, fillGaps: true, minNeighbors: 6);
        return keepLargest ? KeepLargestComponent(dst) : dst;
    }

    /// <summary>
    /// The automatic threshold, found by stability: the subject area is measured for growing thresholds (4, 6, ... 30 units of Lab) and the first
    /// threshold after which the area stops shrinking quickly is taken. Below it background noise still sticks to the subject, above it parts of the
    /// subject that look like the background (a pale belly, a snowy tail) start to be eaten away.
    /// </summary>
    private static double StableThreshold(Func<double, bool[]> background, int pixels)
    {
        const int first = 4, last = 30, step = 2;
        var area = new List<(int T, int Area)>();
        for (int t = first; t <= last + step; t += step)
            area.Add((t, background(t).Count(b => !b)));
        for (int i = 0; i + 1 < area.Count; i++)
        {
            var (t, a0) = area[i];
            if (a0 < pixels * 0.004) break;                                    // the subject has already vanished: stop looking
            if (area[i + 1].Area >= a0 * 0.95) return t;                       // the area barely changes with a larger threshold: stable
        }
        return 8;
    }

    /// <summary>Otsu's threshold of a distance map: the cut that best separates two groups (background and subject) by their means.</summary>
    private static double Otsu(float[] values)
    {
        const int bins = 128;
        float max = values.Max();
        if (max <= 0) return 0;
        var hist = new double[bins];
        foreach (var v in values) hist[Math.Min(bins - 1, (int)(v / max * bins))]++;
        double total = values.Length, sum = 0;
        for (int i = 0; i < bins; i++) sum += i * hist[i];
        double wb = 0, sb = 0, best = -1, cut = 0;
        for (int i = 0; i < bins; i++)
        {
            wb += hist[i];
            if (wb == 0) continue;
            double wf = total - wb;
            if (wf == 0) break;
            sb += i * hist[i];
            double mb = sb / wb, mf = (sum - sb) / wf;
            double between = wb * wf * (mb - mf) * (mb - mf);
            if (between > best) { best = between; cut = i; }
        }
        return (cut + 1) * max / bins;
    }

    /// <summary>Separable box blur of a float map (edges are clamped).</summary>
    private static float[] BoxBlur(float[] src, int w, int h, int radius)
    {
        if (radius <= 0) return src;
        var tmp = new float[src.Length];
        var dst = new float[src.Length];
        // NaN marks a transparent pixel: it is skipped, so removed areas do not pull the distance of the subject edge down (and stay NaN themselves)
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float s = 0; int n = 0;
                for (int k = -radius; k <= radius; k++)
                {
                    float v = src[y * w + Math.Clamp(x + k, 0, w - 1)];
                    if (!float.IsNaN(v)) { s += v; n++; }
                }
                tmp[y * w + x] = n == 0 ? float.NaN : s / n;
            }
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (float.IsNaN(src[y * w + x])) { dst[y * w + x] = float.NaN; continue; }
                float s = 0; int n = 0;
                for (int k = -radius; k <= radius; k++)
                {
                    float v = tmp[Math.Clamp(y + k, 0, h - 1) * w + x];
                    if (!float.IsNaN(v)) { s += v; n++; }
                }
                dst[y * w + x] = n == 0 ? float.NaN : s / n;
            }
        return dst;
    }

    /// <summary>k-means centers over the border colors: one or two colors for a studio background, more for a gradient or several shades.</summary>
    private static List<uint> BorderClusters(PixelImage src, int k)
    {
        var samples = new List<uint>();
        for (int x = 0; x < src.Width; x++)
        {
            samples.Add(src.Pixels[x]);
            samples.Add(src.Pixels[(src.Height - 1) * src.Width + x]);
        }
        for (int y = 1; y < src.Height - 1; y++)
        {
            samples.Add(src.Pixels[y * src.Width]);
            samples.Add(src.Pixels[y * src.Width + src.Width - 1]);
        }
        samples = samples.Where(p => !Pixel.IsEmpty(p)).ToList();
        if (samples.Count == 0) return [];

        // initial centers: the colors farthest from each other (farthest point)
        var centers = new List<uint> { samples.GroupBy(c => c).OrderByDescending(g => g.Count()).First().Key };
        while (centers.Count < k)
        {
            uint far = samples.OrderByDescending(s => centers.Min(c => Quantizer.Distance(s, c))).First();
            if (centers.Min(c => Quantizer.Distance(far, c)) == 0) break;
            centers.Add(far);
        }
        for (int iter = 0; iter < 6; iter++)
        {
            var groups = centers.Select(_ => new List<uint>()).ToArray();
            foreach (var s in samples)
            {
                int best = 0;
                long bd = long.MaxValue;
                for (int i = 0; i < centers.Count; i++)
                {
                    long d = Quantizer.Distance(s, centers[i]);
                    if (d < bd) { bd = d; best = i; }
                }
                groups[best].Add(s);
            }
            for (int i = 0; i < centers.Count; i++)
                if (groups[i].Count > 0)
                    centers[i] = Pixel.Rgb((byte)groups[i].Average(c => Pixel.R(c)), (byte)groups[i].Average(c => Pixel.G(c)), (byte)groups[i].Average(c => Pixel.B(c)));
        }
        return centers;
    }

    private static PixelImage KeepLargestComponent(PixelImage img)
    {
        int w = img.Width, h = img.Height;
        var label = new int[w * h];
        var sizes = new List<int> { 0 };
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (label[i] != 0 || Pixel.IsEmpty(img.Pixels[i])) continue;
                int id = sizes.Count, count = 0;
                sizes.Add(0);
                var stack = new Stack<int>();
                stack.Push(i);
                label[i] = id;
                while (stack.Count > 0)
                {
                    int cur = stack.Pop();
                    count++;
                    int cx = cur % w, cy = cur / w;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = cx + dx, ny = cy + dy;
                            if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                            int j = ny * w + nx;
                            if (label[j] != 0 || Pixel.IsEmpty(img.Pixels[j])) continue;
                            label[j] = id;
                            stack.Push(j);
                        }
                }
                sizes[id] = count;
            }
        if (sizes.Count <= 2) return img;
        int keep = Enumerable.Range(1, sizes.Count - 1).OrderByDescending(i => sizes[i]).First();
        var dst = img.Clone();
        for (int i = 0; i < label.Length; i++)
            if (label[i] != keep) dst.Pixels[i] = Pixel.Empty;
        return dst;
    }
}

/// <summary>Cutting out by a polygon set by hand or by an agent looking at the picture: a reliable way when the background resembles the subject and automation fails.</summary>
public static class ShapeMask
{
    /// <summary>
    /// Keeps the pixels inside a polygon given by points [[x, y], ...] (source picture coordinates), the rest become transparent.
    /// keepOutside=true does the opposite and cuts the polygon out (for example to remove an object from the background).
    /// </summary>
    public static PixelImage Apply(PixelImage src, IReadOnlyList<(int X, int Y)> points, bool keepOutside = false)
    {
        if (points.Count < 3) throw new ArgumentException(CoreText.T("polygonMask: нужно минимум 3 точки"));
        var mask = SelectionMask.Polygon(points, src.Width, src.Height);
        var dst = src.Clone();
        for (int y = 0; y < src.Height; y++)
            for (int x = 0; x < src.Width; x++)
            {
                bool inside = mask != null && mask.Contains(x, y);
                if (inside == keepOutside) dst.Pixels[y * src.Width + x] = Pixel.Empty;
            }
        return dst;
    }
}
