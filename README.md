# Pixel Editor

**Version 1.0.0** (under active development, pull requests welcome) · License: GPL-3.0-or-later · Copyright (C) 2026 mrl4an · [Русская версия](README.ru.md)

A lightweight pixel-art and sprite editor for Windows (WPF, .NET 10) with a full set of tools for AI agents.
Transparency is binary: a pixel is either present or empty. All logic lives in `PixelEditor.Core`, so the same operations are available in the editor,
on the command line (`pixed`) and through an MCP server (`pixed-mcp`) for AI assistants.

- **Editor:** layers, pencil, eraser, fill (with tolerance), eyedropper, shapes, gradient, lighter/darker brush, text, selections (frame, wand, lasso, polygon),
  move, stretch and rotate handles like in Paint, palettes, tool memory between launches, light and dark themes, Russian and English interface (switch on the fly).
- **Animation and game formats:** sprite sheets, GIF export, RSI (Space Station 14) states with `meta.json`, export for Unity (Aseprite JSON) and Godot 4 (`SpriteFrames` `.tres`),
  animation templates (idle, walk in 4 or 8 directions, attack, door and your own).
- **Processing:** 48 operations (resize with several methods, quantizing, palettes, cutout, outline, color and shape tools, despeckle...), 11 result checks,
  12 built-in templates (pipelines) and your own library of templates and palettes.
- **For AI:** works even with a plain AI chat (copy a prompt, paste the JSON answer, preview, apply) and with agents: CLI and MCP with the same capabilities, 32 MCP tools, an error log the agent can read itself. People: [docs/AI-TOOLS-HOWTO.md](docs/AI-TOOLS-HOWTO.md); agents: [docs/AI-GUIDE.md](docs/AI-GUIDE.md).

## Build and run

**Quick start:** double-click `PixelEditor.cmd` in this folder. It builds the editor into `app\` on the first run and **rebuilds automatically when the sources are newer**, so an old version is never started (needs the .NET 10 SDK and Windows). `build.cmd` makes a full fresh build: the editor into `app\` and the AI tools into `tools\` (`--test` runs the tests first, `--clean` removes old output, `--gui` builds only the editor). New to the editor? Read [docs/USER-GUIDE.md](docs/USER-GUIDE.md).

From a terminal:

```
dotnet build
dotnet run --project src/PixelEditor.App
dotnet test tests/PixelEditor.Tests
dotnet test tests/PixelEditor.ViewModels.Tests
dotnet run --project tests/PixelEditor.GuiSmoke     # opens a window, needs a desktop
```

Publish the AI tools into one folder:

```
dotnet publish src/PixelEditor.Cli -c Release -o <folder>
dotnet publish src/PixelEditor.Mcp -c Release -o <folder>
claude mcp add pixel-editor -- "<folder>\pixed-mcp.exe"
```

User data (settings, palettes, templates, autosave) lives in `%AppData%\PixelEditor`; the `PIXED_DATA_DIR` environment variable overrides the folder.

**Error log:** every failure is written (in English, with stack traces) to `%AppData%\PixelEditor\logs\pixed-YYYYMMDD.log`. Open it from **Help → Open error log folder**; an AI agent reads it with `pixed log` or the MCP tool `read_log`. `pixed --version` prints the version.

## Projects

| Project | Purpose |
|---|---|
| `src/PixelEditor.Core` | UI-free library: pixels, layers, undo, selection, drawing, sheets, GIF, RSI, operations, checks, pipelines, plugins, texts |
| `src/PixelEditor.Imaging` | Image files (PNG, JPG, BMP, GIF, WebP) and the high-level `PixelEngine` |
| `src/PixelEditor.ViewModels` | MVVM view models without WPF: all interface logic, localization |
| `src/PixelEditor.App` | WPF views (`PixelEditor.exe`) |
| `src/PixelEditor.Cli` | `pixed.exe`, command line for agents and scripts |
| `src/PixelEditor.Mcp` | `pixed-mcp.exe`, MCP server (stdio) |
| `tests/*` | xUnit tests for the core and view models, and a GUI smoke test |
| `scripts/audit_ai_access.py` | Calls every operation, check, template and MCP tool to prove an AI can reach them |
| `pipelines/`, `plugins-sample/`, `examples/` | Template samples, a sample plugin, example scenes and photos |

## Editor basics

- **Tools (left panel):** pencil `B`, eraser `E`, fill `G`, eyedropper `I`, shapes `L R O`, selection `M W S P`, move `V`, gradient `D`, lighter/darker `U`, text `T`.
  Right-click the Shapes and Selection buttons to pick a variant. `Alt+click` is the eyedropper, `X` swaps colors.
- **Selections:** after a selection the Move tool is active: drag inside to move, drag the squares to stretch or shrink (Shift keeps proportions on corners),
  drag just outside a corner to rotate (Shift snaps to 15°). Presets: 90° left and right, 180°, any angle.
- **View:** wheel zooms around the cursor, `Space`+drag pans, `F` fits the canvas to the window.
- **Layers:** every shape, paste and text goes to its own layer by default (Edit menu).
- **Palettes and colors:** palette library, used-colors panel, import and export of `.hex` and `.gpl`.
- **Import:** `Ctrl+I` or drag a picture into the window: downscale with several methods, quantize, clean up and outline, optional preview.
- **Automation (Templates menu):** run templates on the current or all layers, batch a folder, build your own template in the constructor.
- **Language:** the `RU`/`EN` button next to the theme switch. Extra languages: put a JSON file named after the language code into `%AppData%\PixelEditor\locales`.

## Pipelines and checks

A template is JSON with `steps` (operations) and `checks` (acceptance criteria):

```json
{ "name": "photo-to-sprite",
  "steps": [
    { "op": "trim" },
    { "op": "resize", "width": 32, "method": "mode" },
    { "op": "alphaThreshold", "value": 128 },
    { "op": "quantize", "colors": 16 },
    { "op": "despeckle", "maxArea": 3, "diagonal": false },
    { "op": "outline", "color": "#000000" } ],
  "checks": [ { "check": "maxColors", "value": 16 }, { "check": "binaryAlpha" } ] }
```

Run it with `pixed run photo-to-sprite.json in.png out.png` (exit codes: 0 success, 1 error, 2 checks failed) or by the name of a built-in template.
The full catalogs are available with `pixed ops`, `pixed checks`, `pixed templates` and `pixed palettes`.

## Plugins

A custom operation or check is a class in a DLL that implements `IPixelOperation` or `IPixelCheck` from `PixelEditor.Core.Plugins`.
Put the DLL into a `plugins` folder next to the program or into `%AppData%\PixelEditor\plugins` (the CLI also accepts `--plugins <folder>`).
See `plugins-sample/SymmetryPlugin`. A plugin runs with the application's rights: load only DLLs you trust.

## Documentation

- [docs/USER-GUIDE.md](docs/USER-GUIDE.md): for beginners and confident users: drawing, automatic tools, pipelines for one style, AI tools, animation sheets.
- [docs/AI-TOOLS-HOWTO.md](docs/AI-TOOLS-HOWTO.md): how a person sets up and uses the AI tools (connect MCP, prompts, troubleshooting).
- [docs/AI-GUIDE.md](docs/AI-GUIDE.md): the guide an AI agent reads (English only).
- [AGENTS.md](AGENTS.md): project map for developers.
- [docs/RELEASE.md](docs/RELEASE.md): what is still missing for a release.
- [docs/IMPROVEMENTS.md](docs/IMPROVEMENTS.md), [docs/TOOLS_REVIEW.md](docs/TOOLS_REVIEW.md): ideas and comparison with other editors.
- Russian versions of all of the above (except the AI guide) are in [docs/ru](docs/ru) and [README.ru.md](README.ru.md).

## Contributing

The project is **actively developed and pull requests are welcome**: fixes, operations, tools, translations, docs, examples. See [CONTRIBUTING.md](CONTRIBUTING.md) for the rules and good first tasks ([docs/IMPROVEMENTS.md](docs/IMPROVEMENTS.md)).

## License

GNU General Public License v3.0 or later (see [LICENSE](LICENSE)). The source code is open; if you use or redistribute it, you must keep the copyright notice
(Copyright (C) 2026 mrl4an) and release derivative works under the same license.
