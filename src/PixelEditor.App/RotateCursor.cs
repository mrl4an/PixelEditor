// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PixelEditor.App;

/// <summary>Rotation cursor: a curved double arrow (as in Paint). Drawn in code, no resource files needed.</summary>
public static class RotateCursorFactory
{
    /// <summary>true if the custom cursor was built; otherwise the ordinary "hand" is used (for the smoke test check).</summary>
    public static bool Succeeded { get; private set; }

    public static Cursor Create()
    {
        try
        {
            const int size = 32;
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                var arc = new StreamGeometry();
                using (var g = arc.Open())
                {
                    g.BeginFigure(new Point(6, 22), false, false);
                    g.ArcTo(new Point(26, 22), new Size(14, 14), 0, false, SweepDirection.Clockwise, true, true);
                }
                void Head(Point tip, Point a, Point b)
                {
                    var h = new StreamGeometry();
                    using (var g = h.Open())
                    {
                        g.BeginFigure(a, true, true);
                        g.LineTo(tip, true, false);
                        g.LineTo(b, true, false);
                    }
                    dc.DrawGeometry(Brushes.Black, new Pen(Brushes.White, 1.6), h);
                }
                dc.DrawGeometry(null, new Pen(Brushes.White, 5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, arc);
                dc.DrawGeometry(null, new Pen(Brushes.Black, 2.4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, arc);
                Head(new Point(3, 25), new Point(3, 17), new Point(11, 24));
                Head(new Point(29, 25), new Point(29, 17), new Point(21, 24));
            }
            var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using var png = new MemoryStream();
            enc.Save(png);
            var data = png.ToArray();

            using var cur = new MemoryStream();
            using var w = new BinaryWriter(cur);
            w.Write((short)0); w.Write((short)2); w.Write((short)1);                 // type 2 = cursor, one frame
            w.Write((byte)size); w.Write((byte)size); w.Write((byte)0); w.Write((byte)0);
            w.Write((short)16); w.Write((short)16);                                  // hot spot in the center
            w.Write(data.Length); w.Write(22);
            w.Write(data);
            w.Flush();
            cur.Position = 0;
            var cursor = new Cursor(cur);
            Succeeded = true;
            return cursor;
        }
        catch
        {
            return Cursors.Hand;
        }
    }
}
