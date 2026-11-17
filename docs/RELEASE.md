# What is still missing for a release

State (version 1.0.0): the editor, CLI and MCP work; tests: core 143, view models 277, GUI smoke (needs a desktop).

Russian version: [ru/RELEASE.md](ru/RELEASE.md).
Priorities: **P0** blocks a release, **P1** should be done before announcing, **P2** can follow.

## P0: blockers

| # | What | Why |
|---|---|---|
| 1 | **Commit the work and settle the git workflow.** There is one commit ("git init"); about 58 files are uncommitted on `feature/ai-postprocessing-layers`. Branch switching in GitHub Desktop has already stashed the work twice | one slip loses weeks of changes; releases need tags |
| 2 | ~~License~~ **done:** GPL-3.0-or-later (`LICENSE`, SPDX headers, copyright "mrl4an"). Still check the sample photos in `examples/web` (CC0, see `SOURCES.txt`) | |
| 3 | ~~Versioning~~ **done:** 1.0.0 in `Directory.Build.props`, `pixed --version`, Help → About, MCP `serverInfo`. A changelog file is still missing | |
| 4 | **Packaging.** No publish profile or installer. Today the user needs the .NET 10 SDK. Needed: self-contained `win-x64` publish of `PixelEditor.exe`, `pixed.exe`, `pixed-mcp.exe` (WPF cannot be trimmed), a zip and ideally an installer (MSIX/Inno Setup) | nobody installs from source |
| 5 | **Verify by hand what automated tests cannot.** Mouse paths were never run on a real desktop in this session: stretch squares, rotation by the corner, Space-pan, right-click variant menus, language button, zoom right after creating an RSI state (reported once, not reproduced) | the GUI smoke test cannot simulate real mouse input |
| 6 | ~~Crash handling~~ **done:** global handlers write to the English error log (`%AppData%\PixelEditor\logs`), the user sees a message with the log path; AI reads it with `pixed log` / `read_log`. Still open: offering to restore the autosave right after a crash | |

## P1: before announcing

| # | What |
|---|---|
| 7 | **App icon** (the About window exists). No `ApplicationIcon`, no assets folder |
| 8 | **CI**: build + tests on every push (GitHub Actions on `windows-latest`); the GUI smoke test as an optional job |
| 9 | **Flaky GUI smoke test.** Two checks (wheel zoom, tool hotkey) failed in 1 of 3 runs because they depend on real focus and mouse position; make them deterministic or exclude them from CI |
| 10 | **User documentation**: a short manual with screenshots and a hotkey list (a hotkeys window is also on the improvement list, E15). Docs now exist in English and Russian (AI guide: English only); the AI how-to for people is done |
| 11 | **Localization finishing:** keys are machine-made slugs (`menu.cut_out_an_object_from`), rename them; status-bar messages already shown stay in the old language until the next event; names of user templates and palettes are not translated (by design) |
| 12 | **Settings migration:** `settings.json` gained many fields; define behavior for older and newer files (unknown fields are ignored today, never tested) |
| 13 | **Performance on large canvases** (2048+ px, many layers, undo of big operations): never measured; the rotate and stretch handles recompute from a snapshot on every mouse move |
| 14 | **High-DPI and multi-monitor check** (the window chrome compensates a resize border manually, see AGENTS.md "Что планируется") |
| 15 | **Security pass:** plugins run with full rights (documented), `fetch` downloads from the network (limited to image/*, 15 MB); review path handling in `batch`, `anim-export` and MCP `output` arguments |

## P2: product gaps (from docs/IMPROVEMENTS.md)

- Undo for layer rename, visibility and lock (E6); onion skin (E7); tile mode as a canvas mode (E8); HSV color picker (E9); reference image under the canvas (E10);
  different frame counts per row (E11); copy frame (E12); RSI overview and validation (E13, E14); hotkeys window (E15); recent colors (E16).
- Simplification ideas not done: merge Move and Selection into one tool, cut built-in templates from 11 to 6-7, three import presets, trim the Operations menu.
- Quality limits: `cutout` cannot separate a subject from a background of the same tone and texture (fox on snow): use `polygonMask`, or add a GrabCut-like
  segmentation; `cleanEdges` removes a halo only when `matte` is given; rotation of 1-2 px lines by an arbitrary angle is jagged (try RotSprite).
- AI tooling: `pixed` has no `--version`; `view_image` and `fetch_image` exist only in MCP; commands do not expand `*` patterns (`batch` covers folders).
- Code health: AGENTS.md is long (370+ lines) and should be split by area; the English table of core texts is keyed by the Russian source strings.

## Release checklist (when P0 and P1 are done)

1. `dotnet build -c Release`, all tests green, GUI smoke green on a clean machine.
2. `python scripts/audit_ai_access.py <publish folder>` prints "all reachable".
3. Self-contained publish, zip, checksum, installer; run on a clean Windows 11 VM without .NET.
4. Update version, changelog, README screenshots; tag the commit; publish the release notes.
