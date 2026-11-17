# Tool review: what exists and what users still miss

Goal: **not a super advanced editor, but a convenient one with the tools people really need.** The comparison is based on the publicly known features of Aseprite, LibreSprite, Piskel, Pixelorama and GraphicsGale (their sources were not used).
Selection principle: take what people use every day, skip what only rare professionals need or what breaks our constraints (binary transparency, pixels only).

Русская версия: [ru/TOOLS_REVIEW.md](ru/TOOLS_REVIEW.md).

## 1. What exists

| Area | Available |
|---|---|
| Drawing | pencil, eraser (size up to 16, symmetry on two axes), fill with tolerance and "whole layer", line, rectangle, ellipse (thickness, fill), gradient (dither, steps, smooth), lighter/darker, text (own 5×7 font, Cyrillic), brush footprint highlight |
| Selection | frame, magic wand (tolerance, connected or whole layer, add), lasso, polygon selection; move, copy, cut, rotate (handles, presets, any angle), flip, ×2 and ÷2, stretch by handles; crop canvas to selection; selection coordinate grid |
| Layers | add, duplicate, reorder, merge down, visibility, lock, thumbnails; a new layer for each shape, paste and text |
| Color | RGB and hex, primary and secondary color, palettes (built-in and user, library, import and export), used-colors list with replacement |
| Canvas | canvas size and image size (downscale methods), crop to content, import with downscale, paste from the clipboard, Space+drag pan, fit to window |
| Automation | templates (built-in, user, constructor), 48 operations, 11 checks, batch, result preview, scenes from primitives, CLI and MCP, error log for AI |
| Animation | sheet with zones, RSI, per-row preview, per-frame delays, export for Unity, Godot, GIF |

## 2. What is missing (by day-to-day usefulness)

### Should do (high usefulness, reasonable cost)

| # | Feature | Why | Cost | Status |
|---|---|---|---|---|
| 1 | Pixel-perfect freehand drawing | quick strokes leave L-shaped corner pixels | small | tried and removed at the user's request |
| 2 | Fill with tolerance and "whole layer" | the wand has it, the fill did not | small | **done** |
| 3 | Tile mode (seamless textures) | main Aseprite feature for tiles | medium | open |
| 4 | Onion skin | frames are hard to draw without it | medium | open |
| 5 | Shade ramp from the primary color | quick 4-5 shades for shadows and highlights | small | open |
| 6 | HSV color picker | RGB sliders are poor for choosing a hue | medium | open |
| 7 | Invert, grow and shrink selection | "everything except" after the wand | small | open |
| 8 | Space to pan and Fit | familiar controls | small | **done** (Space, `F`) |
| 9 | Reference picture under the canvas | draw from a sample without pasting it | medium | open |
| 10 | Undo for layer name, visibility and lock | not undoable today | small | open |

### Consider (medium usefulness)

| Feature | Comment |
|---|---|
| Color-replace brush mode | paint only pixels of the chosen color |
| Layer opacity and blend modes (multiply) | ideal for shadows but breaks binary transparency; only as a preview, decision postponed |
| Copy a frame between RSI states and cells | one-click copy of a frame (sheet cell) to the next |
| Hotkeys in one place and hover hints | hints exist, a hotkey window does not |

### Do not do (low usefulness or against the principles)

| Feature | Why not |
|---|---|
| Soft brushes, brush opacity, blur, gradient maps in the editor | against binary transparency and pixel crispness (the `tint` operation exists for batch use) |
| Full timeline animation with linked cells | the zone sheet is simpler; exports for engines already exist |
| 2D bones and meshes | rarely used in pixel art, breaks the grid |
| Indexed color mode | complex migration, most users do not need it; the palette library covers the basics |
| Reading and writing `.aseprite`, `.psd` | expensive format support; PNG, sheets, GIF and RSI are enough |
| Light maps and dynamic lighting | easier to draw by hand; "light and shadow on edges" exists |

## 3. Suggested order

1. Quick wins: shade ramp (5), invert selection (7), undo for layer properties (10).
2. Color work: HSV picker (6).
3. Frames and tiles: onion skin (4), tile mode (3).
4. Working from a sample: reference picture (9).

Each small-cost item is 1-3 days with tests under our scheme (logic in `ViewModels` or `Core`, the window only shows it).

## 4. What to ask users

- Do they need a layer with transparency and multiply, or are "lighter/darker" and "light and shadow on edges" enough?
- Is the zone sheet enough for animation, or is a real frame strip under the canvas needed?
- Which 5-7 hotkeys do they want "like in Aseprite"?
