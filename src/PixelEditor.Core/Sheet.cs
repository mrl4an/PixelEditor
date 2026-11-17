// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
namespace PixelEditor.Core;

/// <summary>Sprite sheets: cutting into cells and assembling frames into a sheet.</summary>
public static class Sheet
{
    public static int Columns(int imageWidth, int cellWidth) => Math.Max(1, imageWidth / Math.Max(1, cellWidth));
    public static int Rows(int imageHeight, int cellHeight) => Math.Max(1, imageHeight / Math.Max(1, cellHeight));

    /// <summary>Cuts a picture into cw×ch cells left to right, top to bottom. Incomplete cells at the edge are dropped.</summary>
    public static IEnumerable<(int Col, int Row, PixelImage Image)> Slice(PixelImage img, int cw, int ch, bool skipEmpty = false)
    {
        if (cw < 1 || ch < 1) throw new ArgumentException(CoreText.T("Размер ячейки должен быть > 0"));
        int cols = img.Width / cw, rows = img.Height / ch;
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                var cell = new PixelImage(cw, ch);
                for (int y = 0; y < ch; y++)
                    for (int x = 0; x < cw; x++)
                        cell.Pixels[y * cw + x] = img.Pixels[(r * ch + y) * img.Width + c * cw + x];
                if (skipEmpty && cell.OpaqueCount() == 0) continue;
                yield return (c, r, cell);
            }
    }

    /// <summary>Assembles same-size frames into a sheet with the given number of columns (0 = everything in one row).</summary>
    public static PixelImage Compose(IReadOnlyList<PixelImage> frames, int columns)
    {
        if (frames.Count == 0) throw new ArgumentException(CoreText.T("Нет кадров"));
        int cw = frames.Max(f => f.Width), ch = frames.Max(f => f.Height);
        if (columns <= 0 || columns > frames.Count) columns = frames.Count;
        int rows = (frames.Count + columns - 1) / columns;
        var sheet = new PixelImage(cw * columns, ch * rows);
        for (int i = 0; i < frames.Count; i++)
        {
            int ox = (i % columns) * cw, oy = (i / columns) * ch;
            var f = frames[i];
            for (int y = 0; y < f.Height; y++)
                for (int x = 0; x < f.Width; x++)
                    sheet.Pixels[(oy + y) * sheet.Width + ox + x] = f.Pixels[y * f.Width + x];
        }
        return sheet;
    }
}

public static class Animation
{
    /// <summary>
    /// Document frames: if a sheet grid is set and the canvas holds more than one cell, frames are the cells of the stitched image
    /// (a tail of empty cells is dropped); otherwise a frame is each layer from bottom to top.
    /// </summary>
    public static List<PixelImage> GetFrames(Document doc)
    {
        if (doc.HasSheetGrid && (doc.Width > doc.SheetCellWidth || doc.Height > doc.SheetCellHeight))
        {
            var cells = Sheet.Slice(doc.Flatten(), doc.SheetCellWidth, doc.SheetCellHeight).Select(c => c.Image).ToList();
            while (cells.Count > 1 && cells[^1].OpaqueCount() == 0) cells.RemoveAt(cells.Count - 1);
            return cells;
        }
        return doc.Layers.Select(l => l.ToImage()).ToList();
    }
}
