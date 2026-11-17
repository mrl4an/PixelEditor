// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.IO.Compression;
using System.Text;
using PixelEditor.Core;
using PixelEditor.Core.Ops;

namespace PixelEditor.Tests;

/// <summary>A synthetic picture with a known ground truth: Subject[i] is true where the pixel belongs to the subject.</summary>
public sealed record BgCase(string Name, PixelImage Image, bool[] Subject, bool Exact = false);

/// <summary>
/// Generators of synthetic background-removal inputs. Antialiased edges are made by 4x4 supersampling of a shape; a pixel belongs to the subject
/// when at least half of it is covered. Everything is deterministic (fixed seeds).
/// </summary>
public static class BgCases
{
    private const int W = 96, H = 80;

    private static uint Lerp(uint a, uint b, double t) => Pixel.Rgb(
        (byte)Math.Clamp(Math.Round(Pixel.R(a) + (Pixel.R(b) - Pixel.R(a)) * t), 0, 255),
        (byte)Math.Clamp(Math.Round(Pixel.G(a) + (Pixel.G(b) - Pixel.G(a)) * t), 0, 255),
        (byte)Math.Clamp(Math.Round(Pixel.B(a) + (Pixel.B(b) - Pixel.B(a)) * t), 0, 255));

    private static bool InEllipse(double x, double y, double cx, double cy, double rx, double ry)
    {
        double dx = (x - cx) / rx, dy = (y - cy) / ry;
        return dx * dx + dy * dy <= 1;
    }

    /// <summary>Composes background and subject. subject(x, y) returns a color inside the shape (x, y are subpixel coordinates), null outside.</summary>
    private static BgCase Compose(string name, int w, int h, Func<int, int, uint> bg, Func<double, double, uint?> subject, int noise, int seed, bool exact = false)
    {
        var img = new PixelImage(w, h);
        var truth = new bool[w * h];
        var rnd = new Random(seed);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int hits = 0, n = exact ? 1 : 4;
                double r = 0, g = 0, b = 0;
                for (int sy = 0; sy < n; sy++)
                    for (int sx = 0; sx < n; sx++)
                    {
                        var c = subject(x + (sx + 0.5) / n, y + (sy + 0.5) / n);
                        if (c is uint cc) { hits++; r += Pixel.R(cc); g += Pixel.G(cc); b += Pixel.B(cc); }
                    }
                uint back = bg(x, y), px = back;
                double cov = hits / (double)(n * n);
                if (hits > 0)
                {
                    uint sc = Pixel.Rgb((byte)Math.Round(r / hits), (byte)Math.Round(g / hits), (byte)Math.Round(b / hits));
                    px = Lerp(back, sc, cov);
                }
                truth[y * w + x] = cov >= 0.5;
                if (noise > 0)
                    px = Pixel.Rgb(
                        (byte)Math.Clamp(Pixel.R(px) + rnd.Next(-noise, noise + 1), 0, 255),
                        (byte)Math.Clamp(Pixel.G(px) + rnd.Next(-noise, noise + 1), 0, 255),
                        (byte)Math.Clamp(Pixel.B(px) + rnd.Next(-noise, noise + 1), 0, 255));
                img.Pixels[y * w + x] = px;
            }
        return new BgCase(name, img, truth, exact);
    }

    private static uint? Creature(double x, double y, uint body, uint stripe)
    {
        bool torso = InEllipse(x, y, 48, 44, 25, 21), head = InEllipse(x, y, 62, 22, 11, 10), ear = InEllipse(x, y, 66, 11, 4, 5);
        if (!(torso || head || ear)) return null;
        return y > 48 ? stripe : body;
    }

    public static IReadOnlyList<BgCase> All()
    {
        var list = new List<BgCase>();
        uint flat = Pixel.Rgb(232, 226, 210);

        list.Add(Compose("flat_antialiased", W, H, (_, _) => flat, (x, y) => Creature(x, y, Pixel.Rgb(205, 95, 40), Pixel.Rgb(150, 60, 30)), 0, 1));
        list.Add(Compose("noisy_flat_jpeg", W, H, (_, _) => flat, (x, y) => Creature(x, y, Pixel.Rgb(205, 95, 40), Pixel.Rgb(150, 60, 30)), 4, 2));

        uint g0 = Pixel.Rgb(70, 100, 170), g1 = Pixel.Rgb(180, 200, 235);
        list.Add(Compose("gradient_linear", W, H, (x, y) => Lerp(g0, g1, x / (double)W * 0.6 + y / (double)H * 0.4),
            (x, y) => Creature(x, y, Pixel.Rgb(200, 50, 50), Pixel.Rgb(140, 30, 40)), 2, 3));

        uint centerC = Pixel.Rgb(215, 210, 200), edgeC = Pixel.Rgb(100, 98, 112);
        list.Add(Compose("gradient_radial", W, H, (x, y) => Lerp(centerC, edgeC, Math.Min(1, Math.Sqrt((x - 48) * (x - 48) + (y - 40) * (y - 40)) / 62.0)),
            (x, y) => Creature(x, y, Pixel.Rgb(140, 90, 60), Pixel.Rgb(105, 65, 45)), 2, 4));

        uint floor = Pixel.Rgb(205, 195, 175);
        list.Add(Compose("soft_shadow", W, H,
            (x, y) =>
            {
                double d = ((x - 50.0) * (x - 50.0)) / (2 * 22 * 22) + ((y - 66.0) * (y - 66.0)) / (2 * 6 * 6);
                double k = 1 - 0.4 * Math.Exp(-d);
                return Pixel.Rgb((byte)(Pixel.R(floor) * k), (byte)(Pixel.G(floor) * k), (byte)(Pixel.B(floor) * k));
            },
            (x, y) =>
            {
                if (x < 36 || x > 62 || y < 16 || y > 64) return null;
                // rounded top corners
                if (y < 22 && !InEllipse(x, y, x < 49 ? 42 : 56, 22, 6.5, 6.5) && (x < 42 || x > 56)) return null;
                return Lerp(Pixel.Rgb(70, 120, 200), Pixel.Rgb(35, 75, 150), (x - 36) / 26.0);
            }, 2, 5));

        uint wall = Pixel.Rgb(192, 202, 218), floor2 = Pixel.Rgb(150, 118, 86);
        list.Add(Compose("two_tone_floor_wall", W, H, (_, y) => y < 44 ? wall : floor2,
            (x, y) => InEllipse(x, y, 48, 40, 22, 26) ? Pixel.Rgb(50, 140, 70) : null, 2, 6));

        uint calm = Pixel.Rgb(226, 223, 216);
        list.Add(Compose("similar_color_inside", W, H, (_, _) => calm,
            (x, y) =>
            {
                if (!InEllipse(x, y, 48, 40, 28, 24)) return null;
                if (x is >= 40 and < 50 && y is >= 34 and < 44) return Pixel.Rgb(228, 224, 218); // a patch almost like the background (white shirt)
                return Pixel.Rgb(55, 65, 130);
            }, 2, 7));

        list.Add(Compose("subject_touches_border", W, H, (_, _) => Pixel.Rgb(225, 215, 200),
            (x, y) => InEllipse(x, y, 48, 82, 22, 38) ? Pixel.Rgb(90, 140, 90) : null, 3, 8));

        list.Add(Compose("enclosed_ring", W, H, (_, _) => Pixel.Rgb(220, 225, 235),
            (x, y) => InEllipse(x, y, 48, 40, 30, 30) && !InEllipse(x, y, 48, 40, 17, 17) ? Pixel.Rgb(180, 70, 60) : null, 3, 9));

        // a heavy photo-like background: low-frequency blotches plus strong noise
        list.Add(Compose("textured_noisy", W, H,
            (x, y) =>
            {
                double v = 9 * Math.Sin(x * 0.21) * Math.Cos(y * 0.17) + 6 * Math.Sin((x + y) * 0.09);
                return Pixel.Rgb((byte)Math.Clamp(150 + v, 0, 255), (byte)Math.Clamp(170 + v, 0, 255), (byte)Math.Clamp(140 + v * 0.7, 0, 255));
            },
            (x, y) => Creature(x, y, Pixel.Rgb(205, 95, 40), Pixel.Rgb(150, 60, 30)), 7, 11));

        // informational: a subject of nearly the same tone as the background (a known weak spot; no quality is asserted)
        list.Add(Compose("low_contrast_subject", W, H, (_, _) => Pixel.Rgb(225, 215, 200),
            (x, y) => Creature(x, y, Pixel.Rgb(205, 195, 180), Pixel.Rgb(195, 185, 172)), 2, 12));

        // exact pixel art: no antialiasing, no noise, flat background and a few palette colors; the eye whites have the background color
        uint pbg = Pixel.Rgb(127, 178, 216), outl = Pixel.Rgb(30, 20, 40), body = Pixel.Rgb(200, 80, 60), belly = Pixel.Rgb(240, 200, 150), pupil = Pixel.Rgb(10, 10, 10);
        list.Add(Compose("pixel_art_exact", 32, 32, (_, _) => pbg,
            (x, y) =>
            {
                int px = (int)x, py = (int)y;
                if (!InEllipse(px + 0.5, py + 0.5, 16, 17, 10, 11)) return null;
                if (!InEllipse(px + 0.5, py + 0.5, 16, 17, 9, 10)) return outl;
                if (px is 12 or 13 && py is 12 or 13) return px == 12 && py == 13 ? pupil : pbg; // eye 2x2, three pixels of the background color
                if (px is 19 or 20 && py is 12 or 13) return px == 20 && py == 13 ? pupil : pbg;
                return py > 19 ? belly : body;
            }, 0, 10, exact: true));
        return list;
    }

    /// <summary>
    /// A painted illustration: a rider on a horse against a flat sky. The subject is made of brush strokes (streaky texture, per-pixel grain, a bluish cape
    /// close to the sky), and the sky shows through enclosed gaps: between the horse legs (closed by the ground), inside a loop of reins. Not part of
    /// <see cref="All"/>: the default removal is not expected to find those enclosed patches, only the simplified mode is.
    /// </summary>
    public static BgCase Painterly()
    {
        // the sky is painted too: broad soft strokes change its tone by a few levels (a flat wash would be too easy)
        uint Sky(int x, int y)
        {
            double v = 7 * Math.Sin(x * 0.11 + y * 0.05) + 5 * Math.Sin((x - y) * 0.07) + 4 * Math.Sin(y * 0.23 + x * 0.02);
            return Pixel.Rgb((byte)Math.Clamp(110 + v * 0.7, 0, 255), (byte)Math.Clamp(170 + v * 0.8, 0, 255), (byte)Math.Clamp(230 + v * 0.4, 0, 255));
        }
        uint Stroke(double x, double y, int r, int g, int b)
        {
            // streaks along a slanted direction plus a coarser wobble, like brush hairs
            double streak = 11 * Math.Sin((x * 0.8 + y * 0.35) * 0.9) + 7 * Math.Sin((x * 0.2 - y * 0.9) * 1.3);
            return Pixel.Rgb((byte)Math.Clamp(r + streak, 0, 255), (byte)Math.Clamp(g + streak * 0.8, 0, 255), (byte)Math.Clamp(b + streak * 0.6, 0, 255));
        }
        uint? Subject(double x, double y)
        {
            bool Leg(double x0) => x >= x0 && x <= x0 + 6 && y >= 82 && y <= 106;
            if (InEllipse(x, y, 60, 70, 34, 17)) return Stroke(x, y, 140, 90, 55);                        // horse body
            if (InEllipse(x, y, 96, 52, 8, 17) || InEllipse(x, y, 104, 40, 9, 7)) return Stroke(x, y, 150, 100, 60);   // neck and head
            if (Leg(34) || Leg(46) || Leg(68) || Leg(80)) return Stroke(x, y, 125, 80, 48);               // legs
            if (x >= 30 && x <= 90 && y > 106 && y <= 112) return Stroke(x, y, 70, 120, 60);              // the ground that closes the gaps between the legs
            if (InEllipse(x, y, 58, 44, 8, 17)) return Stroke(x, y, 105, 115, 140);                      // rider's armor
            if (InEllipse(x, y, 58, 22, 6, 6)) return Stroke(x, y, 215, 170, 140);                       // head
            if (x >= 40 && x <= 52 && y >= 36 && y <= 62 && x < 50 - (y - 36) * 0.1) return Stroke(x, y, 75, 105, 185);   // bluish cape
            double d = Math.Sqrt((x - 112) * (x - 112) + (y - 66) * (y - 66));
            if (d >= 6 && d <= 9) return Stroke(x, y, 90, 60, 40);                                         // reins: a ring that encloses a sky patch
            if (x >= 90 && x <= 104 && y >= 64 && y <= 68) return Stroke(x, y, 90, 60, 40);                // the rein to the head
            return null;
        }
        return Compose("painterly_sky", 128, 116, Sky, Subject, 3, 21);
    }

    // ---- measurements ----

    public sealed record Score(double BgIoU, double SubjIoU, int Eaten, int Left);

    public static Score Measure(PixelImage result, bool[] subject)
    {
        int interR = 0, unionR = 0, interS = 0, unionS = 0, eaten = 0, left = 0;
        for (int i = 0; i < subject.Length; i++)
        {
            bool removed = Pixel.IsEmpty(result.Pixels[i]), bgTruth = !subject[i];
            if (removed && bgTruth) interR++;
            if (removed || bgTruth) unionR++;
            if (!removed && subject[i]) interS++;
            if (!removed || subject[i]) unionS++;
            if (removed && subject[i]) eaten++;
            if (!removed && bgTruth) left++;
        }
        return new Score(unionR == 0 ? 1 : interR / (double)unionR, unionS == 0 ? 1 : interS / (double)unionS, eaten, left);
    }

    public static PixelImage Run(string op, PixelImage img, string json = "{}")
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        return Operations.Get(op).Run(img, new OpArgs(doc.RootElement, null));
    }

    // ---- PNG output (for looking at the pictures) ----

    public static void WritePng(string path, int w, int h, uint[] argb)
    {
        var raw = new byte[h * (w * 4 + 1)];
        for (int y = 0; y < h; y++)
        {
            raw[y * (w * 4 + 1)] = 0;
            for (int x = 0; x < w; x++)
            {
                uint p = argb[y * w + x];
                int o = y * (w * 4 + 1) + 1 + x * 4;
                raw[o] = Pixel.R(p); raw[o + 1] = Pixel.G(p); raw[o + 2] = Pixel.B(p); raw[o + 3] = Pixel.A(p);
            }
        }
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Fastest, true)) z.Write(raw);
        using var fs = File.Create(path);
        fs.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        WriteBE(ihdr, 0, (uint)w); WriteBE(ihdr, 4, (uint)h); ihdr[8] = 8; ihdr[9] = 6;
        Chunk(fs, "IHDR", ihdr);
        Chunk(fs, "IDAT", ms.ToArray());
        Chunk(fs, "IEND", []);
    }

    private static void WriteBE(byte[] b, int o, uint v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4]; WriteBE(len, 0, (uint)data.Length);
        s.Write(len);
        var t = Encoding.ASCII.GetBytes(type);
        s.Write(t); s.Write(data);
        uint crc = 0xFFFFFFFF;
        foreach (var b in t.Concat(data)) { crc ^= b; for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1; }
        var c = new byte[4]; WriteBE(c, 0, ~crc);
        s.Write(c);
    }

    /// <summary>Panels side by side (zoom ×4); transparent pixels are shown as magenta so removed areas are obvious.</summary>
    public static void WritePanels(string path, IReadOnlyList<PixelImage> panels, int zoom = 4)
    {
        int pw = panels.Max(p => p.Width), ph = panels.Max(p => p.Height), gap = 3;
        int ow = (pw * zoom + gap) * panels.Count, oh = ph * zoom;
        var o = new uint[ow * oh];
        Array.Fill(o, 0xFF404040u);
        for (int k = 0; k < panels.Count; k++)
        {
            var p = panels[k];
            for (int y = 0; y < p.Height * zoom; y++)
                for (int x = 0; x < p.Width * zoom; x++)
                {
                    uint c = p.Pixels[(y / zoom) * p.Width + x / zoom];
                    o[y * ow + k * (pw * zoom + gap) + x] = Pixel.IsEmpty(c) ? 0xFFFF00FFu : c | 0xFF000000u;
                }
        }
        WritePng(path, ow, oh, o);
    }

    public static PixelImage TruthImage(BgCase c)
    {
        var t = c.Image.Clone();
        for (int i = 0; i < t.Pixels.Length; i++) if (!c.Subject[i]) t.Pixels[i] = Pixel.Empty;
        return t;
    }
}

public class BackgroundBenchmark(Xunit.Abstractions.ITestOutputHelper output)
{
    /// <summary>Prints the IoU table of the default removeBackground and cutout on every synthetic case. PIXED_BG_SHOTS=folder also saves PNGs, PIXED_BG_TAG names them (before/after).</summary>
    [Fact]
    public void Report()
    {
        string? shots = Environment.GetEnvironmentVariable("PIXED_BG_SHOTS");
        string tag = Environment.GetEnvironmentVariable("PIXED_BG_TAG") ?? "run";
        if (shots != null) Directory.CreateDirectory(shots);
        var sb = new StringBuilder();
        sb.AppendLine($"{"case",-24} | {"RB bgIoU",8} {"subjIoU",8} {"eaten",6} {"left",6} | {"CO bgIoU",8} {"subjIoU",8} {"eaten",6} {"left",6}");
        foreach (var c in BgCases.All())
        {
            var rb = BgCases.Run("removeBackground", c.Image);
            var co = BgCases.Run("cutout", c.Image);
            var a = BgCases.Measure(rb, c.Subject);
            var b = BgCases.Measure(co, c.Subject);
            sb.AppendLine($"{c.Name,-24} | {a.BgIoU,8:F4} {a.SubjIoU,8:F4} {a.Eaten,6} {a.Left,6} | {b.BgIoU,8:F4} {b.SubjIoU,8:F4} {b.Eaten,6} {b.Left,6}");
            // quality floor of the defaults (measured before the rewrite: gradients 0.13-0.66, shadow 0.87, two-tone 0.53, ring 0.83 for removeBackground)
            Assert.True(a.BgIoU >= 0.99 && a.SubjIoU >= 0.985, $"removeBackground {c.Name}: {a.BgIoU:F4}/{a.SubjIoU:F4}");
            Assert.True(b.BgIoU >= 0.99 && b.SubjIoU >= 0.985, $"cutout {c.Name}: {b.BgIoU:F4}/{b.SubjIoU:F4}");
            if (shots != null)
                BgCases.WritePanels(Path.Combine(shots, $"{c.Name}_{tag}.png"), [c.Image, BgCases.TruthImage(c), rb, co], c.Image.Width <= 32 ? 8 : 4);
        }
        output.WriteLine(sb.ToString());
        if (shots != null) File.WriteAllText(Path.Combine(shots, $"table_{tag}.txt"), sb.ToString());
    }
}
