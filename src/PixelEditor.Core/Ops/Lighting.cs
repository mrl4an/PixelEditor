// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
namespace PixelEditor.Core.Ops;

/// <summary>Settings of <see cref="Lighting.Light"/>.</summary>
public sealed class LightOptions
{
    /// <summary>Direction toward the light source (see <see cref="Lighting.ToLight"/>).</summary>
    public (int Dx, int Dy) ToLight { get; set; } = (-1, -1);
    /// <summary>Size of the color steps: 1 = the steps of PaletteTools.ShadeRamp.</summary>
    public double Strength { get; set; } = 0.8;
    /// <summary>2: base + shadow; 3: shadow, base, light; 4: + deep shadow; 5: + specular.</summary>
    public int Bands { get; set; } = 3;
    /// <summary>How far (pixels) the volume rises from the edge; 0 or 1 = automatic from the sprite size.</summary>
    public int Depth { get; set; }
    /// <summary>Dark pixels on the silhouette edge (the outline) are left untouched and are not part of the volume.</summary>
    public bool KeepOutline { get; set; } = true;
    /// <summary>0..1: how much inner color regions shape the volume (0 = only the silhouette).</summary>
    public double PerRegion { get; set; } = 0.2;
    /// <summary>A thin extra-bright line on the lit edge.</summary>
    public bool Rim { get; set; }
    /// <summary>Light elevation above the picture plane, degrees (90 = from the viewer, no shading).</summary>
    public double Elevation { get; set; } = 40;
    /// <summary>Ordered dithering on the band borders (off: crisp borders).</summary>
    public bool Dither { get; set; }
    /// <summary>When set, every produced color is snapped to this palette (colors of the picture itself stay as they are).</summary>
    public IReadOnlyList<uint>? Palette { get; set; }
    /// <summary>Extra multipliers of the light and shadow steps (the legacy highlight / shadow arguments).</summary>
    public double LightScale { get; set; } = 1;
    public double ShadowScale { get; set; } = 1;
}

/// <summary>
/// Pixel-art lighting of a flat-colored sprite. The opaque silhouette (without a dark outline) is a height field: a rounded "pillow" that rises
/// with the distance from the edge (plus smaller domes for the inner color regions). From the height a normal is derived and lit by a directional
/// light; the light value is cut into a few bands and every band maps to a step of the pixel's own hue-shifted shade ramp.
/// No gradients: a handful of crisp colors per base color.
/// </summary>
public static class Lighting
{
    /// <summary>Direction "toward the light source" by the side name: N, S, E, W, NE, NW, SE, SW (NW by default).</summary>
    public static (int Dx, int Dy) ToLight(string? from) => (from ?? "NW").Trim().ToUpperInvariant() switch
    {
        "N" => (0, -1), "S" => (0, 1), "E" => (1, 0), "W" => (-1, 0),
        "NE" => (1, -1), "NW" => (-1, -1), "SE" => (1, 1), "SW" => (-1, 1),
        var s => throw new ArgumentException(CoreText.F("edgeShade: неизвестная сторона света '{0}' (N, S, E, W, NE, NW, SE, SW)", s)),
    };

    /// <summary>
    /// Legacy entry point (edgeShade, scene groups, sprite styles): the same banded lighting with three bands.
    /// depth 0 or 1 = automatic; highlight and shadow scale the light and shadow steps (0.2 and 0.25 are the neutral values).
    /// </summary>
    public static PixelImage EdgeShade(PixelImage src, (int Dx, int Dy) toLight, int depth, double highlight, double shadow) =>
        Light(src, new LightOptions
        {
            ToLight = toLight, Depth = depth, Bands = 3,
            LightScale = Math.Max(0, highlight) / 0.2, ShadowScale = Math.Max(0, shadow) / 0.25,
        });

    // Band borders on the light value u = (normal . light) - (flat surface value).
    private const double ShadowBorder = -0.14, DeepBorder = -0.40, LightBorder = 0.14, SpecBorder = 0.34;

    private const double Inf = 1e15;
    private const int TinyRegion = 6;

    public static PixelImage Light(PixelImage src, LightOptions o)
    {
        int w = src.Width, h = src.Height, n = w * h;
        var dst = src.Clone();
        if (n == 0) return dst;
        var px = src.Pixels;
        int bands = Math.Clamp(o.Bands, 2, 5);
        double pr = Math.Clamp(o.PerRegion, 0, 1);

        // outline (dark pixels on the edge) and body (the part that gets a volume)
        var outline = o.KeepOutline ? DetectOutline(px, w, h) : new bool[n];
        var body = new bool[n];
        for (int i = 0; i < n; i++) body[i] = !Pixel.IsEmpty(px[i]) && !outline[i];

        // distance from the silhouette edge (pixels outside the body are the sources; the canvas border is not an edge)
        var f = new double[n];
        for (int i = 0; i < n; i++) f[i] = body[i] ? Inf : 0;
        var dSil = Edt(f, w, h);
        double maxD = 0;
        for (int i = 0; i < n; i++) if (body[i] && dSil[i] < 1e6) maxD = Math.Max(maxD, dSil[i]);
        if (maxD == 0) return dst; // nothing with a finite distance: nothing to shape

        int depth = o.Depth >= 2 ? o.Depth : Math.Clamp((int)Math.Round(maxD * 0.45), 2, 12);
        double regionDepth = Math.Max(2, depth * 0.6);

        // inner color regions
        double[]? dReg = null;
        if (pr > 0)
        {
            var label = LabelRegions(px, body, w, h, out var tiny);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (!body[i]) { f[i] = 0; continue; }
                    f[i] = Inf;
                    if (tiny[label[i]]) continue;
                    if (x > 0 && Differs(i, i - 1)) f[i] = 1;
                    else if (x < w - 1 && Differs(i, i + 1)) f[i] = 1;
                    else if (y > 0 && Differs(i, i - w)) f[i] = 1;
                    else if (y < h - 1 && Differs(i, i + w)) f[i] = 1;
                }
            dReg = Edt(f, w, h);
            bool Differs(int a, int b) => body[b] && label[a] != label[b] && !tiny[label[b]];
        }

        // height field
        var z = new double[n];
        for (int i = 0; i < n; i++)
        {
            if (!body[i]) continue;
            double hs = Profile(Math.Min(dSil[i], depth) / depth);
            double hr = dReg == null ? 0 : Profile(Math.Min(dReg[i], regionDepth) / regionDepth);
            z[i] = ((1 - pr) * hs + pr * hr) * depth;
        }

        // soften the staircase of the distance field so the band borders are smooth curves, not teeth
        z = Blur(z, w, h);
        z = Blur(z, w, h);

        // light vector
        double el = Math.Clamp(o.Elevation, 5, 85) * Math.PI / 180, ce = Math.Cos(el), se = Math.Sin(el);
        double ln = Math.Sqrt(o.ToLight.Dx * o.ToLight.Dx + o.ToLight.Dy * o.ToLight.Dy);
        double lx = ln == 0 ? 0 : o.ToLight.Dx / ln * ce, ly = ln == 0 ? 0 : o.ToLight.Dy / ln * ce;
        double flat = se;

        var cache = new Dictionary<(uint, int), uint>();
        var palLab = o.Palette is { Count: > 0 } ? o.Palette.Select(ColorMetric.ToLab).ToArray() : null;
        double lightScale = o.Strength * o.LightScale, shadowScale = o.Strength * o.ShadowScale;

        var levels = new sbyte[n];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (!body[i]) continue;
                int xl = Math.Max(x - 1, 0), xr = Math.Min(x + 1, w - 1), yu = Math.Max(y - 1, 0), yd = Math.Min(y + 1, h - 1);
                double zx = (z[y * w + xr] - z[y * w + xl]) / Math.Max(1, xr - xl);
                double zy = (z[yd * w + x] - z[yu * w + x]) / Math.Max(1, yd - yu);
                double len = Math.Sqrt(zx * zx + zy * zy + 1);
                double u = (-zx * lx - zy * ly + se) / len - flat;
                if (o.Dither) u += (Bayer4[y & 3, x & 3] - 0.5) * 0.12;

                int level = 0;
                if (u < ShadowBorder) level = -1;
                if (bands >= 4 && u < DeepBorder) level = -2;
                if (bands >= 3 && u > LightBorder) level = 1;
                if (bands == 5 && u > SpecBorder) level = 2;
                levels[i] = (sbyte)level;
            }

        // remove lone pixels: a level that no neighbor of the same color shares takes the level of the majority
        if (!o.Dither) levels = Majority(levels, px, body, w, h);

        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (!body[i]) continue;
                int level = levels[i];
                if (o.Rim && level >= 1 && OnLitEdge(body, px, w, h, x, y, o.ToLight)) level = Math.Max(level, 2);
                if (level == 0) continue;
                uint c = px[i];
                if (!cache.TryGetValue((c, level), out uint r))
                {
                    r = PaletteTools.ShadeStep(c, level, lightScale, shadowScale);
                    if (palLab != null) r = Snap(r, c, level, o.Palette!, palLab);
                    cache[(c, level)] = r;
                }
                dst.Pixels[i] = r;
            }
        return dst;
    }

    private static sbyte[] Majority(sbyte[] lv, uint[] px, bool[] body, int w, int h)
    {
        var res = (sbyte[])lv.Clone();
        Span<int> count = stackalloc int[5];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (!body[i]) continue;
                count.Clear();
                int total = 0;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if ((dx == 0 && dy == 0) || nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        int j = ny * w + nx;
                        if (!body[j] || px[j] != px[i]) continue;
                        count[lv[j] + 2]++; total++;
                    }
                if (total < 4 || count[lv[i] + 2] > 1) continue;
                int best = 0;
                for (int k = 1; k < 5; k++) if (count[k] > count[best]) best = k;
                if (count[best] >= 4) res[i] = (sbyte)(best - 2);
            }
        return res;
    }

    /// <summary>3x3 binomial blur (edges replicated).</summary>
    private static double[] Blur(double[] a, int w, int h)
    {
        var t = new double[a.Length];
        var r = new double[a.Length];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                t[y * w + x] = (a[y * w + Math.Max(x - 1, 0)] + 2 * a[y * w + x] + a[y * w + Math.Min(x + 1, w - 1)]) / 4;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                r[y * w + x] = (t[Math.Max(y - 1, 0) * w + x] + 2 * t[y * w + x] + t[Math.Min(y + 1, h - 1) * w + x]) / 4;
        return r;
    }

    private static readonly double[,] Bayer4 =
    {
        { 0.5 / 16, 8.5 / 16, 2.5 / 16, 10.5 / 16 },
        { 12.5 / 16, 4.5 / 16, 14.5 / 16, 6.5 / 16 },
        { 3.5 / 16, 11.5 / 16, 1.5 / 16, 9.5 / 16 },
        { 15.5 / 16, 7.5 / 16, 13.5 / 16, 5.5 / 16 },
    };

    /// <summary>Rounded profile: steep at the edge, flat in the middle (0..1 to 0..1).</summary>
    private static double Profile(double t) => 1 - (1 - t) * (1 - t);

    private static bool OnLitEdge(bool[] body, uint[] px, int w, int h, int x, int y, (int Dx, int Dy) toLight)
    {
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if ((dx == 0 && dy == 0) || dx * toLight.Dx + dy * toLight.Dy <= 0) continue;
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue; // the canvas border is not an edge
                if (!body[ny * w + nx]) return true;
            }
        return false;
    }

    /// <summary>
    /// Outline: dark pixels (luma &lt;= 80) on the silhouette edge that have a clearly lighter neighbor inside; one more layer
    /// of dark pixels next to the first one (a two pixel outline). A uniformly dark sprite has no outline in this sense.
    /// </summary>
    private static bool[] DetectOutline(uint[] px, int w, int h)
    {
        var res = new bool[w * h];
        bool Opaque(int x, int y) => (uint)x < (uint)w && (uint)y < (uint)h && !Pixel.IsEmpty(px[y * w + x]);
        for (int pass = 0; pass < 2; pass++)
        {
            var found = new List<int>();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (res[i] || Pixel.IsEmpty(px[i])) continue;
                    double l = PaletteTools.Luma(px[i]);
                    if (l > 80) continue;
                    bool touches = false, lighter = false;
                    for (int d = 0; d < 4; d++)
                    {
                        int nx = x + (d == 0 ? -1 : d == 1 ? 1 : 0), ny = y + (d == 2 ? -1 : d == 3 ? 1 : 0);
                        bool inside = (uint)nx < (uint)w && (uint)ny < (uint)h;
                        if (inside && Pixel.IsEmpty(px[ny * w + nx]) && pass == 0) touches = true;
                        if (inside && res[ny * w + nx] && pass == 1) touches = true;
                    }
                    if (!touches) continue;
                    for (int dy = -1; dy <= 1 && !lighter; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                            if (Opaque(x + dx, y + dy) && !res[(y + dy) * w + x + dx] && PaletteTools.Luma(px[(y + dy) * w + x + dx]) >= l + 20) { lighter = true; break; }
                    if (lighter) found.Add(i);
                }
            foreach (int i in found) res[i] = true;
        }
        return res;
    }

    /// <summary>Connected (4-neighbor) areas of one exact color inside the body; tiny[label] marks areas too small to shape the volume.</summary>
    private static int[] LabelRegions(uint[] px, bool[] body, int w, int h, out bool[] tiny)
    {
        var label = new int[w * h];
        Array.Fill(label, -1);
        var sizes = new List<int>();
        var stack = new Stack<int>();
        for (int s = 0; s < label.Length; s++)
        {
            if (!body[s] || label[s] >= 0) continue;
            int id = sizes.Count, count = 0;
            label[s] = id;
            stack.Push(s);
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                count++;
                int x = i % w, y = i / w;
                void Try(int j) { if (body[j] && label[j] < 0 && px[j] == px[s]) { label[j] = id; stack.Push(j); } }
                if (x > 0) Try(i - 1);
                if (x < w - 1) Try(i + 1);
                if (y > 0) Try(i - w);
                if (y < h - 1) Try(i + w);
            }
            sizes.Add(count);
        }
        tiny = new bool[sizes.Count + 1];
        for (int k = 0; k < sizes.Count; k++) tiny[k] = sizes[k] < TinyRegion;
        for (int i = 0; i < label.Length; i++) if (label[i] < 0) label[i] = sizes.Count; // outside the body (never tiny-checked as differing)
        return label;
    }

    /// <summary>Palette color for a shade step: the nearest to the target; never the base color itself for a non-zero step if a fitting neighbor exists.</summary>
    private static uint Snap(uint target, uint baseColor, int level, IReadOnlyList<uint> pal, LabColor[] palLab)
    {
        var t = ColorMetric.ToLab(target);
        int best = -1; double bd = double.MaxValue;
        for (int k = 0; k < pal.Count; k++)
        {
            double d = ColorMetric.Distance(t, palLab[k]);
            if (d < bd) { bd = d; best = k; }
        }
        uint pick = pal[best] | 0xFF000000u;
        if ((pick & 0xFFFFFFu) != (baseColor & 0xFFFFFFu)) return pick;

        // the nearest palette color is the base color: look for the nearest one on the right side of it
        double baseLuma = PaletteTools.Luma(baseColor), limit = ColorMetric.Distance(target, baseColor) * 2.5 + 10;
        int alt = -1; double ad = double.MaxValue;
        for (int k = 0; k < pal.Count; k++)
        {
            uint c = pal[k] | 0xFF000000u;
            if ((c & 0xFFFFFFu) == (baseColor & 0xFFFFFFu)) continue;
            double l = PaletteTools.Luma(c);
            if (level > 0 ? l <= baseLuma : l >= baseLuma) continue;
            double d = ColorMetric.Distance(t, palLab[k]);
            if (d < ad && ColorMetric.Distance(c, baseColor) <= limit) { ad = d; alt = k; }
        }
        return alt >= 0 ? pal[alt] | 0xFF000000u : baseColor;
    }

    /// <summary>Exact Euclidean distance transform (Felzenszwalb): f is the initial squared cost per pixel (0 for a source), result is a distance.</summary>
    private static double[] Edt(double[] f, int w, int h)
    {
        var g = new double[w * h];
        int m = Math.Max(w, h);
        var col = new double[m]; var d = new double[m]; var v = new int[m]; var zz = new double[m + 1];
        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++) col[y] = f[y * w + x];
            Edt1D(col, h, d, v, zz);
            for (int y = 0; y < h; y++) g[y * w + x] = d[y];
        }
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++) col[x] = g[y * w + x];
            Edt1D(col, w, d, v, zz);
            for (int x = 0; x < w; x++) g[y * w + x] = Math.Sqrt(d[x]);
        }
        return g;
    }

    private static void Edt1D(double[] f, int n, double[] d, int[] v, double[] z)
    {
        int k = 0;
        v[0] = 0; z[0] = double.NegativeInfinity; z[1] = double.PositiveInfinity;
        for (int q = 1; q < n; q++)
        {
            double s;
            while (true)
            {
                int p = v[k];
                s = ((f[q] + (double)q * q) - (f[p] + (double)p * p)) / (2.0 * q - 2.0 * p);
                if (s <= z[k]) k--; else break;
                if (k < 0) { k = 0; break; }
            }
            k++; v[k] = q; z[k] = s; z[k + 1] = double.PositiveInfinity;
        }
        k = 0;
        for (int q = 0; q < n; q++)
        {
            while (z[k + 1] < q) k++;
            double dq = q - v[k];
            d[q] = dq * dq + f[v[k]];
        }
    }
}
