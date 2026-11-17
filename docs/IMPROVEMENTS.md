# Improvement list: new features, simplifications, cuts

Scope: tools for AI (CLI, MCP, DLL), the editor with drawing, selection, layers, palettes, templates, RSI and animation export.
Ratings: **usefulness** (H high, M medium, L low) and **cost** (S small, up to 1 day; M medium, 2-4 days; L large, a week or more).

Русская версия: [ru/IMPROVEMENTS.md](ru/IMPROVEMENTS.md).

## 1. For agents (CLI, MCP, documentation)

| # | What | Use | Cost | Status |
|---|---|---|---|---|
| A1 | Compact catalogs: `pixed ops --names`, `pixed ops <name>`, `palettes <name>`, `info` with top colors | H | S | **done** |
| A2 | JSON from stdin or `@file` | H | S | open (JSON in a file works) |
| A3 | Path patterns (`slices\*.png`) in `sheet`, `gif`, `batch` | M | S | open |
| A4 | Uniform English error messages with a hint | M | S | **done** (English by default, `--lang ru`) |
| A5 | `pixed doctor`: version, tool path, .NET and WPF checks, write access, data folders | H | S | partly: `pixed --version`, `pixed log`, `scripts/audit_ai_access.py` |
| A6 | Save to library from the CLI: `save-template`, `save-palette`, `validate` | M | S | **done** |
| A7 | "Similar to the source" check and `pixed compare a.png b.png` | M | M | partly: `pixed compare` compares template variants |
| A8 | Text preview `pixed ascii <img>` | M | S | open |
| A9 | RSI commands: `pixed rsi new/state/info/export` | M | M | open |
| A10 | Scene 2.0 (offset, repeat, named parts, gradient, text) | M | M | partly (groups, offsets, radius added) |
| A11 | Single-file publish and a ready zip | H | S | open, see docs/RELEASE.md |
| A14 | Chat mode without agents: copy a prompt, paste the JSON answer, preview and apply; `chat-prompt`, `pipeline-check` | H | M | **done** |
| A12 | Batch scenes `pixed draw-many` | L | S | open |
| A13 | Error log for agents: `pixed log`, MCP `read_log` | H | S | **done** |

## 2. New in the editor

| # | What | Use | Cost | Status |
|---|---|---|---|---|
| E1 | Pixel-perfect freehand drawing | H | S | removed at the user's request |
| E2 | Fill with tolerance and whole-layer mode | H | S | **done** |
| E3 | Shade ramp from the primary color | H | S | open |
| E4 | Invert, grow and shrink selection | M | S | open |
| E5 | Space to pan and Fit | H | S | **done** |
| E6 | Undo for layer rename, visibility and lock | M | S | open |
| E7 | Onion skin in the animation sheet | H | M | open |
| E8 | Tile mode as a canvas mode | H | M | open |
| E9 | HSV color picker | H | M | open |
| E10 | Reference picture under the canvas | M | M | open |
| E11 | Different row lengths in the animation sheet | M | M | open |
| E12 | Copy a frame to the neighbor cell | H | S | open |
| E13 | RSI overview: all states, rename, duplicate, delete, `icon` state | M | M | open |
| E14 | RSI validation before saving | M | S | open |
| E15 | Hotkey window and rebinding | M | M | open |
| E16 | Recent colors above the palette | M | S | open |
| E17 | "As in the game" preview inside the import dialog | L | S | open |
| E18 | Stretch and rotate handles like in Paint | H | M | **done** |
| E19 | Remember the selected tool and its options; 64×64 default canvas | M | S | **done** |

## 3. Simplify and cut

| # | What | Result | Status |
|---|---|---|---|
| S1 | Group tools: selection (frame, wand, lasso, polygon) in one button, shapes in one | 15 buttons became 10 | **done** |
| S2 | Merge Move and Selection into one "Move" tool | no confusion after the auto-switch | open |
| S3 | Cut built-in templates from 11 to 6-7 | shorter list, fewer tests | open |
| S4 | Import: three presets instead of many options | readable dialog | open |
| S5 | Operations menu: keep 6-8 frequent items | less scary menu | partly (duplicates removed, Color and Shape submenus added) |
| S6 | Remove duplicate palette buttons | fewer places for the same thing | **done** |
| S7 | "Apply to all layers": move the switch into the result preview dialog | fewer accidental presses | open |
| S8 | Text and Lighter/darker into an "More" group | shorter main panel | open |
| S9 | Layers: add a "Merge into one" button and a first-use hint | clear where layers come from | open |
| S10 | Default palettes: keep 6, add "Rename" in the panel | order in the library | open |

## 4. Notes

- The user library may contain a junk palette from manual tests; it can be deleted in the palette library window.
- `AGENTS.md` is long (370+ lines) and should be split by area (map, GUI, core, RSI).
