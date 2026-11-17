// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Text;

namespace PixelEditor.Core.Ops;

/// <summary>
/// Prompts for an ordinary AI chat that DRAWS from nothing, with no reference picture: a sprite from primitives (a scene) and a whole sprite sheet
/// with custom frames. The answer is a short JSON that the editor renders and previews. Always English, like <see cref="ChatPrompt"/>.
/// </summary>
public static class ChatScenePrompt
{
    private static void Format(Action<string> line)
    {
        line("## Reply format");
        line("- Reply with a single JSON object inside one ```json code block. A short plain-text explanation (1-3 sentences) may come BEFORE the block, nothing after it.");
        line("- No comments inside the JSON, no trailing commas, straight double quotes only. Coordinates are whole numbers.");
    }

    private static void ShapeReference(Action<string> line)
    {
        line("## Shapes (the \"items\" list; drawn in order, later ones on top of earlier ones)");
        line("- {\"rect\":[x,y,w,h], \"color\":\"...\"}  optional: \"fill\":false (outline only), \"radius\":n (rounded corners), \"thickness\":n");
        line("- {\"ellipse\":[x,y,w,h], \"color\":\"...\"}  an ellipse inscribed in the frame; \"fill\":false for the outline only");
        line("- {\"line\":[x1,y1,x2,y2], \"color\":\"...\", \"thickness\":n}  both end pixels included");
        line("- {\"poly\":[[x,y],[x,y],...], \"color\":\"...\"}  a filled polygon (\"fill\":false: only its outline)");
        line("- {\"pixels\":[[x,y],[x,y],...], \"color\":\"...\"}  single pixels, for eyes, highlights and small details");
        line("- {\"fill\":[x,y], \"color\":\"...\"}  flood fill from a point (only when the area is closed)");
        line("- {\"group\":[ ...items... ], \"offset\":[dx,dy], \"mirror\":\"x\", \"lightFrom\":\"NW\"}  items drawn in local coordinates and moved by offset; lightFrom adds a highlight toward that side and a shadow opposite");
        line("- Every shape takes \"color\": \"#RRGGBB\", a name from \"palette\", or \"clear\" to erase. Any shape may take \"mirror\":\"x\" | \"y\" | \"xy\" (also drawn mirrored about the frame center).");
        line("- Coordinates: origin at the top left, x to the right, y down. rect and ellipse use [x, y, width, height]; they must stay inside the frame.");
    }

    private static void Craft(Action<string> line)
    {
        line("## Rules that make good pixel art");
        line("- Use a small palette of 5-10 colors and give every color a name in \"palette\" (skin, hair, coat, shadow, outline...). Never use more colors than needed.");
        line("- Block in the big shapes first (body, head), then the middle shapes, then details (eyes, buttons). Prefer rect, ellipse and poly over many single pixels.");
        line("- Symmetric things (a front view of a face or body): draw one half and use \"mirror\":\"x\" on the shape or on the whole thing.");
        line("- \"outline\":\"#RRGGBB\" at the top level draws a 1 px outline AROUND the finished drawing and does not enlarge the canvas: leave at least 1 free pixel on every side.");
        line("- Light comes from the top left: add one lighter and one darker shade of the main colors for volume (a group with \"lightFrom\":\"NW\" does it automatically).");
        line("- The background stays transparent. Do not add a \"background\" unless I ask for one.");
        line("- Keep it short: usually 10-40 shapes. Check that every coordinate is inside the frame.");
    }

    /// <summary>A prompt for ONE sprite described as a scene.</summary>
    public static string Sprite()
    {
        var sb = new StringBuilder();
        void Line(string s = "") => sb.AppendLine(s);
        Line("You are a pixel-art sprite designer. I will describe a sprite. Reply with ONE scene as JSON for the editor \"Pixel Editor\": the editor draws it from your primitives (rectangles, ellipses, lines, polygons).");
        Line();
        Format(Line);
        Line("- Shape of the object: {\"name\":\"...\", \"size\":[width,height], \"palette\":{\"name\":\"#RRGGBB\"}, \"mirror\":\"x\" (optional), \"outline\":\"#RRGGBB\" (optional), \"items\":[ ... ]}");
        Line("- Pick the canvas size yourself unless I give one: 16x16 for an item or icon, 24x32 for a person or animal, up to 64x64 for something big. Maximum 512x512.");
        Line();
        ShapeReference(Line);
        Line();
        Craft(Line);
        Line();
        Line("## Examples");
        Line("A little person, front view (24x32):");
        Line("```json");
        Line(Person);
        Line("```");
        Line("A treasure chest (24x20):");
        Line("```json");
        Line(Chest);
        Line("```");
        Line();
        Line("Now I will describe the sprite I want. If something is unclear, make the most sensible choice and say it in the short explanation before the block.");
        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    /// <summary>A prompt for a whole sprite sheet: directions as rows, animation frames as columns, custom frames.</summary>
    /// <param name="layout">The REQUIRED layout (frame size, rows, frames, delays); null: the classic free prompt (the AI picks S, N, E, W or its own rows).</param>
    public static string Sheet(SheetLayoutSpec? layout = null)
    {
        var sb = new StringBuilder();
        void Line(string s = "") => sb.AppendLine(s);
        Line("You are a pixel-art sprite designer. I will describe a character or object and the frames I need. Reply with ONE sprite sheet as JSON for the editor \"Pixel Editor\": the editor draws every frame from your primitives and lays them out in a grid (one row per direction or animation, one column per frame).");
        Line();
        Format(Line);
        Line("- Shape of the object: {\"name\":\"...\", \"frameSize\":[w,h], \"palette\":{...}, \"outline\":\"#RRGGBB\" (optional), \"mirror\":\"x\" (optional), \"delay\":0.15 (seconds, default for every frame), \"base\":[ ...items ], \"rows\":[ ... ]}");
        Line("- A row: {\"name\":\"S\", \"base\":[ ...items for this row only ], \"frames\":[ {\"name\":\"stand\", \"delay\":0.5, \"items\":[ ... ]}, ... ]}");
        Line("- A frame is drawn as: sheet \"base\" + row \"base\" + the frame's \"items\". So put the parts that never change in \"base\" (the body) and list in each frame ONLY what differs (the legs, an arm, a bob). This keeps the answer short.");
        Line("- A frame may also have \"offset\":[dx,dy] (the whole frame is shifted, good for a one pixel bob) or \"copyOf\":\"<name of an earlier frame of the same row>\" (the same picture again, with its own delay).");
        Line("- A row without \"frames\" can be \"mirrorOf\":\"E\" (every frame of row E flipped left-right: use it for W when E is drawn) or \"copyOf\":\"S\" (an exact copy). The row it refers to must have its own frames.");
        if (layout == null)
            Line("- Row order for directions is S, N, E, W (the standard order of the RSI format: S faces the viewer, N shows the back, E and W are the sides). If I ask for another set of rows (idle, walk, attack), name the rows that way. Up to 16 rows and 32 frames per row. Frame size up to 256x256.");
        else
            Line("- Up to 16 rows and 32 frames per row, frame size up to 256x256: the required layout below is within these limits.");
        Line("- Give frames of the same column the same \"name\" in every row (stand, step1, step2): the editor takes the frame names and delays from the first row that sets them.");
        if (layout != null)
        {
            Line();
            RequiredLayout(Line, layout);
        }
        Line();
        ShapeReference(Line);
        Line();
        Craft(Line);
        Line("- Keep the character the same size and in the same place in every frame: only the moving parts change. Use the same palette names everywhere.");
        if (layout == null)
            Line("- For a walk cycle use 3-4 frames (stand, step1, stand, step2 via copyOf); for an idle use 2 frames with offset [0,1].");
        Line();
        Line("## Example");
        if (layout == null)
        {
            Line("A person walking in 4 directions (24x32; S, N and E are drawn, W is the mirror of E):");
            Line("```json");
            Line(Walker);
        }
        else
        {
            Line($"The required layout ({layout.FrameWidth}x{layout.FrameHeight}, rows {string.Join(", ", layout.Rows.Select(Quote))}) with a placeholder figure; draw what I describe, keep these row names, frame names and delays." +
                 (layout.Rows.Count > 2 ? " To keep the example short, some rows only copy another row (copyOf, mirrorOf): in your answer draw every row that differs." : ""));
            Line("```json");
            Line(ExampleSheet(layout));
        }
        Line("```");
        Line();
        Line("Now I will describe the character and the frames I need. If something is unclear, make the most sensible choice and say it in the short explanation before the block.");
        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    private static readonly System.Text.Json.JsonSerializerOptions Relaxed = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static string Quote(string s) => System.Text.Json.JsonSerializer.Serialize(s, Relaxed);

    private static string Seconds(double d) => d.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    private static void RequiredLayout(Action<string> line, SheetLayoutSpec l)
    {
        line("## REQUIRED layout (the editor is waiting for exactly this sheet: follow it, do not choose your own)");
        line($"- \"frameSize\": [{l.FrameWidth},{l.FrameHeight}]");
        line($"- \"rows\" in exactly this order, with exactly these names ({l.Rows.Count}): {string.Join(", ", l.Rows.Select(Quote))}");
        if (l.UniformRows)
            line($"- every row has exactly these {l.Columns} frames, in this order, with these names and delays in seconds: " +
                 string.Join(", ", l.Frames.Select((f, i) => $"{i + 1}. {Quote(f.Name)} (delay {Seconds(f.Delay)})")));
        else
        {
            line($"- every row has exactly {l.Columns} frames, in this order, with these names: {string.Join(", ", l.Frames.Select(f => Quote(f.Name)))}");
            line("- delays in seconds per row:");
            for (int r = 0; r < l.Rows.Count; r++)
                line($"  - {Quote(l.Rows[r])}: " + string.Join(", ", Enumerable.Range(0, l.Columns).Select(c => Seconds(l.DelayAt(r, c)))));
        }
        line("- Put \"name\" and \"delay\" on every frame exactly as listed. Do not add, remove, rename or reorder rows or frames, and do not change the frame size. A row that uses copyOf or mirrorOf takes the frames of the row it refers to, so it must refer to a row with the same delays.");
    }

    /// <summary>
    /// An example answer for ANY layout (the tests check that it renders and has exactly this layout): a placeholder figure drawn in the first rows;
    /// a row named W mirrors E when E is drawn, the remaining rows copy the first one.
    /// </summary>
    public static string ExampleSheet(SheetLayoutSpec l)
    {
        int fw = l.FrameWidth, fh = l.FrameHeight;
        var rowsNames = l.Rows.Take(16).ToList();
        var frames = l.Frames.Take(32).ToList();
        int bw = Math.Max(1, fw / 2), bh = Math.Max(1, fh / 2), bx = (fw - bw) / 2, by = Math.Max(0, fh - bh - Math.Max(0, fh / 8));
        int eIndex = rowsNames.FindIndex(n => string.Equals(n, "E", StringComparison.OrdinalIgnoreCase));
        var drawn = new HashSet<int> { 0 };
        if (rowsNames.Count > 1) drawn.Add(1);
        if (eIndex >= 0) drawn.Add(eIndex);

        static string Rect(int fw, int fh, int x, int y, int w, int h, string color)
        {
            // clamp into the frame so that the example stays valid for any frame size
            x = Math.Clamp(x, 0, fw - 1); y = Math.Clamp(y, 0, fh - 1);
            w = Math.Clamp(w, 1, fw - x); h = Math.Clamp(h, 1, fh - y);
            return $"{{\"rect\":[{x},{y},{w},{h}],\"color\":\"{color}\"}}";
        }

        var sb = new StringBuilder();
        sb.Append($"{{\"name\":\"example\",\"frameSize\":[{fw},{fh}],\"palette\":{{\"body\":\"#3B5BA5\",\"skin\":\"#F1C27D\",\"legs\":\"#3A3A4A\",\"mark\":\"#C9B037\"}},\"base\":[");
        sb.Append(Rect(fw, fh, bx, by, bw, bh, "body")).Append(',');
        sb.Append(Rect(fw, fh, bx + bw / 4, by - bh / 2, Math.Max(1, bw / 2), Math.Max(1, bh / 2), "skin"));
        sb.Append("],\"rows\":[");
        for (int r = 0; r < rowsNames.Count; r++)
        {
            if (r > 0) sb.Append(',');
            sb.Append($"{{\"name\":{Quote(rowsNames[r])},");
            if (!drawn.Contains(r))
            {
                bool mirror = eIndex >= 0 && string.Equals(rowsNames[r], "W", StringComparison.OrdinalIgnoreCase);
                sb.Append(mirror ? $"\"mirrorOf\":{Quote(rowsNames[eIndex])}}}" : $"\"copyOf\":{Quote(rowsNames[0])}}}");
                continue;
            }
            sb.Append($"\"base\":[{Rect(fw, fh, bx + r % 3, by + bh - 1, 1, 1, "mark")}],\"frames\":[");
            for (int c = 0; c < frames.Count; c++)
            {
                if (c > 0) sb.Append(',');
                int lw = Math.Max(1, bw / 3);
                int lx = bx + (c % 2 == 0 ? 0 : bw - lw);
                sb.Append($"{{\"name\":{Quote(frames[c].Name)},\"delay\":{Seconds(l.DelayAt(r, c))},\"items\":[{Rect(fw, fh, lx, by + bh, lw, Math.Max(1, fh / 8) + c % 3, "legs")}]}}");
            }
            sb.Append("]}");
        }
        sb.Append("]}");
        return PipelineText.Pretty(sb.ToString());
    }

    /// <summary>The JSON examples inside the prompts (the tests check that they are valid).</summary>
    public const string Person = """
        {"name":"person","size":[24,32],"palette":{"skin":"#F1C27D","hair":"#5A3A1E","coat":"#3B5BA5","coatDark":"#2A4078","pants":"#3A3A4A","boots":"#222222","outline":"#1B1B1B"},
         "mirror":"x","outline":"#1B1B1B","items":[
          {"ellipse":[7,2,10,9],"color":"skin"},
          {"rect":[6,1,12,4],"color":"hair","radius":2},
          {"pixels":[[9,7],[14,7]],"color":"#1B1B1B"},
          {"rect":[6,12,12,9],"color":"coat"},
          {"rect":[6,18,12,3],"color":"coatDark"},
          {"rect":[2,12,3,8],"color":"coat"},{"rect":[2,20,3,2],"color":"skin"},
          {"rect":[7,21,4,8],"color":"pants"},{"rect":[7,29,4,2],"color":"boots"}]}
        """;

    public const string Chest = """
        {"name":"chest","size":[24,20],"palette":{"wood":"#8B5A2B","woodDark":"#5E3B17","metal":"#C9B037","shadow":"#3A2410"},"outline":"#1B1B1B","items":[
          {"rect":[2,8,20,10],"color":"wood"},
          {"rect":[2,2,20,7],"color":"woodDark","radius":3},
          {"rect":[2,8,20,2],"color":"shadow"},
          {"rect":[10,7,4,5],"color":"metal"},
          {"pixels":[[11,9],[12,9]],"color":"shadow"},
          {"line":[6,2,6,17],"color":"metal"},{"line":[17,2,17,17],"color":"metal"}]}
        """;

    public const string Walker = """
        {"name":"walker","frameSize":[24,32],"outline":"#1B1B1B","delay":0.15,
         "palette":{"skin":"#F1C27D","hair":"#5A3A1E","coat":"#3B5BA5","coatDark":"#2A4078","pants":"#3A3A4A","boots":"#222222"},
         "base":[
          {"rect":[7,12,10,9],"color":"coat"},{"rect":[7,18,10,3],"color":"coatDark"},
          {"ellipse":[7,2,10,9],"color":"skin"}],
         "rows":[
          {"name":"S","base":[{"rect":[6,1,12,4],"color":"hair","radius":2},{"pixels":[[9,7],[14,7]],"color":"#1B1B1B"},{"rect":[3,12,3,8],"color":"coat"},{"rect":[18,12,3,8],"color":"coat"}],
           "frames":[
            {"name":"stand","delay":0.5,"items":[{"rect":[8,21,3,8],"color":"pants"},{"rect":[13,21,3,8],"color":"pants"},{"rect":[8,29,3,2],"color":"boots"},{"rect":[13,29,3,2],"color":"boots"}]},
            {"name":"step1","items":[{"rect":[8,21,3,6],"color":"pants"},{"rect":[13,21,3,8],"color":"pants"},{"rect":[8,27,3,2],"color":"boots"},{"rect":[13,29,3,2],"color":"boots"}]},
            {"name":"step2","items":[{"rect":[8,21,3,8],"color":"pants"},{"rect":[13,21,3,6],"color":"pants"},{"rect":[8,29,3,2],"color":"boots"},{"rect":[13,27,3,2],"color":"boots"}]}]},
          {"name":"N","base":[{"rect":[6,1,12,9],"color":"hair","radius":3},{"rect":[3,12,3,8],"color":"coat"},{"rect":[18,12,3,8],"color":"coat"}],
           "frames":[
            {"name":"stand","items":[{"rect":[8,21,3,8],"color":"pants"},{"rect":[13,21,3,8],"color":"pants"},{"rect":[8,29,3,2],"color":"boots"},{"rect":[13,29,3,2],"color":"boots"}]},
            {"name":"step1","items":[{"rect":[8,21,3,6],"color":"pants"},{"rect":[13,21,3,8],"color":"pants"},{"rect":[8,27,3,2],"color":"boots"},{"rect":[13,29,3,2],"color":"boots"}]},
            {"name":"step2","items":[{"rect":[8,21,3,8],"color":"pants"},{"rect":[13,21,3,6],"color":"pants"},{"rect":[8,29,3,2],"color":"boots"},{"rect":[13,27,3,2],"color":"boots"}]}]},
          {"name":"E","base":[{"rect":[7,1,11,4],"color":"hair","radius":2},{"pixels":[[15,7]],"color":"#1B1B1B"},{"rect":[10,12,4,8],"color":"coat"}],
           "frames":[
            {"name":"stand","items":[{"rect":[9,21,6,8],"color":"pants"},{"rect":[9,29,7,2],"color":"boots"}]},
            {"name":"step1","items":[{"rect":[7,21,4,8],"color":"pants"},{"rect":[13,21,4,6],"color":"pants"},{"rect":[6,29,5,2],"color":"boots"},{"rect":[13,27,5,2],"color":"boots"}]},
            {"name":"step2","items":[{"rect":[13,21,4,8],"color":"pants"},{"rect":[7,21,4,6],"color":"pants"},{"rect":[13,29,5,2],"color":"boots"},{"rect":[6,27,5,2],"color":"boots"}]}]},
          {"name":"W","mirrorOf":"E"}]}
        """;
}
