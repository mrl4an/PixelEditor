# Pixel Editor: user guide

For **beginners** (draw your first sprite in ten minutes) and for **confident users** (automatic processing, templates that keep a whole project in one style, AI tools, animation sheets).
Pick the part you need: every section stands on its own.

Русская версия: [ru/USER-GUIDE.md](ru/USER-GUIDE.md).

| I want to... | Read |
|---|---|
| draw something right now | [1. Start](#1-start) and [2. Beginner path](#2-beginner-path-your-first-sprite) |
| clean up or convert existing pictures automatically | [3. Automatic image tools](#3-automatic-image-tools) |
| make a whole set of assets look the same | [4. Templates: one style for everything](#4-templates-one-style-for-everything) |
| let an AI draw placeholders or turn photos into pixel art | [5. AI tools](#5-ai-tools-primitives-and-photos-from-the-internet) |
| make animations and game files | [6. Animation sheets](#6-animation-sheets) |

## 1. Start

1. Double-click **`PixelEditor.cmd`** in the main folder. The first run builds the program (needs the .NET 10 SDK, about a minute). The launcher **rebuilds by itself when the sources changed**, so you never start an old version; otherwise it starts immediately. `build.cmd` makes a full clean build (editor in `app\`, AI tools in `tools\`).
   `PixelEditor.cmd --rebuild` rebuilds after you update the sources, `PixelEditor.cmd my.pixproj` opens a project.
2. The editor opens with a **start screen**: one-click blank canvas sizes (16×16 to 64×64 and your last size), "Other size…", an animation sheet, an RSI state, open a project, import and convert a picture, and your recent files. Untick *Show at startup* to skip it; **File → Start screen…** brings it back. Closing it leaves a blank 64×64 canvas.
   **File → New…** picks another size (the last size is remembered). **Help → About** shows the version.
3. Language: the `RU`/`EN` button at the top right. Theme: the light/dark switch next to it.

A pixel here is either solid or empty (**binary transparency**): there are no soft edges, which is exactly what pixel art needs.

## 2. Beginner path: your first sprite

**Tools (left panel)** and their keys: Pencil `B`, Eraser `E`, Fill `G`, Eyedropper `I`, Shapes `L R O` (line, rectangle, ellipse), Selection `M W S P`, Move `V`, Gradient `D`, Lighter/darker `U`, Text `T`.
Left mouse button paints with the primary color, right button with the secondary one (empty by default, so it erases). Right-click the **Shapes** and **Selection** buttons to pick a variant.

1. **Pick colors.** Click a palette swatch (left = primary, right = secondary). `X` swaps them. The **Library…** button in the Palette panel opens ready sets (PICO-8, Game Boy and more).
   In the Color section the **HSV | RGB** chips switch between a hue strip with a saturation/value square and the RGB sliders (the choice is remembered). **Shades** adds two darker and two lighter shades of the primary color (with a pixel-art hue shift) to the palette. The **Recent** row above the palette holds the last 8 colors you painted with; click it like a swatch.
2. **Draw.** Pencil `B`; the brush size is in the left panel. Hold `Space` and drag to move the canvas; the wheel zooms around the cursor; `F` (or `Ctrl+0`) fits the canvas to the window, `Ctrl+1` shows it at 100% (View menu).
3. **Fill.** `G` and click. Increase *Tolerance* to include similar shades, tick *Whole layer* to repaint every similar pixel.
4. **Shapes.** Each shape goes to **its own layer** (Edit → "Shapes, paste and text on a new layer"), so nothing is lost and you can move shapes later.
5. **Select and transform.** Choose Selection, drag a frame. The selection tool stays active: drag inside to move; a press outside starts a new selection; the Background eraser (`K`) clears a clicked region with one click; drag the **squares** to stretch or shrink (Shift on a corner keeps proportions);
   drag **just outside a corner** to rotate (Shift snaps to 15°). The *Rotation* buttons give 90°, 180° or any angle. `Esc`, `Ctrl+D` or a click on empty space drops the selection.
   **Edit → Transform (selection or layer)**: rotate 90°/180° and the flips work on the selection, or on the **whole layer** when nothing is selected (one undo step; a non-square layer turns around its center and the edges are cropped, like *Rotate by angle…*). To scale a selection, drag its stretch squares.
6. **Layers (right panel).** A click selects a layer; eye = visible, lock = protected; the pencil on the row (or `F2`, or a double click on the name) renames it: `Enter` or leaving the box confirms, `Esc` cancels, an empty name is refused, and the rename can be undone; arrows reorder; the buttons add (`Ctrl+Shift+N`), duplicate (`Ctrl+J`), merge down and delete; *Merge visible* turns all visible unlocked layers into one (hidden layers stay).
7. **Undo** `Ctrl+Z`, redo `Ctrl+Y` or `Ctrl+Shift+Z`.
8. **Save.** `Ctrl+S` saves the project (`.pixproj`, keeps layers). `Ctrl+E` exports a PNG for use in a game (all exports are also under **File → Export**). Autosave runs every minute and offers to restore after a crash.

Useful: **Edit → Canvas size…** changes the paper size without scaling; **Edit → Image size…** scales the picture with a method (use *Most frequent color* for pixel art, *Average* for photos).

## 3. Automatic image tools

You do not have to draw everything by hand. Two groups of automatic tools work in the editor:

**a) Import with downscaling (`Ctrl+I`, or drag a picture into the window).** The dialog turns any picture into pixel art: pick the target size (original, 32, 64, 128 on the long side),
the downscale method, the number of colors, cleanup and an outline, and watch the preview. By default the picture is imported as is.

**b) The Operations menu** (applies to the current layer). With *Operations and templates only inside selection* ticked (Edit → Modes) the result is applied only inside the selection; the rest of the layer is restored:

| Group | What it does |
|---|---|
| **Other operation / tune parameters…** (first item) | any operation with parameter fields and a live Before/After preview: change a number and see the result at once; the list shows a short title with the technical name in brackets; the first thing to try when an automatic tool does too little or too much (for a gradient background set `follow` to 6-12) |
| Remove background (by edge color) / Cut out an object from a complex background | makes the background transparent in one click (several background colors, gradients, noise, soft shadows and the antialiased rim are handled automatically); use the first for plain and gradient backgrounds, the second for busy ones. The magic wand has the same color metric and a "Follow smooth transitions" option for gradient and noisy backgrounds |
| Cleanup: **Clean up sprite**, **remove small islands…** (you choose the size), remove stray debris (up to 3 px), close holes (up to 4 px), **remove color speckle…** | removes noise; *speckle* cleans what is left on a photo after reducing colors; *Clean up sprite* runs the built-in template of that name (single pixels, holes and rare shades) and shows the result before applying |
| Outline: outside, colored (dark shade of the neighbor), inside, **remove outline**, **remove dark outline** | frames the sprite or takes an existing outline off; *outside* and *inside* use the **primary color**; the colored outline looks more natural; *remove dark outline* also takes off outlines made of several dark shades |
| **Snap to project palette**, **Reduce number of colors…**, **Brightness and contrast…**, **Hue shift…**, grayscale, auto levels, **Replace color…** | snap every color to the palette of the project; the items with dots open the tuner (on `quantize`, `adjust`, `hueShift`, `replaceColor`). *Replace color…* starts with *from* = the color selected in the *Used colors* list (otherwise the secondary color) and *to* = the primary color |
| Shape and light: grow, shrink, mirror (left half to the right), light and shadow on edges | changes the silhouette; the last item adds a highlight toward the light and a shadow opposite |

The other operations (invert, sepia, posterize, mosaic, merge rare colors, smooth edges and more) are not in the menu but stay available in **Other operation / tune parameters…**, in templates and in the AI tools.

Tips: for a photo with noise use **Remove color speckle…** (a few times), then an outline. The automatic cutout struggles when the subject and the background have the same tone and texture;
in that case use the lasso or polygon selection and delete the rest by hand.
**Templates → Reference: operations, checks, plugins…** lists every operation with its parameters.

## 4. Templates: one style for everything

A **template** (also called a *pipeline*) is a recipe: steps like *downscale → reduce to 16 colors → remove speckle → outline*, plus **checks** that prove the result is good (at most 16 colors, no stray pixels, binary alpha...).
Run the same recipe on every asset and the whole project looks the same.

- **Run:** *Templates → Standard templates* (12 built-in) or *My templates*, or *Run template from file…*. A result preview shows before and after, the steps and the checks; nothing changes until you press the button.
  *Templates: apply to all layers* (Edit → Modes) runs a template on every layer (single operations always change the current layer); *Operations and templates only inside selection* (also Edit → Modes) limits it to the selection.
- **Run:** *Templates → Standard templates* (8 built-in, each with parameters; the preview dialog uses the defaults for now, set values on the command line `pixed run <template> in out size=48 colors=16` or in MCP `run_pipeline` through `params`) or *My templates*, or *Run template from file…*. A result preview shows before and after, the steps and the checks; nothing changes until you press the button.
  *Apply to all layers* runs it on every layer; *Operations and templates only inside selection* limits it to the selection.
- **Build your own:** *Templates → Create template…* opens the constructor: name, description, steps and checks from the catalog with parameter fields, a ready JSON and a **trial run** on the current layer without changing the document.
  *Template manager…* copies a built-in template, edits or deletes your own, imports and exports `.json`.
- **A whole folder:** *Templates → Batch process folder…* applies one template to every picture in a folder and reports each file (ok, checks failed, error).
- **Palette = style.** Keep one palette for the project: the **Library…** button in the Palette panel, then *Apply palette* in a template (`applyPalette` with `"paletteName": "PICO-8"` and the check `onlyPalette`).
  The **⋯** button next to *Library…* has *Palette from image* (builds a palette from a reference picture), sorting by brightness or hue, removing unused colors and saving to the library.
- **Match an existing sprite's style** (through the AI tools or the command line): `pixed style-extract ref.png` makes a profile (canvas, palette, outline, light) and `pixed style-apply profile.json in.png out.png` brings a new picture to that style.
  `pixed compare in.png "Template A" "Template B"` shows variants side by side so you can pick the best.
- **Share a style:** a template is a small JSON file in `%AppData%\PixelEditor\pipelines`; copy it to a teammate or keep it in your repository.

A good recipe for photos: `trim → resize (smart) → alphaThreshold → quantize (16 colors, lab) → despeckle → cleanEdges → outline` with checks `maxColors`, `binaryAlpha`. This is the built-in "Photo to pixel art" (parameters size, colors, background, outline).

## 5. AI tools: primitives and photos from the internet

An AI assistant can use the same operations and templates. You do not need to know how: you describe the result and give the tools folder. Setup and ready-made prompts: [AI-TOOLS-HOWTO.md](AI-TOOLS-HOWTO.md).

**Placeholders from primitives.** Ask for "a 24×32 guard with a helmet and a spear". The agent does not draw pixel by pixel: it writes a short **scene** (rectangles, ellipses, lines, polygons, mirror for symmetry, an outline) and the program renders it, about 10 JSON lines per character.
Open the PNG in the editor to finish it by hand (*Ctrl+I*). Good for quick prototypes of characters, chests, tiles and animals; the examples are in `examples/animals`.

**Photos from the internet.** Ask the agent to find a picture with a **free license** (CC0 or public domain), download it with your permission, and turn it into pixel art with the template "Photo to pixel art" (`background=auto` or `complex` removes the background) or its own.
Rules: it downloads only with your permission, only direct image links, up to 15 MB; it records the source and license next to the result. If the automatic cutout fails (subject and background alike), the agent traces the subject with a polygon.

**Without any agent: a plain AI chat.** Choose *Templates → From an AI chat…*, copy the prompt, paste it into any chat with your photo and request, then paste the answer back into the same window.
The editor extracts the JSON, lets you edit it, shows a live before/after preview (you can keep several variants and compare them) and applies it only when you press Apply. Details: [AI-TOOLS-HOWTO.md](AI-TOOLS-HOWTO.md#4b-no-agent-use-any-ordinary-ai-chat).

**What else the agent can do:** process a whole folder, check results with the same checks, save a new template (it appears in **Templates → My templates**), assemble sheets and export animation files.
If something fails the agent reads the error log itself (`pixed log`); you can open it from **Help → Open error log folder**.

Always give the agent a copy of your files and review the result before replacing anything.

## 6. Animation sheets

A **sheet** is one picture divided into a grid of frames; rows are animations or directions, columns are frames. The editor draws the zones over the canvas so you know where each frame goes.

**Make a sheet.** *Animation → New animation sheet…*: pick a **template** (one frame; idle in 4 directions; walk in 4 or 8 directions with "idle, step 1, step 2"; walk cycle; idle with breathing; attack; door; platformer; effect) or enter your own rows and frames with names and delays
(for example rows "idle, run, jump", frames "frame 1, 0.1"). *Save as template…* keeps your layout for next time. The canvas is sized for you.
The **delay** field (seconds, comma or dot) sets the delay of ALL frames, also after you chose a template, and rewrites the frame list; a line edited by hand wins for its own frame, and the frame count resizes the list. A delay you typed stays when you pick another template. If the frames have different delays the field is empty and says so: what the dialog shows is what is created. The last template and delay are offered again next time.
To have an AI chat draw a sheet of exactly this layout, use *Pipelines → From an AI chat… → Draw a sprite sheet*: choose the layout there (or work on an open sheet: its layout is required automatically).

**Draw.** Draw each frame inside its zone (labels like "S · step 1"). `Ctrl+arrows` shifts a layer; *Sheet frame grid…* changes the cell size.
Frames can also be layers: *Import sheet (cells to layers)…* splits a sheet into layers and *Export layers as sheet…* assembles them back; *Export slices to folder…* saves every cell as a separate PNG.

**Preview.** *Animation → Animation preview and GIF…* plays each row with per-frame delays; save a GIF from there.

**Export for games.** *Animation → Export for Unity, Godot, GIF…* writes the sheet PNG plus:
- **Unity:** an Aseprite-style `.json` with frames, durations and one tag per row; import the sheet with Unity's Sprite Editor (Grid By Cell Size) or an Aseprite importer.
- **Godot 4:** a `SpriteFrames` `.tres` with one animation per row (set the project path of the PNG in the dialog).
- **Unity editor script (optional):** `PixelEditorSheetImporter.cs` plus the plain `.json`. Drop the script into `Assets/Editor` once, select the `.json` and choose *Assets → Pixel Editor → Import sheet from JSON*: the PNG is sliced by the grid (sprites `<animation>_<frame>`, Point filter, no compression) and one AnimationClip per row is created with the delays and loop flag. The script has not been verified inside Unity; it targets 2021.3 LTS and newer.
- **GIF** per row and a simple JSON description.
- **Loop:** the dialog lists the animations (rows) with a "loop" checkbox each (Godot `loop`, `"loop"` in the plain JSON, Unity clips).
The dialog remembers its settings in the document (and in `.pixproj`) and is pre-filled from them; *Animation → Export again* (`Ctrl+Shift+E`) repeats the last export without the dialog and reports in the status bar.
The dialog contains a short how-to for each engine. Without the editor: `pixed anim-export sheet.png 32 32 out_dir hero rows=idle,walk delay=0.1 formats=aseprite,godot,unity noloop=attack,die`.

**RSI.** RSI is the sprite format of Space Station 14: a `.rsi` folder with `meta.json` and one PNG per state. The *RSI* submenu of **Animation**: *New RSI…* creates a folder with `meta.json` (size; license and copyright are optional), *New state…* makes a sheet for a state with 1, 4 or 8 directions and frame delays,
*Open state…* loads an existing `.rsi` state, *Save state to RSI* writes the PNG and updates only that state in `meta.json` (other states and unknown fields such as `flags` or custom keys are kept as they are). *RSI metadata…* edits the license and size; *Open RSI folder* shows the files. The last three items are greyed out until you use *New RSI…* or *Open state…* (hover them to see why).

Tips: name your frames (the templates do it); keep one frame size per sheet; draw the idle frame first and copy it between cells with copy and paste; the preview is the fastest way to catch a jerky loop.

## 7. Handy facts

| Need | How |
|---|---|
| Move the canvas | `Space`+drag or middle mouse button; wheel zooms; `Shift`+wheel scrolls sideways |
| Pick a color from the picture | `I` or `Alt`+click (the option "only from the layer" is in the tool panel) |
| Change the palette | the **⋯** button in the Palette panel header: sort by brightness or hue, remove unused colors, palette from the picture, save to the library. Loading and saving `.hex` / `.gpl` files and the ready sets are in the **Library…** button of the Palette panel |
| Replace a color everywhere | the used-colors panel (right): select a color, then "Recolor to primary" |
| Where my data is | `%AppData%\PixelEditor`: `settings.json`, palettes, templates (`pipelines` folder), locales, logs, `autosave.pixproj` |
| Something went wrong | **Help → Open error log folder**; the log is English and includes details you can attach to a bug report |

License: GPL-3.0-or-later, copyright (C) 2026 mrl4an.
