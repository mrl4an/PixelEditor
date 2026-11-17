# AGENTS.md: Pixel Editor project map

> **If you are an AI that only uses the tools** (process, draw or export pictures), do not read this file or the sources: the minimum you need is in [docs/AI-GUIDE.md](docs/AI-GUIDE.md). This file is for people and agents who change the project itself.
> Русская версия: [docs/ru/AGENTS.md](docs/ru/AGENTS.md).

A pixel-art editor on WPF (.NET 10) and a toolkit for automatic sprite processing. Version 1.0.0, GPL-3.0-or-later, copyright (C) 2026 mrl4an.
Main principle: **all logic lives in the `PixelEditor.Core` library; the GUI, CLI and MCP server only call it.**
For a new feature, add it to Core first (with a test), then connect it to the GUI and to `PixelEngine` (CLI and MCP).

## Map

```
PixelEditor/
  AGENTS.md, README.md, README.ru.md, LICENSE, Directory.Build.props (version, author, license)
  PixelEditor.sln
  docs/                  AI-GUIDE (English only), AI-TOOLS-HOWTO, IMPROVEMENTS, TOOLS_REVIEW, RELEASE; Russian versions in docs/ru/
  PixelEditor.cmd        double-click launcher (builds into app\ on first run and rebuilds when sources are newer, then starts the GUI)
  build.cmd              full build: editor into app\, pixed and pixed-mcp into tools\ (--test, --clean, --gui); refuses to run while the editor is open
  scripts/audit_ai_access.py   calls every operation, check, template and MCP tool
  scripts/make_icon.py   regenerates the application icon (src/PixelEditor.App/Assets: app.ico, logo-64.png)
  pipelines/             template samples (JSON); working templates live in the library (Core/Ops/PipelineLibrary.cs)
  plugins-sample/SymmetryPlugin/   sample plugin (a check and an operation)
  examples/              animals (scenes), web (CC0 photo experiments with SOURCES.txt)
  src/
    PixelEditor.Core/    UI-free, Windows-free DLL: all logic
      Pixel.cs, PixelImage.cs   pixel format, flat raster (the unit of exchange between operations)
      Layer.cs, Document.cs     layers and document: compositing, layer undo, ResizeCanvas, sheet grid, RSI binding
      PixelBatch.cs, History.cs edits with undo (only changed pixels are stored)
      Selection.cs, SelectionMask.cs   PixelRect, copy and clear, FloatingSelection (lift, move, transform, Save/Restore, ScaleFrom), wand and lasso masks
      Drawing.cs, PaintContext.cs      line, rectangle, ellipse, fill (tolerance, whole layer), brush
      Gradient.cs, PixelFont.cs        dither gradients, 5×7 font (Latin and Cyrillic)
      ColorStats.cs, Hsv.cs, PaletteIO.cs, PaletteTools.cs (incl. ShadeRamp), PaletteLibrary.cs   colors, HSV, .hex/.gpl, built-in and user palettes
      AppPaths.cs               data folders (PIXED_DATA_DIR or %AppData%/PixelEditor): palettes, pipelines, plugins, logs
      ProjectIO.cs              .pixproj (zip with layers, sheet grid, RSI binding)
      Sheet.cs, SheetTemplates.cs, GifWriter.cs, Rsi.cs, Scene.cs   sheets and animation templates, GIF89a encoder, RSI and animation export, scene primitives
      RsiFolder.cs              command-line RSI workflow over a folder (info, new, state, export); an image codec is passed in, so it is testable without WIC
      InputArgs.cs              `@file` / `-` (stdin) command-line arguments and `*` / `?` file globs (CLI helpers, unit-tested)
      Localization/CoreText.cs  texts of the core (see "Localization")
      Diagnostics/AppInfo.cs, Log.cs   version/author/license and the error log (see "Version, license, log")
      Plugins/Plugins.cs        IPixelOperation, IPixelCheck, PluginLoader
      Ops/
        Operations.cs, MoreOpsRegistry.cs   OPERATION REGISTRY (name, description, parameters, function); 49 operations
        MoreOps.cs              ColorOps, ShapeOps, Despeckle algorithms
        Checks.cs               result CHECK REGISTRY (11 checks)
        Pipeline.cs, PipelineLibrary.cs     template: JSON with steps and checks, run, step and check log; built-in and user templates
        Resampler.cs, Quantizer.cs, Perceptual.cs, Cleanup.cs, Background.cs, Cutout.cs, Outline.cs, Lighting.cs, Adjust.cs, Rotation.cs, Transform.cs, ColorRegions.cs, SpriteStyle.cs, ImageComparison.cs   operation algorithms
    PixelEditor.Imaging/  (net10.0-windows) image files through WIC: ImageIO, ImageFetcher, PixelEngine (HIGH-LEVEL API for CLI and MCP)
    PixelEditor.ViewModels/ (net10.0, NO WPF) MVVM without libraries: all interface logic
      Mvvm.cs, Services.cs, EditorSession.cs, MainViewModel.cs   base classes, service interfaces (replaced in tests), shared state, root model
      CanvasViewModel*.cs       tools, strokes, selection with move, stretch and rotate handles (Scale.cs, Rotate.cs), clipboard, zoom, preferences (Prefs.cs)
      LayersViewModel.cs, ColorsViewModel.cs, SheetViewModel.cs, AutomationViewModel.cs, RsiViewModel.cs, AnimationViewModel.cs
      AppSettings.cs            settings (tool memory, default canvas size, language, theme, recent files)
      Loc.cs, Locales/ru.json, en.json   interface localization
      Messages.cs, SmallDialogs.cs, ImportViewModel.cs, ImageSizeViewModel.cs, PaletteLibraryViewModel.cs, TemplateLibraryViewModel.cs, ...   dialogs
    PixelEditor.App/        WPF views (PixelEditor.exe): XAML bindings, thin code-behind, service implementations
      Themes/                   ALL STYLING (from the Figma layout): Palette.Dark/Light.xaml tokens, Metrics.xaml, Controls/*.xaml, Icons.xaml, Ui.cs, ThemeManager.cs
      Localization/TExtension.cs   {loc:T key} markup extension
      Services.cs               DialogService (model type to window), clipboard, timers, batch runner, explorer
    PixelEditor.Cli/        pixed.exe: command line over PixelEngine, JSON output
    PixelEditor.Mcp/        pixed-mcp.exe: MCP server (stdio) over PixelEngine, 32 tools
  tests/
    PixelEditor.Tests/               xUnit tests of the core (including an independent GIF decoder, OperationsCatalogTests that runs every operation)
    PixelEditor.ViewModels.Tests/    xUnit tests of view models with fake services, no windows
    PixelEditor.GuiSmoke/            view smoke test: opens windows, checks bindings and raster, scans English mode for Russian labels
```

Dependencies: `App`, `Cli`, `Mcp` use `Core` and `Imaging`; `App` also uses `ViewModels`; `ViewModels` and `Imaging` use `Core`; `Core` depends on nothing.

## Commands

```bash
dotnet build                                   # whole solution
dotnet test tests/PixelEditor.Tests            # core tests (must be green)
dotnet test tests/PixelEditor.ViewModels.Tests # view model tests
dotnet run --project tests/PixelEditor.GuiSmoke   # GUI smoke test (needs a Windows desktop), exit code 0 on success
PIXED_SMOKE_SHOTS=<folder> dotnet run --project tests/PixelEditor.GuiSmoke   # also saves PNGs of the main window (dark and light) and all dialogs
PIXED_SMOKE_LANG=en dotnet run --project tests/PixelEditor.GuiSmoke          # English dialogs: scans each for Russian labels (checks that compare Russian text fail by design)
python scripts/audit_ai_access.py [folder_with_exe]                         # AI access audit
dotnet run --project src/PixelEditor.App
```

If the editor is running, `PixelEditor.exe` in `bin` is locked and rebuilding App fails: close it or build App elsewhere (`dotnet build src/PixelEditor.App -o <temp>`).

## Data model

- A pixel is a `uint` `0xAARRGGBB`. **Empty is exactly `0`.** In a document alpha is only 0 or 255.
- Semi-transparent pixels exist only in input pictures, before the `alphaThreshold` step.
- `PixelImage` and operations never change the input; they return a new picture.
- Document edits go through `PixelBatch` and `History`, otherwise there is no undo.
- `Document.ResizeCanvas` replaces layer objects: old layer references become invalid, subscribe to `CanvasChanged`.
- Animation frames: if a sheet grid is set and the canvas holds more than one cell, frames are cells; otherwise frames are layers bottom to top.

## For agents: how to process sprites

Use [docs/AI-GUIDE.md](docs/AI-GUIDE.md): templates (steps plus checks), `pixed run` exit codes 0/1/2, CLI and MCP. In short: turn the user's rule into a template, add **checks that prove the rule holds**,
run it, look at the result, fix the template until exit code 0, save it to the library (`save-template` or MCP `save_template`).

## Libraries of palettes and templates

- **Built-in** (in code, immutable): `BuiltInPalettes.All` (`Core/PaletteLibrary.cs`), `BuiltInTemplates.All` (`Core/Ops/PipelineLibrary.cs`), id `system:<id>`. The lists are rebuilt on every access so names and descriptions follow the current language.
  A new built-in item: add it to the list and make sure `LibraryTests` and `CoreLocalizationTests` pass (they run every built-in template on a sample and require an English translation).
- **User** items are JSON files in `%AppData%\PixelEditor\palettes` and `pipelines` (`PIXED_DATA_DIR` overrides), id `user:<file name>`. `Save`/`Delete` check the name (required, unique among built-in and user), the content (a template passes `Pipeline.Validate`).
  A built-in name is found in any language (`CoreText.SameName`).
- **Project palette** lives in `Document.Palette/PaletteName` and in `.pixproj`; `ColorsViewModel` keeps it in sync.
- **Template constructor** (`PipelineEditorViewModel`) builds parameter fields from `OperationInfo.Parameters` strings of the form `name:type=default (explanation)`: keep this format when adding operations, otherwise the field becomes a `string`.

### Connecting the MCP server

```bash
claude mcp add pixel-editor -- "<path>\pixed-mcp.exe"
```

Plugins are picked up from the standard folders, from `--plugins <folder>` and from the `PIXED_PLUGINS` variable (paths separated by `;`). Protocol: JSON-RPC lines on stdio, only protocol on stdout, diagnostics on stderr.

## Plugins: your own operations and checks

A DLL with classes implementing the interfaces from `PixelEditor.Core.Plugins`. Working sample: `plugins-sample/SymmetryPlugin` (check `symmetricX`, operation `mirrorLeftToRight`).

1. `dotnet new classlib -f net10.0`, reference `PixelEditor.Core` (ProjectReference or HintPath to `PixelEditor.Core.dll`).
2. A `public`, non-`abstract` class with a parameterless constructor implementing `IPixelCheck` (`CheckResult Run(PixelImage, OpArgs)`) and/or `IPixelOperation` (`PixelImage Run(PixelImage, OpArgs)`). Read parameters with `args.Int/Double/Bool/String/Color/Palette`. Never change the input; return a clone.
3. Build into a plugins folder: `dotnet build -c Release -o <folder>`. The folder is `plugins` next to `pixed.exe` / `PixelEditor.exe`, or `%AppData%\PixelEditor\plugins`, or any via `pixed --plugins <folder> ...`.
4. `pixed --plugins <folder> plugins` (MCP: `list_plugins`) shows what loaded and why something did not. Names must not clash with existing ones. `PixelEditor.*.dll` files in the plugins folder are ignored.

A plugin runs with the application's rights: load only DLLs written or reviewed in the same task.

## How to add an operation or a check

1. The algorithm: a function `PixelImage -> PixelImage` (or a check `PixelImage -> CheckResult`) in `Core/Ops/`.
2. Registration: `Operations.Build()` in `Core/Ops/Operations.cs` or `MoreOpsRegistry.cs` (or `Checks.Build()` in `Core/Ops/Checks.cs`): name, description, parameter list, lambda over `OpArgs`. The source description is Russian and is translated at read time (`OperationInfo.Description` calls `CoreText.T`); add the English text to `Locales/core.en.json`.
3. A test in `tests/PixelEditor.Tests`, **and a sample in `OperationsCatalogTests.Samples()`**: the catalog test runs every operation on a real picture and fails for an operation without a sample.
4. CLI and MCP pick it up (`pixed ops`, `list_operations`). In the GUI add a menu item in `MainWindow.xaml` (Operations menu, `CommandParameter='name|{json args}'`; `"$primary"`, `"$secondary"` and `"$selectedUsed"` (the color selected in the used-colors list, else the secondary color) inside the JSON are replaced by hex colors when the command runs; the tuner resolves them too) and the localization keys. The Operations menu is flat with separators: tuner, background, cleanup, outline, color, shape and light; rarely used operations stay in the registry and in "Other operation…" only.

## How to change the GUI (MVVM without libraries)

Rule: **view models (`PixelEditor.ViewModels`) make the decisions, the window only shows their state.**

- New behavior: a property or command in the right model, a binding in `MainWindow.xaml`, a test in `PixelEditor.ViewModels.Tests`.
- Models know nothing about WPF (no `Window`, `MessageBox`, `Clipboard`, `Key`, brushes). Everything they need goes through the interfaces in `Services.cs`; implementations live in `PixelEditor.App/Services.cs`, fakes in `Fakes.cs`.
- Shared state only in `EditorSession`; models talk through its events. Actions that change a layer first call `Session.FlushPending()` (commits the floating selection).
- Commands are `RelayCommand`; raise `CanExecuteChanged` manually (`RaiseCanExecuteChanged`) and call `Session.NotifyStateChanged()` after state changes that affect availability.
- Code-behind may only hold what belongs to WPF itself: the raster (`WriteableBitmap`), overlays (grid, selection frame, handles), mouse to `PointerDown/Move/Up`, scrolling, letter hotkeys, timers. Ctrl shortcuts are declared in XAML.
- Bind status to `Session.StatusText`, not to a copy in another model.
- Dialogs are MVVM too: a model inherits `DialogViewModel`, is shown through `IDialogService.ShowDialog/Show`; which window belongs to a model is recorded in one place, `DialogService.CreateView`. New dialog: a model, a window with empty code-behind, a line in `CreateView`, a branch in `FakeDialogs.ShowDialog`.
- Notifications and questions are a model, not `MessageBox`: `MessageViewModel`. For errors in `catch` blocks call `dialogs.ShowError(ex)` (the extension logs the exception with its stack and shows the message); use `ConfirmDestructive` for deletion.
- Timers only through `IUiTimerFactory`; in tests `FakeTimers.FireAll()`.
- Data lives in `%AppData%\PixelEditor` (`settings.json`, `autosave.pixproj`, `logs`); `PIXED_DATA_DIR` overrides (the smoke test uses it).

## How to change styles (theme)

Source of truth: the Figma layout. Styling lives only in `PixelEditor.App/Themes`.

- **Colors only as tokens** (`{DynamicResource Brush.Surface}`, `Brush.Text`, `Brush.Accent`...). A new token goes into both `Palette.Dark.xaml` and `Palette.Light.xaml` under one key. Exceptions: the canvas itself (checkerboard, grid, selection frame, handles).
- Sizes, fonts, radii come from `Metrics.xaml`.
- **Implicit styles** in `Themes/Controls/*.xaml` apply to every control of that type in every window (Button, TextBox, CheckBox, RadioButton, Expander, ComboBox, Slider, ListBox, ScrollBar, Menu, MenuItem, ContextMenu, StatusBar, ToolTip, GroupBox, TabControl, Window...).
- **Named variants:** `PrimaryButton`, `IconButton`, `ToolButtonStyle` (+ `ui:Ui.Icon`, `ui:Ui.Hotkey`), `ChipStyle`, `LockToggleStyle`, `MonoTextBox`, `NumericTextBox`, `PanelSection`, text styles `SectionTitleText`, `DialogHeadingText`, `DetailTitleText`, `MutedText`, `CaptionText`, `MonoText`, `ErrorText`.
- A style with `BasedOn` for MenuItem: `BasedOn="{StaticResource {x:Type MenuItem}}"`, otherwise `ItemContainerStyle` loses the look.
- The custom window title is defined in the `Window` style (`Controls/Containers.xaml`): `ui:Ui.Subtitle`, `ui:Ui.TitleContent` (a resource with `x:Shared="False"`, see `ThemeSwitch` in `MainWindow.xaml`), window buttons by `ResizeMode`.
- `ThemeManager.Apply("Dark"|"Light"|"System")` swaps the palette dictionary; open windows recolor immediately. The UI offers only the light/dark toggle in the title bar; `System` is kept in `ThemeManager` internally, but `AppSettings.Load` turns a saved `System` (or anything unknown) into `Dark`.

## Pitfalls

- **Code comments are written in English** (`//`, `///`, XAML comments, scripts). Russian stays only in user-visible strings: `CoreText.T("...")` sources (they are translation keys), `ru.json`, and test assertions that check Russian texts.
- In WPF projects (`App`, `Imaging`, `Cli`, `Mcp`) there is no implicit `using System.IO`.
- `Drawing` and `Transform` clash with `System.Windows.Media`: use full names or aliases in WPF code.
- `C.Zoom` is an `int`: write `7.0 / C.Zoom`, not `7 / C.Zoom` (integer division gave a zero hit radius for the stretch squares).
- `Window_Loaded` may show an autosave message box: delete `%AppData%\PixelEditor\autosave.pixproj` in automated runs.
- A resource file named `core.en.json` is treated by MSBuild as a culture resource: it needs `WithCulture="false"` and a `LogicalName` in the csproj.

## Behavior that is easy to break (covered by tests)

- **Paste (`Ctrl+V`):** a picture larger than the canvas asks and enlarges the canvas (`CanvasViewModel.Paste`). Clipboard reading tries PNG, then a normal bitmap (zero alpha counts as opaque), then a copied picture file.
- **Import:** by default the picture is offered as is (original size, no cropping or quantizing); presets: original, 32, 64, 128 on the long side.
- **Wheel over the canvas:** always zooms around the cursor (`MainWindow.ZoomAt`); Shift+wheel scrolls horizontally, Ctrl+wheel vertically; `Space`+drag or the middle button pans; `F` fits.
- **Running a template from the editor** first shows `ResultPreviewViewModel` (before/after, steps, checks) and applies only on the button; if the size changes the button says "Open in a new document".
- **Selections:** a new selection no longer switches the tool: under Select, Wand, Lasso, Polygon (and Move) a press inside drags it, handles and the rotate zone work (`CanvasViewModel.HandlesActive`), a press outside starts a new selection (Move: clears and takes the object under the cursor). Select all from a drawing tool goes to the last selection tool; a paste keeps the tool and stays draggable until the first click outside. Background eraser (K, `BgEraserClick`) uses `SelectionMask.Wand` with its own tolerance/adjacent/follow options. Leaving the selection family for a drawing tool (except Gradient) clears it. The press clears the old selection immediately (`BeginSelectDrag`).
- **Handles:** at the Move tool eight squares around the frame stretch (Shift keeps proportions on corners; size is computed from a snapshot, nearest neighbor, `CanvasViewModel.Scale.cs`, `FloatingSelection.ScaleFrom`); dragging just outside a corner rotates (`MainWindow.InRotateZone`, angle from the grab point, Shift snaps to 15°, `RotateHandleTo`, rotated frame overlay `ShowRotationFrame`). Both start in `Workspace_MouseDown` and in `Canvas_MouseDown`; the smoke test checks the hit geometry.
- **Object click without drag** keeps the object selected (it used to clear the selection on release).
- **Layers for objects:** shapes, paste and text create a new layer by default (`CanvasViewModel.NewLayerForObjects`, Edit menu); the layer and the first edit are one history step.
- **Tool memory:** `CanvasViewModel.Prefs.cs` saves the tool and its options to `settings.json`; Move is not restored at startup. The default canvas is 64×64 (`AppSettings.DefaultWidth/Height`, remembers the last "New document" size); tests set 32×32 in `Rig`.

- **Operations and the selection:** `AutomationViewModel.SelectionOnly` ("Operations and templates only inside selection", Edit → Modes) limits single operations, the tuner and templates to the selection by restoring the protected pixels (`Pipeline.Run(img, protectMask)` for templates, `RestoreProtected` for operations); without a selection nothing is applied. `AutomationViewModel.AllLayers` ("Templates: apply to all layers", also Edit → Modes) affects templates only. `"$primary"` in an operation's JSON is the primary color (black when empty).
- **Hotkeys:** Ctrl shortcuts live in `MainWindow.xaml` (`Ctrl+Shift+Z` redo, `Ctrl+D` deselect, `Ctrl+J` duplicate layer, `Ctrl+Shift+N` new layer, `Ctrl+0` fit, `Ctrl+1` 100%). Letter tool keys and `Ctrl+arrows` ignore keyboard focus in a TextBox, ComboBox, Slider and the layers list (`MainWindow.Window_PreviewKeyDown`); a click on the canvas clears such focus.

## Tools and drawing (extended)

- **Shape selections:** `Core/SelectionMask.cs` (wand with tolerance and connected-or-whole-layer, lasso, polygon, union and subtract, copy and clear by mask). `FloatingSelection.LiftMask/LiftObject/Transform` lift and transform an area with a shape; the VM keeps the shape in `_selMask`.
- **Tool groups:** Shapes (L R O) and Selection (M W S P) are single buttons; the left click uses the last chosen variant (`LastShapeTool/LastSelectionTool`), the right click opens a menu with icons (`ToolIconConverter`), chips in the options pick a variant.
- **Other tools:** gradient D, lighter/darker U, text T (`PixelFont`, 5×7), move object V. Selection transforms (rotate, flip) are in Edit → Transform (selection or layer): rotate 90°/180° and the flips act on the whole active layer when nothing is selected (one undo step; a non-square layer turns like "Rotate by angle…"). ×2 and ÷2 are done by the stretch handles; `ScaleUpCommand`/`ScaleDownCommand` stay in `CanvasViewModel` (tests) but have no menu item. Rotation by an angle is always crisp (`AppSettings.RotationSmooth` is obsolete and ignored).
- **Fill:** `Drawing.FloodFill(batch, x, y, color, tolerance, wholeLayer)`, tolerance 0..255 per channel.
- **Text tool:** live typing on the canvas (`CanvasViewModel.Text.cs`, `TextSession`); the preview is `EditorSession.Preview` (not in a layer), commit is one undo step; while `IsTextEditing` the window sends keys to `TextInsert/TextBackspace/...` and ignores hotkeys; Ctrl+Z undoes typing, then cancels the session.
- To add a glyph: a string of rows separated by `/` in `Core/PixelFontData.cs` (`G` caps and ascenders, `Low` x-height letters, `Desc` letters with tails); fonts live in `PixelFonts` (5×7 with lowercase, 3×5 capitals only, wide = 5×7 doubled); the test `PixelFonts_EveryGlyphHasTheCellSize_...` checks sizes.

## Drawing placeholders with a scene (for agents, cheap in tokens)

Do not draw pixel by pixel. Describe a drawing as a scene of primitives and call `pixed draw <scene.json|JSON> <out.png>` (MCP: `draw_scene`). One little person is about 10 JSON lines.
Format (details in `Core/Scene.cs`): `{"size":[w,h], "palette":{"skin":"#F1C27D"}, "mirror":"x", "outline":"#1B1B1B", "items":[...]}`. Shapes: `rect`, `ellipse`, `line`, `poly`, `pixels`, `fill`, and groups with `offset`, `mirror`, `lightFrom`;
each takes `color` (`#RRGGBB`, a palette name or `clear`), `fill:false`, `thickness`, `mirror`. They are drawn in order. Plan: the canvas of the task size, big shapes, small details, `mirror:"x"` for symmetric parts, `outline` last, then look at the result and fix coordinates.

## Icons

Vector (SVG-like): `Themes/Icons.xaml`, geometry in a 24×24 system, attached as `ui:Ui.IconData="{StaticResource Icon.Pencil}"` on a `ToolButtonStyle` button (filled ones add `ui:Ui.IconFilled="True"`). A real-size preview window: `PreviewWindow` (View menu).

## Resizing, cropping, outlines, pictures from the internet

- Edit → Image size (`ImageSizeViewModel`, `Document.ResizeImage`): scale all layers with a method (mode, box, nearest, smart), percent, ×2, ÷2. Canvas size changes the field without scaling. Crop: `Document.Crop`, "to selection" and "to content".
- Import: the "no resize (original)" method is selected by default and skips the resize step.
- Operations `removeOutline` (any thickness, including ragged; `dark:true` for several dark shades), `adjust`, templates "Remove outline" and "Picture from the internet to 64 px pixel art".
- Pictures from the network: `pixed fetch <url> <file>` and MCP `fetch_image` (http(s) only, image/*, up to 15 MB, `Imaging/ImageFetcher.cs`). Choosing a picture and checking its license is up to the agent or the user. The operation and check registries are locked: plugins may register from other threads.

## Smart photo processing (cutout, smart, lab, colored outline, despeckle)

- `cutout` (`Core/Ops/Cutout.cs`): cuts a subject out of a complex background without a neural network (k-means over border colors, flood fill from the edges with a boundary barrier `edgeStop`, `keepLargest`, `keepRect`). A plain background is easier with `removeBackground`. **Limit:** it cannot separate a subject from a background of the same tone and texture (fox on snow, grass and fur): use `polygonMask` with a rough hand-traced outline (recompute coordinates after `resize`) or an external segmentation model.
- `resize` with `method: "smart"`: two color clusters per block, the main one wins, a dark contrasting one (an outline) wins already at a quarter of the block.
- `quantize` and `applyPalette` with `space: "lab"` (`Core/Ops/Perceptual.cs`): k-means and nearest color in Lab.
- `outline` with `shade: true` and `darken`: an outline of a dark saturated shade of the neighboring color (sel-out).
- `despeckle` (`Core/Ops/MoreOps.cs`): small connected regions of one color are repainted with the neighbor that shares the longest border; on a quantized photo the share of tiny regions falls from about 88% to 1%. Use `diagonal:false` for photos (4-connectivity) and the default `true` for line art (a thin diagonal line stays whole).
  `removeIslands`, `fillHoles`, `cleanEdges` work on transparency only and do nothing on a fully opaque picture; `cleanEdges` removes a halo only when `matte` is given (with a `tolerance` smaller than the distance to a real outline).

## RSI and animation export

- RSI (the sprite format of Space Station 14): a folder `name.rsi` with `meta.json` (version, license, copyright, size, states) and `<state>.png`. A state: `name`, `directions` (1, 4, 8), `delays` (seconds, per direction and frame). Direction order S, N, E, W, SE, SW, NE, NW; sheet cells go direction by direction, frame by frame.
- A state sheet document: rows are directions, columns frames; size = `frames × width`, `directions × height`, exactly the PNG layout in an RSI. The binding `Document.Rsi` (`RsiBinding`) is stored in `.pixproj`. Zones (`RsiLayout.Zones`) are drawn by `MainWindow.DrawRsiZones`.
- Model and layout: `Core/Rsi.cs` (`RsiMeta`, `RsiLayout`, `AnimationExport`). Workflow (new RSI, new state, open, save, export): `RsiViewModel` and `RsiDialogs.cs`, RSI submenu of the Animation menu. Foreign PNGs wrapped by width are repacked (`RsiLayout.Repack`).
- `meta.json` round trip is lossless: `RsiMeta.Parse` keeps the original JSON nodes (root and per state), `ToJson` updates only known fields (and only for states set through `Upsert`), so foreign fields (`flags`, `load`, custom keys) and the state order survive. Export: `AnimationExport.Clip.Loop` (per-clip loop; CLI `noloop=`, MCP `no_loop`), the Unity editor script `AnimationExport.UnityImporterScript` (`Core/UnityImporter.cs`, unverified in Unity; a test ties it to the `SimpleJson` field names), and `Document.ExportProfile` (`AnimationExportProfile`, in `.pixproj`) that pre-fills the dialog and drives Animation → Export again.
- Without the editor: `pixed rsi info|new|state|export` and MCP `rsi_info`, `rsi_new`, `rsi_set_state`, `rsi_export_state` (`Core/RsiFolder.cs`, wrapped by `PixelEngine.Rsi*`). meta.json is written only through `RsiMeta.Parse` -> `Upsert` -> `ToJson`.
- CLI arguments that carry JSON or text accept `@path` (read from a file) and `-` (stdin); input image lists accept `*` and `?` in the file name (`Core/InputArgs.cs`: `InputArgs`, `FileGlob`). When a CLI command gets a new JSON-or-file argument, resolve it through `inputArgs` in `Cli/Program.cs`.
- Export (`AnimationExport`): a sheet PNG, Aseprite JSON (frames with durations and tags per direction; Unity), Godot 4 `SpriteFrames` (.tres, one animation per direction), a GIF per direction, simple JSON. Without the editor: `pixed anim-export` and MCP `export_animation`.
- Animation → New animation sheet makes a sheet without RSI with your own row names (`RsiBinding.Rows`, `IsRsi=false`). Sheet layout templates (`Core/SheetTemplates.cs`, folder `sheettemplates`): rows and frames with names and delays; only 1/4/8-direction templates are offered for RSI.
- Lighting (`Core/Ops/Lighting.cs`, operation `light`; `edgeShade`, scene `lightFrom` and sprite styles call the same engine): the opaque silhouette without a dark outline is a height field (Euclidean distance from the edge plus smaller domes for inner color regions), lit by a directional light, the light value is cut into `bands` crisp bands, each a step of `PaletteTools.ShadeStep` (hue-shifted ramp) of the pixel's own color; optional palette snapping. Tests: `LightingTests`.

## Localization

- **Interface:** `ViewModels/Loc.cs` with `Locales/ru.json` (source of truth) and `en.json`; `{loc:T key}` in XAML (live switching through the `Item[]` indexer), `Loc.T(key, args)` in models. Extra languages: a JSON file in `%AppData%\PixelEditor\locales`. Add a key to both files; the parity test fails otherwise. The language button is in the title bar next to the theme switch.
- **Core, CLI, MCP:** `Core/Localization/CoreText.cs`: the source string in code is Russian, the English text lives in `Core/Locales/core.en.json` (key: the Russian string, format strings use `{0}`). Wrap new texts in `CoreText.T("...")` / `CoreText.F("... {0}", x)`. The language comes from `PIXED_LANG` (default `en`), `--lang`, or the editor's UI language; tests run in Russian (`TestLanguage`), `CoreText.Scope("en")` switches one call chain.
  `CoreLocalizationTests` fails if an English catalog contains Russian. Built-in template and palette names are found in any language.
- Documentation: English at the root and in `docs/`, Russian in `docs/ru/` and `README.ru.md`; `docs/AI-GUIDE.md` is English only.

## Version, license, log

- Version 1.0.0 is set in `Directory.Build.props` (`Version`, `AssemblyVersion`, `FileVersion`, author, license). `AppInfo` (`Core/Diagnostics`) exposes name, version, author and license: `pixed --version`, Help → About, MCP `serverInfo`.
- License GPL-3.0-or-later (`LICENSE`); every `.cs` file has an SPDX header and `Copyright (C) 2026 mrl4an`.
- **Error log:** `Core/Diagnostics/Log.cs`, a static `Log` (always English entries, one file per day `logs/pixed-YYYYMMDD.log` in the data folder, parts of 2 MB, 14 days kept, thread-safe, never throws).
  Writers: unhandled exceptions of the application (`App.xaml.cs`), `DialogExtensions.ShowError(ex)` (all models use it instead of `ShowError(ex.Message)`), CLI and MCP failures. Readers: Help → Open error log folder, `pixed log`, MCP `read_log` (so an AI can see it).
  Entries contain file paths: do not log secrets.

## Chat mode (pipelines from an ordinary AI chat)

- `Core/Ops/ChatPrompt.cs` builds the prompt from the LIVE catalog (always English; image size included); `Core/Ops/PipelineText.cs` extracts the JSON from a chat answer (fenced blocks, prose, smart quotes, trailing commas, comments), checks operation, check and parameter names with "did you mean" hints and builds a fix request.
- `ViewModels/ChatPipelineViewModel.cs` + `ChatPipelineDialog.xaml`: ONE dialog (menu Templates → From an AI chat…): copy prompt, paste, editable JSON with live validation, live before/after preview (debounced through `IUiTimerFactory`, `PipelineText.Pretty` for Format), up to 6 kept variants to compare, save as template; Apply runs `AutomationViewModel.RunPipelineFile(json, alreadyPreviewed: true)` (no second preview window)
- CLI: `pixed chat-prompt`, `pixed pipeline-check`; `PixelEngine.LoadPipeline` also accepts a file or text with a whole chat answer. `ChatPipelineTests` checks that the examples inside the prompt are valid and that every live operation is listed.

## Drawing from nothing through a chat (sprites and sheets)

- `Core/SheetScene.cs`: a sprite sheet from primitives (`frameSize`, `base`, `rows` with `frames`, `copyOf`, `mirrorOf`, per-frame `offset`, `delay`, `name`); every frame is a `Scene` (base + row base + frame items). `SheetResult` carries the image, row and frame names, delays and `ToMetaJson()`.
- `Core/Ops/ChatScenePrompt.cs`: the English prompts for a sprite and for a sheet (the examples inside are checked by `SheetSceneTests`); `ChatPrompt.Build(ChatKind, ...)` dispatches. `PipelineText.Detect/Extract/Check` recognize a pipeline, a sprite (`items`) and a sheet (`rows`), check shape keys with "did you mean" hints and report where an error is (`Row "S", frame 2: ...`). `PipelineText.Pretty` keeps short objects on one line.
- The chat window (`ChatPipelineViewModel`) detects the kind from the JSON; Apply gives `ResultKind`, `ResultImage`, `ResultSheet`; `AutomationViewModel.OpenChatPipeline` runs the pipeline, adds the sprite as a layer (`AddSpriteLayer`, grows the canvas), or opens the sheet as an animation sheet (`OpenSheetDocument`: grid + `RsiBinding` with row names, frame names, delays).
- Required layout: `Core/SheetLayoutSpec.cs` (frame size, rows, frames with delays) from a template, the current sheet (`FromBinding`) or CLI text (`Parse`); `ChatScenePrompt.Sheet(layout)` states it as REQUIRED and builds a valid example for ANY layout (`ExampleSheet`, tested); `Differences` reports expected vs received, `Complete` fills the frame names and delays the answer omits (`SheetResult.ExplicitDelays` tells which were given). `ChatPipelineViewModel` (`ChatSheetSetup`) shows the layout selector only for the sheet kind and without a sheet document; with one, its layout is fixed and an answer of the same grid is drawn INTO it (`ResultFillsCurrent`, binding untouched).
- The new sheet / RSI state dialog (`RsiStateViewModel`): a template fills names and delays; the delay field then sets ALL frames (and rewrites the list), a hand-edited line wins for its frame, the frame count resizes the list, a typed delay survives choosing another template, the field is empty when delays differ (`DelayInfo`); last template and typed delay are in `AppSettings.LastSheet*`.
- CLI: `pixed draw-sheet <json> <out.png> [--meta f]`, `pixed chat-prompt --kind image|sprite|sheet`, `pixed pipeline-check` reports `Kind`. MCP: `draw_sheet`.

## Tuning operations (Operations → Other operation / tune parameters…)

- `ViewModels/OperationTunerViewModel.cs` + `OperationTunerDialog.xaml`: any registry operation with parameter rows (`ParamRow`, defaults prefilled when plain) and a live Before/After preview (debounced), applied through `EditorSession.ApplyResult`. `AutomationViewModel.TuneOperation("name|{json}")` opens it; the generic menu item passes no name (the last tuned operation, otherwise the first of the list), "Reduce number of colors…" starts on `quantize`, "Brightness and contrast…" on `adjust`, "Hue shift…" on `hueShift`, "Replace color…" on `replaceColor` (from = selected used color, else secondary; to = primary). "Clean up sprite" runs the built-in template found by its system id (`AutomationViewModel.CleanSpriteTemplateId`) and is hidden when the id is gone. The list shows `OperationChoice` entries (a short title from the first sentence of the localized description, then the technical name in brackets); `SelectedOperation` still selects by `OperationInfo`.
- `removeBackground` (`Core/Ops/Background.cs` + `BackgroundRemove.cs`, model in `BackgroundModel.cs`, metric in `Core/ColorMetric.cs`): the background is a few Lab clusters of the border ring (clusters under 15% of the ring are dropped so a subject touching the border is not taken for background); tolerance is automatic from the spread of the ring unless given (0 = exact RGB); distances are `ColorMetric` units (CIE76 x 2.5, about one RGB step on mid grays). The flood (`BackgroundFlood`) follows smooth changes (a step from the mean of the already accepted neighbors, `follow`, distance from the model <= `limit`) and is switched off by itself on a strongly textured border (grass, fur). After the flood: enclosed background (>= 3% of the picture, never on crisp pixel art), `Defringe` (antialiased blend pixels on the B..I Lab segment: erased below 50% subject share, repainted up to 92%) and tiny specks. Crisp = flat exact border and at most 24 colors: no rim cleanup, enclosed areas stay. An explicit `tolerance` without `follow` keeps the old "no following" mode.
- `cutout` distance mode uses the same model and flood and refits the model on the found background until the result stops changing (at most 4 rounds); a picture whose border is mostly transparent is returned unchanged, which is why a second run changes nothing.
- Measurement harness: `tests/PixelEditor.Tests/BackgroundBench.cs` (synthetic cases with ground truth, IoU table; `PIXED_BG_SHOTS=<folder> PIXED_BG_TAG=<name> dotnet test --filter BackgroundBenchmark` also writes before/after panels). Add a case there when tuning the defaults.
- The magic wand (`SelectionMask.Wand`) uses the same metric (tolerance 0 = exact, slider 0..128 keeps its meaning on plain grays) and has `follow` (step from the neighbor, at least 8, drift from the clicked color <= 5x the tolerance, at least 60). The Fill tool keeps its own per-channel rule (0..255, alpha included, also used by scenes and the CLI): the stored fill tolerance keeps its meaning.

## Documents

- `CONTRIBUTING.md` (Russian: `docs/ru/CONTRIBUTING.md`) and `.github/pull_request_template.md`: how to send pull requests; keep them in line with the rules in this file.
- `docs/AI-GUIDE.md`: how an AI uses the program through MCP, CLI and DLL without reading the project (keep it current when adding CLI commands and MCP tools; English only).
- `docs/AI-TOOLS-HOWTO.md`: how a person sets up and uses the AI tools.
- `docs/TOOLS_REVIEW.md`, `docs/IMPROVEMENTS.md`, `docs/RELEASE.md`: what exists, ideas, what is missing for a release.

## Planned (not done)

- Different row lengths in the animation sheet; a real tile mode; layer opacity (see `docs/TOOLS_REVIEW.md`).
- The mouse in the window is verified by hand (pointer logic is covered by tests through `PointerDown/Move/Up`, translation of coordinates in `MainWindow` only partly by the smoke test).
- A maximized window with the custom title compensates by `SystemParameters.WindowResizeBorderThickness`: check on screens with non-standard scaling.
