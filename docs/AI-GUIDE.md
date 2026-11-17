# Pixel Editor: guide for AI agents (how to use it without reading the project)

This file is for an agent that needs to **process, draw or export sprites**. Do not read the whole project: it wastes tokens and risks breaking something.
If you are working on the project itself (code, tests, UI), read `AGENTS.md` in the repository root instead of this file. This file is self-contained: it needs no other project files.

## Core rules

1. Do not open the sources (`src/`, `tests/`) and do not scan the project folder. Everything you need is behind three entry points: the **MCP server**, the **`pixed` command line**, and the **DLLs**.
2. Learn what the program can do from the program itself, not from code: `pixed help`, `pixed ops`, `pixed checks`, `pixed templates`, `pixed palettes` (in MCP: `list_operations`, `list_checks`, `list_templates`, `list_palettes`). Never invent operation or parameter names.
3. Cheapest order of attack: a ready-made template, then a scene built from primitives, then your own template made of operations. Pixel-by-pixel work comes last.
4. Look at images rarely and small: `view_image` with `max_side` 128 or 256. Checks (`checks`) are cheaper than eyes.
5. Write only to paths the user named. Download images only with the user's permission and only under a free license.

## Which entry point to choose

| Entry point | When | How |
|---|---|---|
| **MCP** `pixed-mcp.exe` | the agent is already connected and you want the image in the reply | `claude mcp add pixel-editor -- <folder>\pixed-mcp.exe` |
| **CLI** `pixed.exe` | you have a shell, scripts, batch work | `<folder>\pixed.exe` (all replies are JSON on stdout; errors are JSON `{"Error": ...}` on stderr) |
| **DLL** | you need your own script or a .NET program | reference `PixelEditor.Core.dll` and `PixelEditor.Imaging.dll` from the same folder |

`<folder>` is the folder where the tools are built. The user gives it to you (for example `C:\work\pixed-tools`). If the exe files are missing, build them from the project with
`dotnet publish src/PixelEditor.Cli -c Release -o <folder>` and `dotnet publish src/PixelEditor.Mcp -c Release -o <folder>` (needs .NET 10). Do not guess paths inside `src\...\bin\...`: they depend on the build configuration.

Messages, catalogs and built-in template names are in English by default. For Russian use `--lang ru` (CLI) or the environment variable `PIXED_LANG=ru` (CLI and MCP). In the DLL the language is `CoreText.Language`.

## CLI cheat sheet

Exit codes: **0** success, **1** error (a step failed, no file written), **2** the steps ran but the template's checks failed (the image is written so you can look at it).

```bash
pixed ops | checks | plugins | palettes | templates      # catalogs (JSON on one line, --pretty: indented)
pixed ops --names | checks --names                       # names only (~500 chars)
pixed ops <name> | checks <name> | palettes <name>       # one entry in full (palette: with colors; --colors: colors for all)
pixed info <img> [--top N | --all-colors]                # size, color count, transparency and the 8 most frequent colors (~500 chars)
pixed apply <operation> <in> <out.png> key=value ...     # a single operation
pixed run <template.json | template name | JSON> <in> <out.png>
pixed check <template> <img>                             # checks only
pixed batch <template> <dir_in> <dir_out> [--recurse] [--pattern *.png;*.jpg]
pixed log [--tail N] [--level info|warning|error] [--stacks] [--files]   # the error log (English): what failed, when, with stack traces on request
pixed chat-prompt [--image file] [--compact] [--out file]   # prompt for a plain AI chat (for the human user; agents do not need it)
pixed pipeline-check <answer.txt|JSON>                   # extract and validate a pipeline from a chat answer
pixed validate <template>                                # check a template without running it
pixed save-template <name> <pipeline.json> [description] # save to the library (available by name and in the editor)
pixed save-palette <name> <#RRGGBB> <#RRGGBB> ...        # save a palette to the library
pixed pixels <img> <x> <y> <w> <h>                       # exact colors of a region up to 16x16
pixed compare <img> <template1> [<template2> ...] [--preview out.png]   # variants side by side, nothing is saved
pixed palette-build <colors> <img1> <img2> ... [--fixed #hex]            # one palette for a series of frames
pixed style-extract <ref.png> [--max-colors N]           # style profile of a sprite (JSON)
pixed style-apply <profile.json> <in> <out> [--mask m.png | --cutout] [--preview p.png]
pixed draw <scene.json | JSON> <out.png>                 # placeholder from primitives
pixed draw-sheet <sheet.json | JSON> <out.png> [--meta f]  # a whole sprite sheet from primitives (directions x frames)
pixed chat-prompt [--kind image|sprite|sheet] ...        # prompt for a plain AI chat (for the human user)
pixed chat-prompt --kind sheet [--template walk-4 | --rows idle,attack --frames stand:0.5,swing:0.1] [--size 32x32] [--delay 0.2]   # the sheet prompt REQUIRES that layout (names, order, delays) and its example uses it; --template is a name or id (built-in or your own), --rows/--frames replace the template's, --delay replaces every delay
pixed fetch <http(s) link to an image> <file>            # download (image/*, up to 15 MB)
pixed slice <sheet> <w> <h> <dir> [--skip-empty]         # sheet into frames
pixed sheet <columns> <out.png> <frame1> <frame2> ...    # frames into a sheet
pixed gif <out.gif> <ms> <frame1> <frame2> ...           # or: pixed gif <out.gif> <ms> --sheet <sheet> <w> <h>
pixed anim-export <sheet.png> <w> <h> <dir> <name> [rows=idle,walk] [delay=0.1] [godotPath=res://x.png] [formats=aseprite,godot,gif,simple,unity] [noloop=attack,die]
pixed rsi info <folder.rsi>                              # Space Station 14 sprite: license, size, states (directions, frames, delays, PNG present and size consistent)
pixed rsi new <folder.rsi> <w> <h> [license=..] [copyright=..]   # folder + meta.json (license and copyright optional; never overwrites)
pixed rsi state <folder.rsi> <name> <sheet.png> [directions=1|4|8] [delay=0.1 | delays=0.1,0.2]   # write one state from a sheet
pixed rsi export <folder.rsi> <state> <out.png>          # a state as a rows x frames sheet (feed it to anim-export)
```

**Arguments from a file or stdin (use this on Windows).** Wherever a command takes JSON, a template, a profile or a color list, you can pass `@path` (read the argument from that file) or `-` (read it from stdin, UTF-8, a BOM is fine) instead of inline text:
`pixed run @pipeline.json in.png out.png`, `pixed draw @scene.json out.png`, `pixed draw-sheet - out.png < sheet.json`. Commands: `draw`, `draw-sheet`, `run`, `check`, `batch`, `validate`, `compare`, `save-template`, `style-apply`, `pipeline-check`, `save-palette` (colors: a JSON array or hex codes separated by spaces, commas or lines), and in `apply` a value `key=@file` (for example `points=@points.json`). A missing `@file` is an error with exit code 1; `-` can be used for only one argument per command. Plain paths, inline JSON and template names still work.

**File patterns.** `*` and `?` in the file-name part are expanded by the tool itself (Windows shells do not do it): `pixed sheet 4 out.png frames\*.png`, `pixed gif out.gif 100 frames\*.png`, `pixed palette-build 16 frames\*.png`. Natural order (`frame2` before `frame10`); no match is an error. Quote the pattern if your shell would expand it differently. Other commands take concrete files.

A file or text that contains a whole chat answer (explanations plus a ```json block) also works wherever a template is expected (`pixed run answer.txt in.png out.png`).
**Template parameters.** A template may declare `"params":[{"name":"size","type":"int","default":64,"min":8,"max":512,"label":"Size","hint":"..."}]` (types: int, number, bool, string, color, palette, choice with `"choices"`). Steps and checks refer to a parameter with a whole-value string (`"width":"$size"`, numbers may add `+2`, `-1`, `*2`, `/2`) and may carry `"when":"$flag"` / `"$mode==a|b"` (also `!=`, `>`, `<`, `>=`, `<=`, `!$flag`). Templates without `params` work as before. Override values: `pixed run <template> in out size=48 colors=16` (also `check`, `batch`), MCP `params` object in `run_pipeline`, `check_image`, `batch_process`; `pixed templates` / `list_templates` list each template's `Params` (name, type, default, range or choices). Unknown names and out-of-range values are errors (exit 1). Built-in templates (8): `Photo to pixel art` (size, colors, background keep|auto|complex, outline none|black|colored), `Downscale pixel art` (size, method), `Remove background` (subject auto|complex, simplify, enclosed auto|off|all, clean, trim), `Remove outline` (dark, passes), `Clean up sprite` (islands, holes, rareColors, speckle), `Snap to a palette` (palette, dither, perceptual), `Prepare for a game` (trim, fit none|multiple|cell, cell, anchor, maxColors), `Check finished sprite` (maxColors, maxSize). The 9 earlier built-in names and ids (Russian and English, e.g. "Photo to a 32×32 sprite", "Game Boy style") still resolve to these with the old values.

Templates can be referenced **by name** (for example `"Clean up sprite"`; matching is case-insensitive and the Russian names work too). A palette inside a template is referenced by name too (`"paletteName": "PICO-8"`).

## MCP tools (32; every CLI command above has an MCP twin, `read_log` included, and every operation, check and template is reachable from both)

`list_operations`, `list_checks`, `list_plugins`, `list_palettes`, `save_palette`, `list_templates`, `save_template`, `image_info`, `view_image`, `apply_operation`, `run_pipeline`, `check_image`,
`validate_pipeline`, `save_pipeline`, `batch_process`, `draw_scene`, `draw_sheet`, `fetch_image`, `slice_sheet`, `compose_sheet`, `make_gif`, `export_animation`, `compare_pipelines`, `inspect_pixels`, `build_shared_palette`,
`extract_style`, `apply_style`, `read_log`, `rsi_info`, `rsi_new`, `rsi_set_state`, `rsi_export_state`. Descriptions and parameters come from the server itself. The `@file`, `-` and glob forms are CLI-only: MCP has no quoting problem and stdin carries the protocol.

RSI (Space Station 14) tools mirror `pixed rsi`: `rsi_new` (folder, width, height, optional license and copyright), `rsi_set_state` (folder, name, sheet, optional directions, delay or delays), `rsi_info` (folder), `rsi_export_state` (folder, state, output; the sheet comes back as an image).

Use `compare_pipelines` with an input image and one to four pipeline names, paths or JSON strings. The preview shows the original, result and difference map for each row. The map compares equal-size images in canvas coordinates. Green marks added pixels, red marks removals and orange marks color or alpha changes. Run the chosen variant with `run_pipeline` to save it.

Pass `protect_mask` to either MCP tool, or `--protect-mask mask.png` to `pixed run`. Every nontransparent mask pixel restores the original pixel before validation. The mask, input and final result must have the same dimensions.

Use `inspect_pixels` for an exact region up to 16×16. Coordinates start at zero. Apply `patchPixels` through `apply_operation` or include it in a pipeline with arguments such as `{"edits":[[5,8,"#FFCC00"],[6,8,"transparent"]]}`.

Use `build_shared_palette` on all frames in a series. Pass accent colors in `fixed_colors`, then use the returned array as the `palette` argument of `applyPalette` for each frame.

### Match an existing sprite style

1. Call `extract_style` with a reference sprite, ideally on transparency. The returned JSON profile has the reference canvas size, palette and content box. A dark, mostly uniform edge can suggest a one-pixel outline. The bottom of the content box sets the baseline. Set `lightFrom` (e.g. `"NW"`) manually if needed.
2. Call `apply_style` with the profile and either an `input` image or a JSON `scene`. Set `reference` to include the reference in the preview. For a photo, use `cutout: true` for automatic removal, or pass a PNG `mask` of the original dimensions with opaque pixels marking the subject. In the editor, use **File → Export → Export selection as mask** to create a mask from a rectangle, wand or lasso selection. Omit `output` to inspect the result before saving.
3. To reapply a profile in the editor or CLI, add `{"op":"applySpriteStyle","profile":{...}}` to a pipeline. For rough photo colors, optionally place `{"op":"simplifyColorRegions","maxArea":4,"tolerance":32}` after quantization. It replaces small color islands with nearby colors without changing their alpha.

Scene groups draw their children in local coordinates and place them with `offset`. They can nest, be mirrored, and use `lightFrom` for highlights and shadows along their own edges. Rectangles also accept `radius`.

```json
{"size":[32,32],"items":[
  {"offset":[5,12],"lightFrom":"NW","group":[{"rect":[0,0,22,14],"radius":2,"color":"#996333"}]},
  {"offset":[5,8],"lightFrom":"N","group":[{"rect":[0,0,22,7],"radius":2,"color":"#BF8A4B"}]},
  {"offset":[13,14],"group":[{"rect":[0,0,6,6],"radius":1,"color":"#ECC573"}]}
]}
```

## DLL: what you can call

Reference `PixelEditor.Core.dll` (net10.0, no UI, no Windows dependency) and, if you need PNG and JPG files, `PixelEditor.Imaging.dll`. **Do not reference** `PixelEditor.ViewModels` or `PixelEditor.App`: they are the editor's interface.

`PixelEditor.Imaging.dll` reads images through WPF, so your project must be **`net10.0-windows` with `<UseWPF>true</UseWPF>`**, otherwise you get `FileNotFoundException: PresentationCore`. Minimal `.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType><TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF><ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="PixelEditor.Core"><HintPath><folder>\PixelEditor.Core.dll</HintPath></Reference>
    <Reference Include="PixelEditor.Imaging"><HintPath><folder>\PixelEditor.Imaging.dll</HintPath></Reference>
  </ItemGroup>
</Project>
```

If you only work with an in-memory `PixelImage` (no files), `PixelEditor.Core.dll` and a plain `net10.0` are enough.

Stable surface:

| What | Where | Purpose |
|---|---|---|
| `PixelEngine` | Imaging | ready-made API: `Inspect`, `Apply`, `Run`, `Check`, `Batch`, `Slice`, `Compose`, `MakeGif`, `Draw`, `Fetch`, `ExportAnimation`, `RsiInfo`, `RsiNew`, `RsiSetState`, `RsiExportState`, `LoadPipeline`, `Templates`, `Palettes`, `RenderPreview` |
| `ImageIO` | Imaging | `Load`, `SavePng`, `EncodePng` |
| `PixelImage`, `Pixel` | Core | `uint` raster 0xAARRGGBB, empty is `0`, `Clone()` |
| `Operations.Get(name).Run(img, args)`, `Operations.All` | Core.Ops | operations |
| `Pipeline.Parse(json).Run(img)` | Core.Ops | a template with steps and checks, result with a log |
| `Scene.Render(json)` | Core | scene from primitives |
| `PipelineLibrary`, `PaletteLibrary` | Core | libraries of templates and palettes (built-in and user) |
| `RsiMeta`, `AnimationExport` | Core | RSI meta.json, animation descriptions for Unity and Godot |

Minimal example:

```csharp
using PixelEditor.Core;
using PixelEditor.Core.Ops;
using PixelEditor.Imaging;

var report = PixelEngine.Run(PixelEngine.LoadPipeline("Clean up sprite"), "in.png", "out.png");
if (!report.StepsOk) throw new Exception("a step failed");

var img = ImageIO.Load("in.png");                                   // PixelImage
var small = Operations.Get("resize").Run(img, ArgsOf("""{"width":32,"method":"smart"}"""));
ImageIO.SavePng(small, "small.png");

static OpArgs ArgsOf(string json) => new(System.Text.Json.JsonDocument.Parse(json).RootElement, null);
```

Code rules: never modify the input (operations return a new image), transparency is binary after `alphaThreshold`, do not create a `Document` and do not touch `History` (those are for the editor).

## Operation groups (48 operations; `pixed ops --names` lists them)

- **Size and canvas:** `resize`, `resizeCanvas`, `pad`, `trim`, `crop`, `tile`, `shift`.
- **Orientation:** `flipH`, `flipV`, `rotate90`, `rotate`, `mirror` (copy one half onto the other, or a quarter onto all four).
- **Colors:** `quantize`, `applyPalette`, `mergeRareColors`, `replaceColor`, `swapColors`, `adjust`, `autoLevels`, `invert`, `grayscale`, `hueShift`, `tint` (gradient map: sepia, monochrome), `posterize`, `threshold`, `background`.
- **Cleanup:** `alphaThreshold`, `removeIslands`, `fillHoles`, `cleanEdges`, `smooth` (noise), `despeckle` (color speckle in photos after quantize: use `diagonal:false`; the alpha-based `removeIslands`/`fillHoles`/`cleanEdges` do nothing on a fully opaque picture), `simplifyColorRegions`, `pixelate` (mosaic).
- **Background and subject:** `removeBackground` (call it with no parameters first: the background is modeled from the border as several Lab clusters, the tolerance and the gradient/noise following are automatic, the antialiased rim is removed (`"defringe": false` turns that off), and background enclosed by the subject is erased when it is at least 3% of the picture (`"enclosed": "off"|"auto"|"all"`); an explicit `"tolerance"` is a perceptual distance, 0 means the exact color and also switches to the old no-following mode unless `"follow"` is set; exact-color pixel art stays pixel-exact), `cutout` (default mode `distance`: Lab distance to the border colors, steadier on photos than the barrier fill; it converges in one call, so running it again changes nothing; `"mode":"flood"` is the old fill; set `"threshold"` by hand when the subject looks like the background), `polygonMask`, `removeOutline`.
- **Photo preparation (Pillow style):** `median` (radius 1-2, removes JPEG noise and grain, keeps edges: run it first on a big photo), `unsharp` (after a downscale, brings thin lines back before quantizing), `autoLevels` with `"cutoff": 1` (stretches the range ignoring stray pixels), `edges` (Sobel lines, `onlyLines` makes a line drawing of a photo).
- **Shape and light:** `outline`, `grow`, `shrink`, `dropShadow`, `light` (pixel-art lighting of a flat sprite: the silhouette is a rounded volume, the light value is cut into `bands` crisp hue-shifted shades of each color; `{"from":"NW","bands":3}`; keeps a dark outline, never changes the silhouette, `paletteName` keeps the result inside a palette; a 1 px line stays flat), `edgeShade` (old name, same engine, `highlight`/`shadow` scale the steps).
- **Editing and style:** `patchPixels`, `applySpriteStyle`.

## Token-cheap scenarios

0. **A whole sprite sheet (a character in S, N, E, W with walk frames).** Do not draw every frame: call `draw-sheet` (MCP `draw_sheet`) with one JSON. Format (details in `Core/SheetScene.cs`): `{"frameSize":[24,32],"palette":{...},"outline":"#1B1B1B","delay":0.15,"base":[items drawn in EVERY frame],"rows":[{"name":"S","base":[items for this row],"frames":[{"name":"stand","delay":0.5,"items":[...]},{"name":"step1","items":[...],"offset":[0,-1]},{"name":"step2","copyOf":"stand"}]},{"name":"W","mirrorOf":"E"},{"name":"N","copyOf":"S"}]}`. A frame is base + row base + its items, so list in frames only what differs (the legs). Rows are directions or animations (RSI order S, N, E, W); `mirrorOf` flips another row left to right, `copyOf` copies it. The result is a PNG sheet plus `name.sheet.json` (frame size, rows, frame names, delays); then `anim-export` with the same rows, or open it in the editor (Animation menu). Look at the PNG with `view_image` and fix the frames.
1. **Placeholder (a little person, a chest, an animal).** Do not draw pixel by pixel. Describe a scene and call `draw`: `{"size":[24,32],"palette":{"skin":"#F1C27D"},"mirror":"x","outline":"#1B1B1B","items":[{"ellipse":[8,2,8,8],"color":"skin"},{"rect":[7,10,10,12],"color":"#3B5BA5"}]}`.
   Shapes: `rect`, `ellipse` (box `[x,y,w,h]`), `line`, `poly`, `pixels`, `fill`; each takes `color`, `fill:false`, `thickness`, `mirror`. They are drawn in order, about 10 lines per character (~200 tokens).
2. **Photo or picture to pixel art.** Use the template "Photo to pixel art" (`pixed run "Photo to pixel art" in.jpg out.png size=64 colors=16 background=auto outline=colored`). A plain or smooth background works right away. A textured background (grass, bricks): the built-in template cannot be edited, so build your own file `photo.json`:
   `{"name":"photo-poly","steps":[{"op":"polygonMask","points":[[x,y],...]},{"op":"alphaThreshold","value":128},{"op":"trim"},{"op":"adjust","contrast":12,"saturation":18},{"op":"resize","width":64,"height":64,"keepAspect":true,"method":"smart"},{"op":"alphaThreshold","value":128},{"op":"quantize","colors":16,"space":"lab"},{"op":"cleanEdges"},{"op":"outline","shade":true,"darken":0.6}],"checks":[{"check":"binaryAlpha"}]}`
   `points` is a rough outline of the subject in pixels of the source image (look at it with `view_image`, mind the preview scale), then `pixed run photo.json in.jpg out.png`.
3. **Clean up a sprite.** Templates "Clean up sprite", "Remove background" (for painted pictures add `simplify=8`), "Remove outline". The template's checks confirm the result (exit code 0).
4. **Snap to a palette.** `applyPalette` with `paletteName` or a list of colors; the `onlyPalette` check.
5. **Many files.** `batch` with a template: a report per file (`ok`, `checks_failed`, `error`), no images in the reply.
6. **Animation for Unity and Godot.** Frames into a sheet (`sheet`), then `anim-export` with row names (`rows=idle,walk`). For Godot you get a `.tres` (SpriteFrames), for Unity an `.aseprite.json` and a sheet to slice by grid. `noloop=attack,die` (MCP `no_loop`) marks rows that must not loop (Godot `loop: false`, `"loop": false` in the plain JSON). `formats=unity` (MCP `formats` item `unity`) also writes `PixelEditorSheetImporter.cs` (an editor script for Assets/Editor, not verified in Unity) and the plain JSON it reads.
6a. **Space Station 14 sprite (RSI).** `pixed rsi new hero.rsi 32 32 license=CC-BY-SA-3.0 "copyright=Made by X"`, then per state `pixed rsi state hero.rsi walk walk.png directions=4 delay=0.15` (the sheet: rows are directions S, N, E, W (SE, SW, NE, NW for 8), columns are frames; the frame count comes from the sheet width and the RSI frame size; `delays=0.1,0.2,0.1` for different frame delays, groups separated by `;` for different directions). A sheet from `draw-sheet` has exactly this layout. Check with `pixed rsi info hero.rsi` (every state `SizeConsistent: true`). Read a state back with `pixed rsi export hero.rsi walk walk-sheet.png`, which also repacks the wrapped PNGs of foreign RSIs. Only that state is touched in meta.json; license and copyright may stay empty.
7. **Your own template.** Build `{"name":..,"steps":[{"op":..}],"checks":[{"check":..}]}`, validate it (MCP `validate_pipeline`; in the CLI just `pixed run` on a test image), and on success save it to the library: MCP `save_template`; in the CLI put the JSON file at `%AppData%\PixelEditor\pipelines\name.json` (it is immediately visible in `pixed templates` and available by name; the same works for palettes: `%AppData%\PixelEditor\palettes`, format `{"name":..,"description":..,"colors":["#RRGGBB",..]}`).
   **A check is an acceptance criterion:** without one, exit code 0 proves nothing.

## Limits

- Transparency is binary (a pixel is either present or empty); semi-transparent pixels exist only in input files, before `alphaThreshold`.
- Scene: canvas up to 512×512 (otherwise an "out of range" error). The `resize` and `resizeCanvas` operations allow sizes up to 8192. A sheet for `anim-export` must divide into frames without a remainder.
- Patterns with `*` and `?` are expanded only by `sheet`, `gif` (frame list) and `palette-build`; other commands take **concrete files** (use `batch` for a folder).
- Replies: JSON on stdout, errors on stderr as `{"Error": "..."}` with exit code 1. In PowerShell stderr shows up as `NativeCommandError`: that is normal, go by the exit code and the `Error` field.
- Catalogs: `pixed ops` is ~10000 chars (expensive: use `ops --names`, then `ops <name>` ~400), `palettes` ~1500 (without colors), `templates` ~3500. `info` is short by default; a full color list of a photo is hundreds of thousands of chars, you do not need it (`--all-colors`). MCP: `image_info top=0`, `list_operations names_only=true`, `list_palettes with_colors=true`. Ask once and remember; pick an operation from the catalog by name instead of rereading the catalog.
- Operations that change the size (`resize`, `outline` with `autoPad`, `trim`): recompute coordinates after them. For `polygonMask`, coordinates are in pixels of the current image at that step.
- Automatic background cutout (`cutout`) cannot tell background from subject when they share the same tone and texture (grass and fur): use `polygonMask`.

## Safety and good manners

- Plugins (`--plugins <folder>`) are third-party code running with the application's rights: load only what you wrote or reviewed in this same task.
- `fetch`/`fetch_image` download a file: only with the user's permission, by a direct link to an image, from a free-license source (for example Openverse, `license=cc0`). Record the source and license next to the result.
- Do not change files outside the given folders and do not overwrite someone else's source images: write the result to a new path.

## If something is missing

Tell the user which operation or parameter was missing and what you did instead. Do not edit the project sources for a single task: new operations are added by the project's developer (in the repository that is `AGENTS.md`, section "How to add an operation or a check"; you may not have that file, and you do not need it).

## When something fails: read the log

Every failure is also written, in English with stack traces, to `%AppData%\PixelEditor\logs\pixed-YYYYMMDD.log` (the editor, `pixed` and `pixed-mcp` share it).
Do not guess from the one-line error: call `pixed log --tail 5` (MCP: `read_log`) to see the latest errors, `--level info` to see what ran before, `--stacks` for stack traces.
Quote the relevant entry when you report a bug to the user. Entries contain file paths, so do not paste them into public places.

## Quick diagnostics

| Symptom | Cause and what to do |
|---|---|
| exit code 2 from `run` | the steps ran but a check failed: open the output PNG and read `Checks` in the reply, fix the template |
| "Unknown operation" | the name was invented: `pixed ops --names` |
| "Palette … was not found in the library" | `pixed palettes` shows the names, case does not matter |
| "Sheet … does not divide into frames" | check the frame `w` and `h`: `pixed info <sheet>` gives the size |
| `fetch` answers 429 or 403 | the server blocks clients without contact details; take another source or ask the user to drop the file in |
| `view_image` is expensive | lower `max_side` or use `info` and `check` |


## Windows: JSON quotes and paths with spaces

- In **PowerShell and cmd**, JSON in a command-line argument gets mangled (inner quotes are lost: `{"a":1}` becomes `{a:1}`). So **never write inline JSON there: put it in a file and pass `@file`** (the recommended default on Windows):
  `pixed draw @scene.json out.png`, `pixed run @pipeline.json in.png out.png`, `pixed apply polygonMask in.png out.png points=@points.json`. The same goes for templates, scenes, sheets, profiles and chat answers. Or pipe it: `Get-Content scene.json -Raw | pixed draw - out.png`. A plain path (`pixed draw scene.json out.png`) still works. MCP does not have this problem.
- Globs are not expanded by PowerShell or cmd, `pixed` does it for the frame lists (`frames\*.png`, see above).
- Put paths with spaces in double quotes. For trial work it is convenient to build the tools into a folder without spaces: `dotnet publish src/PixelEditor.Cli -c Release -o C:\work\pixed-tools` (and the same for `src/PixelEditor.Mcp`); then `pixed.exe` is at `C:\work\pixed-tools\pixed.exe`.
