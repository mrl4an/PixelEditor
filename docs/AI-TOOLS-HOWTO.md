# Working with the AI tools: a guide for people

This page is for **you**, the person who wants an AI assistant (Claude Code, Claude Desktop or any MCP client) to process, draw or export sprites with Pixel Editor.
The agent itself should read [AI-GUIDE.md](AI-GUIDE.md) (English only); this page tells you how to set things up and what to ask.

Русская версия: [ru/AI-TOOLS-HOWTO.md](ru/AI-TOOLS-HOWTO.md).

## 1. What the agent can do

| Capability | How it is done |
|---|---|
| Clean up sprites, remove backgrounds and outlines, reduce colors, snap to a palette | built-in templates and 48 operations |
| Turn a photo into pixel art | the template "Picture from the internet to 64 px pixel art" or your own template |
| Draw placeholders (a character, a chest, an animal) | `draw` with a short JSON scene, about 200 tokens instead of pixel-by-pixel work |
| Process a whole folder | `batch` with one template and a report per file |
| Build sprite sheets, GIFs, Unity and Godot animation files | `sheet`, `gif`, `anim-export` |
| Match the style of an existing sprite | `style-extract` and `style-apply` |
| Prove that a result is good | **checks** (color limit, binary alpha, no debris...) |

There are three entry points. Pick one:

| Entry point | Best for |
|---|---|
| **MCP server** `pixed-mcp.exe` | an assistant that supports MCP: it gets a picture back with every reply |
| **Command line** `pixed.exe` | any agent that can run a shell; scripts and batch jobs |
| **DLL** | your own .NET program |

## 2. Install

1. Get the tools folder. From a release: unpack the zip. From source (needs the .NET 10 SDK on Windows):

   ```powershell
   dotnet publish src/PixelEditor.Cli -c Release -o C:\tools\pixed
   dotnet publish src/PixelEditor.Mcp -c Release -o C:\tools\pixed
   ```

   Use a folder **without spaces**: it saves a lot of quoting trouble. Copy `docs\AI-GUIDE.md` into the same folder; the agent reads it from there.
2. Check that everything is reachable (optional, about a minute):

   ```powershell
   python scripts\audit_ai_access.py C:\tools\pixed
   ```

   The last line must say "all reachable".
3. `C:\tools\pixed\pixed.exe --version` prints the version and license.

## 3. Connect an assistant

**Claude Code (MCP):**

```powershell
claude mcp add pixel-editor -- "C:\tools\pixed\pixed-mcp.exe"
```

Restart the session and run `/mcp`: `pixel-editor` must be listed as connected with 32 tools.

**Claude Desktop:** add this to `claude_desktop_config.json` and restart the app:

```json
{ "mcpServers": { "pixel-editor": { "command": "C:\\tools\\pixed\\pixed-mcp.exe" } } }
```

**No MCP (command line only):** nothing to connect. Tell the agent where the folder is and to read `AI-GUIDE.md` there (see the prompts below).

Useful environment variables (set them before starting the assistant): `PIXED_LANG=ru` for Russian messages, `PIXED_DATA_DIR=<folder>` to keep templates, palettes and logs somewhere else,
`PIXED_PLUGINS=<folder>;<folder>` for plugin DLLs.

## 4. How to ask: ready-made prompts

State **what you want, where the files are, where the result goes and how success is measured**. A measurable rule lets the agent prove the result with checks instead of guessing.

**Clean up a sprite**

> Tools are in `C:\tools\pixed`; read `AI-GUIDE.md` there. Clean up `C:\art\hero.png` with the template "Clean up sprite" and write the result to `C:\art\out\hero.png`.
> It must have at most 16 colors, binary alpha and no stray pixels. Show me the result enlarged ×8.

**Photo to pixel art** (the agent may only download what you allow)

> Make a 64 px pixel-art version of `C:\art\fox.jpg` (background: grass, so automatic cutout may fail; trace the subject with `polygonMask` if it does).
> Save your own template as "fox-64" so I can reuse it. Do not overwrite the source file.

**A whole folder**

> Run the template "Remove background and clean edges" on every PNG in `C:\art\raw` and write results to `C:\art\clean`. Report any file whose checks fail.

**Placeholders**

> Draw a 24×32 front-facing guard with a helmet and spear using `draw_scene` (a JSON scene, not pixel by pixel). Show me, then fix the proportions once.

**Animation for a game**

> Slice `C:\art\walk.png` (cells 32×32), assemble the frames into a sheet with rows "down, up, left, right" and export for Godot 4 and Unity into `C:\export\walk`.

**Test whether an agent can use the tools at all** (a good first run): ask it to find a CC0 animal picture, download it with `pixed fetch`, pick a template, write its own template with 3 or 4 checks,
iterate until exit code 0, save the template to the library and run it again by name. Then ask for a short report: number of calls, errors, parts of the guide that confused it.

## 4b. No agent? Use any ordinary AI chat

You do not need MCP, an agent or even the command line. Any chat (a web chat, a phone app) can write the pipeline, and the editor applies it:

1. In the editor choose **Templates → From an AI chat…** (one window for the whole job) and press **Copy the prompt**. The prompt contains the format, the full list of operations and checks, rules that give good results and the size of your current image.
2. Open any AI chat, **paste the prompt**, then **attach your photo** and say what you want ("64 px pixel art, 16 colors, black outline, remove the background").
3. The chat answers with a JSON block. Copy the whole answer, return to the same window, click the big box and press **Ctrl+V**. The text is cleaned to plain JSON and the result is kept as a variant automatically.
4. The editor finds the JSON inside the answer (explanations and markdown are fine), checks every operation name and parameter, and says "ready: N steps, M checks". The JSON stays **editable**: change a number or a color by hand and the *Before / After* preview on the right updates by itself, with steps and checks under *Steps and checks*. Nothing changes in your picture until you press **Apply**.
5. Not sure which answer is better? Paste another answer (each new one is kept as a variant automatically; identical ones are not duplicated) or edit the JSON and press **Keep as variant** (shown only for a result not kept yet), then compare the thumbnails in *Variants to compare*; click a variant to load its JSON back, then **Apply** the best one.
6. Happy with it? **Save as template…** keeps the pipeline in *My templates*, so you can run it on any other picture.

If the answer has a mistake (a made-up operation name, a broken JSON), the dialog shows it with a suggestion ("did you mean resize?") so you can fix it by hand or ask the chat to redo it and paste the new answer.

The same works from the command line: `pixed chat-prompt --image photo.png > prompt.txt` makes the prompt, `pixed pipeline-check answer.txt` validates a saved answer and `pixed run answer.txt photo.png out.png` runs it (the text file may contain the whole chat answer).

Tips: tell the chat the real size of the picture if it differs from the one in the prompt; for a photo with a busy background ask it to trace the subject with `polygonMask` and give coordinates; ask for "one JSON block only" if it adds too much text.

## 4c. Draw a sprite or a whole sprite sheet in a plain chat (no reference picture)

The same window **Templates → From an AI chat…** can ask the chat to DRAW from nothing, out of primitives (rectangles, ellipses, lines, polygons):

1. In the window choose what to ask for: **Draw a sprite** (one picture) or **Draw a sprite sheet** (several directions and animation frames in one answer), then press **Copy the prompt**.
2. Paste the prompt into any AI chat and describe what you want: "a guard in a blue coat, 24x32, walking in 4 directions, three walk frames". Custom frames work too: "add an attack row with 4 frames, the sword swings down".
3. Paste the answer back (Ctrl+V). The window finds the JSON, checks it (a mistyped shape name is reported with its place, for example "Row S, frame 2: ... did you mean ellipse?") and draws a live preview. You can edit the JSON by hand: shapes are laid out one per line.
4. Press **Add as a layer** (a sprite lands as a new layer, the canvas grows if the sprite is bigger) or **Open as a sheet** (a new animation sheet with the grid, row names, frame names and delays already set; export it from the Animation menu). A sprite can also open as a **New document**.
5. Not happy? Paste another answer (each one is kept as a variant to compare) or ask the chat for a fix and paste again.

**The layout follows your choice.** For *Draw a sprite sheet* the window shows a compact **sheet layout** selector (the same templates as *New animation sheet*, yours included; the last one is preselected) plus the frame size and an optional delay for all frames; the prompt then REQUIRES exactly that layout (frame size, row names in order, frame names, delays) and its example uses it. If the current document is already an animation sheet or an RSI state, its own layout is required instead and the selector is hidden. If the answer differs (other rows, frame count or size) the window says what was expected and what came back, but you can still preview and open it. Frame names and delays missing from the answer come from the requested layout; ones the answer gives win. When the answer has the grid of the current sheet, the main button is **Draw into this sheet** (rows, frame names and delays of the sheet are kept, Undo works) and **Open as a new sheet** is the second button.
A sheet is described compactly: the body goes into `base` and every frame lists only what differs (the legs). A row can be a mirror of another (`"W": mirrorOf "E"`) or a copy, and frames can be shifted (`offset`) or repeated (`copyOf`).
The same works from the command line: `pixed chat-prompt --kind sheet` makes the prompt (add `--template walk-4`, or `--rows idle,attack --frames stand:0.5,swing:0.1 --size 32x32 --delay 0.2`, to require a layout), `pixed pipeline-check answer.txt` validates an answer (it says Sprite or Sheet), `pixed draw answer.json out.png` draws a sprite and `pixed draw-sheet answer.json out.png` a sheet.

## 5. What to expect

- Exit codes: **0** success, **1** error, **2** the steps ran but a check failed (the picture is written so the agent can look at it and fix the template).
- Templates the agent saves go to `%AppData%\PixelEditor\pipelines` and appear in the editor menu **Templates → My templates**; palettes go to `%AppData%\PixelEditor\palettes`.
- The agent writes only to paths you named and downloads only with your permission. Tell it that explicitly.
- Automatic cutout (`cutout`) struggles when the subject and background have the same tone and texture; the agent should use `polygonMask`.
- Plugins are third-party code that runs with full rights: only load DLLs you wrote or reviewed.

## 6. Troubleshooting

| Problem | What to do |
|---|---|
| The assistant does not list `pixel-editor` | check the path in the command, restart the assistant, run `pixed-mcp.exe` once from a terminal (it waits for input; press Ctrl+C) |
| "exe not found" | give the agent the full path to `pixed.exe`; build the tools into a folder without spaces |
| JSON arguments get mangled in PowerShell | the agent must put JSON in a file and pass the path (this is in the guide) |
| Exit code 2 | a check failed: ask the agent to show the check messages and adjust the template |
| Russian text in the answers | unset `PIXED_LANG` or use `PIXED_LANG=en` |
| Something crashed or an operation failed | **the agent can read the log itself** (`pixed log` or the MCP tool `read_log`): ask it "check the log and tell me what failed". You can also read it:  **Help → Open error log folder** in the editor, or `%AppData%\PixelEditor\logs\pixed-YYYYMMDD.log`. Entries are English and include the stack trace; attach them to a bug report |

## 7. Staying safe

Give the agent a working copy, not your only original. Review the output before it replaces anything. Keep downloads limited to free licenses (CC0 or public domain) and ask the agent to record the source next to the result.
