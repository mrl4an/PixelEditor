// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
namespace PixelEditor.Core.Ops;

/// <summary>
/// A model of the background built from the picture border: a few color clusters in Lab (a flat color is one cluster, a gradient or a two-tone floor and wall
/// several), the spread of the border colors (gives the tolerance) and the typical difference between neighboring border pixels (gives the largest step
/// of the smooth-gradient following). All distances are in <see cref="ColorMetric"/> units. Clusters that cover only a small part of the border
/// are dropped, so a subject that touches the border does not become "background".
/// </summary>
public sealed class BackgroundModel
{
    /// <summary>A cluster must hold at least this share of the border samples (the largest cluster is always kept).</summary>
    public const double MinWeight = 0.15;

    public required List<LabColor> Centers { get; init; }
    /// <summary>How far from a center a pixel still counts as the background itself.</summary>
    public double Tolerance { get; init; }
    /// <summary>The largest color step between neighbors for a pixel to continue a smooth background (gradient, noise).</summary>
    public double Step { get; init; }
    /// <summary>The border is one flat exact color (tolerance below one unit): typical for pixel art.</summary>
    public bool Flat { get; init; }
    /// <summary>With tolerance 0 the model is an exact color.</summary>
    public uint? ExactColor { get; init; }

    public float DistanceToModel(in LabColor c)
    {
        float best = float.MaxValue;
        foreach (var m in Centers) { float d = ColorMetric.Distance(c, m); if (d < best) best = d; }
        return best;
    }

    /// <summary>Indices of the picture border pixels in ring order (each pixel once).</summary>
    public static List<int> Ring(int w, int h)
    {
        var r = new List<int>();
        for (int x = 0; x < w; x++) r.Add(x);
        for (int y = 1; y < h; y++) r.Add(y * w + w - 1);
        if (h > 1) for (int x = w - 2; x >= 0; x--) r.Add((h - 1) * w + x);
        if (w > 1) for (int y = h - 2; y >= 1; y--) r.Add(y * w);
        return r;
    }

    /// <summary>
    /// Builds the model. fixedColor: the user named the background color. explicitTolerance: the user named the tolerance (0: exact match).
    /// maxClusters: 1..8. Returns null when there is nothing to model (the border is already transparent).
    /// </summary>
    public static BackgroundModel? Build(PixelImage src, LabColor[] labs, uint? fixedColor, double? explicitTolerance, int maxClusters = 6)
    {
        int w = src.Width, h = src.Height;
        var ring = Ring(w, h);
        var idx = ring.Where(i => !Pixel.IsEmpty(src.Pixels[i])).ToList();
        if (idx.Count == 0) return null;
        // a mostly transparent border means the picture is already cut out: a model from the few opaque border pixels would be the subject itself
        if (fixedColor == null && idx.Count < ring.Count * 0.5) return null;

        // typical deviation of a border pixel from the mean of its ring neighbors (noise and the slope of a gradient): the allowed step of the gradient following
        var nb = new List<float>();
        for (int k = 0; k < ring.Count; k++)
        {
            if (Pixel.IsEmpty(src.Pixels[ring[k]])) continue;
            double l = 0, a = 0, b = 0; int n = 0;
            for (int d = -2; d <= 2; d++)
            {
                if (d == 0) continue;
                int j = ring[((k + d) % ring.Count + ring.Count) % ring.Count];
                if (Pixel.IsEmpty(src.Pixels[j])) continue;
                l += labs[j].L; a += labs[j].A; b += labs[j].B; n++;
            }
            if (n >= 2) nb.Add(ColorMetric.Distance(labs[ring[k]], new LabColor((float)(l / n), (float)(a / n), (float)(b / n))));
        }
        double medianNb = nb.Count == 0 ? 0 : Percentile(nb, 0.5);
        double rawStep = 1.25 * (nb.Count == 0 ? 0 : Percentile(nb, 0.9)) + 1.5;
        // A strongly textured border (grass, fur, snow) makes any step large enough to walk into a subject of the same texture, so the automatic
        // following is switched off there (step 0): the tolerance, which grows with the spread, describes such a background instead.
        double step = rawStep > 12 ? 0 : Math.Clamp(rawStep, 3, 12);

        if (explicitTolerance is <= 0)
        {
            uint exact = idx.GroupBy(i => src.Pixels[i] & 0xFFFFFFu).OrderByDescending(g => g.Count()).First().Key | 0xFF000000u;
            if (fixedColor is uint fc) exact = fc | 0xFF000000u;
            return new BackgroundModel { Centers = [ColorMetric.ToLab(exact)], Tolerance = 0, Step = step, Flat = true, ExactColor = exact };
        }

        List<LabColor> samples;
        List<LabColor> centers;
        double tol;
        if (fixedColor is uint color)
        {
            var c0 = ColorMetric.ToLab(color);
            samples = idx.Select(i => labs[i]).Where(l => ColorMetric.Distance(l, c0) <= 60).ToList();
            centers = [c0];
            tol = explicitTolerance ?? (samples.Count == 0 ? 12 : Math.Clamp(1.25 * Percentile(samples.Select(l => ColorMetric.Distance(l, c0)).ToList(), 0.97) + 2, 6, 45));
        }
        else
        {
            samples = idx.Select(i => labs[i]).ToList();
            double bound = Math.Max(8, 3 * medianNb + 4);
            var all = Cluster(samples, maxClusters, bound, out var assign, out var counts);
            int keepMin = (int)Math.Ceiling(samples.Count * MinWeight);
            int largest = Array.IndexOf(counts, counts.Max());
            var keepIdx = Enumerable.Range(0, all.Count).Where(c => counts[c] >= keepMin || c == largest).ToList();
            centers = keepIdx.Select(c => all[c]).ToList();
            var dKept = new List<float>();
            for (int s = 0; s < samples.Count; s++)
                if (keepIdx.Contains(assign[s])) dKept.Add(ColorMetric.Distance(samples[s], all[assign[s]]));
            tol = explicitTolerance ?? Math.Clamp(1.25 * Percentile(dKept, 0.97) + 2, 6, 40);
        }

        double spread = samples.Count == 0 ? 0 : Percentile(samples.Select(l => centers.Min(c => ColorMetric.Distance(l, c))).ToList(), 0.97);
        return new BackgroundModel { Centers = centers, Tolerance = tol, Step = step, Flat = spread < 1 };
    }

    /// <summary>
    /// k-means in Lab with farthest-point initialization. Clusters are added (up to maxK) until the 97th percentile of the distance to the nearest center
    /// is within the bound. assign: the cluster of every sample, counts: the cluster sizes.
    /// </summary>
    public static List<LabColor> Cluster(List<LabColor> samples, int maxK, double bound, out int[] assign, out int[] counts)
    {
        maxK = Math.Clamp(maxK, 1, 8);
        var centers = new List<LabColor>
        {
            new(Median(samples.Select(s => s.L).ToList()), Median(samples.Select(s => s.A).ToList()), Median(samples.Select(s => s.B).ToList())),
        };
        assign = new int[samples.Count];
        var near = new float[samples.Count];
        while (true)
        {
            for (int iter = 0; iter < 5; iter++)
            {
                for (int s = 0; s < samples.Count; s++)
                {
                    int best = 0; float bd = float.MaxValue;
                    for (int c = 0; c < centers.Count; c++)
                    {
                        float d = ColorMetric.Distance(samples[s], centers[c]);
                        if (d < bd) { bd = d; best = c; }
                    }
                    assign[s] = best; near[s] = bd;
                }
                for (int c = 0; c < centers.Count; c++)
                {
                    double l = 0, a = 0, b = 0; int n = 0;
                    for (int s = 0; s < samples.Count; s++)
                        if (assign[s] == c) { l += samples[s].L; a += samples[s].A; b += samples[s].B; n++; }
                    if (n > 0) centers[c] = new LabColor((float)(l / n), (float)(a / n), (float)(b / n));
                }
            }
            if (centers.Count >= maxK || Percentile(near.ToList(), 0.97) <= bound) break;
            int far = Array.IndexOf(near, near.Max());
            if (near[far] < 1e-3f) break;
            centers.Add(samples[far]);
        }
        counts = new int[centers.Count];
        foreach (var a in assign) counts[a]++;
        return centers;
    }

    private static float Median(List<float> v) => Percentile(v, 0.5);

    public static float Percentile(List<float> values, double p)
    {
        if (values.Count == 0) return 0;
        var sorted = values.OrderBy(v => v).ToList();
        return sorted[Math.Clamp((int)Math.Round((sorted.Count - 1) * p), 0, sorted.Count - 1)];
    }
}
