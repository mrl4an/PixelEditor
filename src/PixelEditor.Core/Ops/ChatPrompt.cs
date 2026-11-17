// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
using System.Text;

namespace PixelEditor.Core.Ops;

/// <summary>Information about the picture that goes into the chat prompt: the size is needed by the AI for polygon coordinates and sizes.</summary>
public sealed record ChatPromptImage(int Width, int Height, int Colors, bool HasTransparency);

/// <summary>
/// A ready prompt for an ordinary AI chat (no agents or tools): explains the template format, lists the operations, checks and palettes
/// from the LIVE catalog (always in English) and asks for a single JSON answer. The user copies it into a chat, attaches a photo and a request,
/// and pastes the answer into the editor ("Pipelines → Pipeline from an AI chat").
/// </summary>
public static class ChatPrompt
{
    /// <summary>The prompt for what the user wants: process the current picture (a pipeline), draw a sprite from primitives, or draw a whole sheet.</summary>
    /// <param name="sheetLayout">For <see cref="ChatKind.Sheet"/>: the layout the answer must have (null: the AI chooses).</param>
    public static string Build(ChatKind kind, ChatPromptImage? image = null, bool compact = false, SheetLayoutSpec? sheetLayout = null) => kind switch
    {
        ChatKind.Sprite => ChatScenePrompt.Sprite(),
        ChatKind.Sheet => ChatScenePrompt.Sheet(sheetLayout),
        _ => Build(image, compact),
    };

    public static string Build(ChatPromptImage? image = null, bool compact = false)
    {
        using var _ = CoreText.Scope("en");
        var sb = new StringBuilder();
        void Line(string s = "") => sb.AppendLine(s);

        Line("You are a pixel-art processing assistant. I will send a picture and a request. Reply with ONE processing pipeline as JSON for the editor \"Pixel Editor\".");
        Line();
        Line("## Reply format");
        Line("- Reply with a single JSON object inside one ```json code block. A short plain-text explanation (1-3 sentences) may come BEFORE the block, nothing after it.");
        Line("- No comments inside the JSON, no trailing commas, straight double quotes only.");
        Line("- Shape: {\"name\": \"...\", \"description\": \"...\", \"steps\": [ {\"op\": \"<operation>\", <parameters>}, ... ], \"checks\": [ {\"check\": \"<check>\", <parameters>}, ... ]}");
        Line("- Steps run top to bottom, each on the result of the previous one. Parameters are plain JSON values; colors are \"#RRGGBB\".");
        Line("- Use ONLY the operations, checks and parameters listed below. Never invent names.");
        Line("- Optional template parameters (only when the user would want to tune something, e.g. the size): add \"params\": [{\"name\":\"size\",\"type\":\"int\",\"default\":64,\"min\":8,\"max\":256,\"label\":\"Size\"}] (types: int, number, bool, string, color, palette, choice with \"choices\":[...]) and refer to a parameter with a whole-value string such as \"width\": \"$size\" (numbers may add +2, -1, *2, /2). A step or check may carry \"when\": \"$flag\" or \"$mode==a|b\" to run only for some values. Every parameter needs a default; without params everything works as before.");
        Line();
        Line("## Rules that make results good");
        Line("- Transparency is binary: a pixel is either opaque or empty. Add {\"op\":\"alphaThreshold\"} after any step that can create semi-transparent pixels (resize, rotate, cutout, polygonMask).");
        Line("- For pixel art from a photo use this order: median (radius 1-2, only for a big noisy photo) -> remove the background (removeBackground, which on its defaults handles noise, gradients, soft shadows and the antialiased rim; or cutout; or polygonMask with a hand-traced outline) -> alphaThreshold -> trim -> adjust (contrast, saturation) -> resize (method \"smart\" or \"mode\", NOT \"box\") -> unsharp (radius 1, amount 0.8) -> alphaThreshold -> quantize (colors 8-16, space \"lab\") -> despeckle (diagonal false) -> cleanEdges -> outline.");
        Line("- Coordinates (polygonMask points, keepRect, crop, patchPixels) are in pixels of the image AT THAT STEP; recompute them after any step that changes the size. Look at the picture carefully and trace the subject with 8-16 points (x to the right, y down, origin top-left).");
        Line("- Automatic cutout fails when the subject and the background have similar color and texture: use polygonMask with a hand-traced outline instead.");
        Line("- Always finish with 2-4 checks that prove the result is good (for example maxColors, binaryAlpha, size, noIslands).");
        Line("- Keep it short: usually 5-12 steps. Prefer simple, robust operations.");
        Line();

        if (image != null)
        {
            Line("## My image");
            Line($"- Size: {image.Width}x{image.Height} px, {image.Colors} distinct colors, {(image.HasTransparency ? "has transparent pixels" : "fully opaque")}.");
            Line("- (If I send a different picture in the chat, use that picture's real size.)");
            Line();
        }

        Line("## Operations");
        foreach (var o in Operations.All.OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase))
        {
            var ps = o.Parameters.Length == 0 ? "" : string.Join(", ", o.Parameters);
            Line($"- {o.Name}({ps})" + (compact ? "" : ": " + o.Description));
        }
        Line();
        Line("## Checks (go into \"checks\"; add \"required\": false to make a check only a warning)");
        foreach (var c in Checks.All.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
        {
            var ps = c.Parameters.Length == 0 ? "" : string.Join(", ", c.Parameters);
            Line($"- {c.Name}({ps})" + (compact ? "" : ": " + c.Description));
        }
        Line();
        var palettes = PaletteLibrary.Default.All().Where(p => p.IsSystem).Select(p => p.Name).ToList();
        Line("## Palettes");
        Line("applyPalette and the onlyPalette check accept {\"paletteName\": \"<name>\"} (case-insensitive) or {\"palette\": [\"#RRGGBB\", ...]}. Built-in names: " + string.Join(", ", palettes) + ".");
        Line();
        Line("## Examples");
        Line("Photo to 64 px pixel art with a hand-traced subject (image 480x320):");
        Line("```json");
        Line("{\"name\":\"photo-polygon-64\",\"description\":\"Trace the subject, shrink to 64 px, 16 colors, outline\",\"steps\":[");
        Line("  {\"op\":\"polygonMask\",\"points\":[[120,40],[360,40],[380,300],[100,300]]},");
        Line("  {\"op\":\"alphaThreshold\",\"value\":128},{\"op\":\"trim\"},");
        Line("  {\"op\":\"adjust\",\"contrast\":12,\"saturation\":18},");
        Line("  {\"op\":\"resize\",\"width\":64,\"height\":64,\"keepAspect\":true,\"method\":\"smart\"},");
        Line("  {\"op\":\"alphaThreshold\",\"value\":128},");
        Line("  {\"op\":\"quantize\",\"colors\":16,\"space\":\"lab\"},");
        Line("  {\"op\":\"despeckle\",\"maxArea\":3,\"diagonal\":false},");
        Line("  {\"op\":\"cleanEdges\"},{\"op\":\"outline\",\"shade\":true,\"darken\":0.6}],");
        Line(" \"checks\":[{\"check\":\"maxColors\",\"value\":24},{\"check\":\"binaryAlpha\"},{\"check\":\"size\",\"maxWidth\":70,\"maxHeight\":70}]}");
        Line("```");
        Line("Clean up an existing sprite and snap it to a palette:");
        Line("```json");
        Line("{\"name\":\"clean-pico8\",\"steps\":[{\"op\":\"removeIslands\",\"maxSize\":2},{\"op\":\"fillHoles\",\"maxSize\":4},{\"op\":\"applyPalette\",\"paletteName\":\"PICO-8\"}],");
        Line(" \"checks\":[{\"check\":\"onlyPalette\",\"paletteName\":\"PICO-8\"},{\"check\":\"noIslands\",\"maxSize\":2}]}");
        Line("```");
        Line();
        Line("Now I will send my picture and what I want. If my request is unclear, make the most sensible choice and say it in the short explanation before the block.");
        return sb.ToString().TrimEnd() + Environment.NewLine;
    }
}
