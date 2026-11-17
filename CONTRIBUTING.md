# Contributing

Pixel Editor is an **actively developed open-source project**, and **pull requests are welcome**: bug fixes, new operations, tools, translations, documentation, examples.
Русская версия: [docs/ru/CONTRIBUTING.md](docs/ru/CONTRIBUTING.md).

## Where to start

- Ideas and known gaps: [docs/IMPROVEMENTS.md](docs/IMPROVEMENTS.md), [docs/TOOLS_REVIEW.md](docs/TOOLS_REVIEW.md), and [docs/RELEASE.md](docs/RELEASE.md) (what is still missing for a release).
- Small, self-contained items are the easiest first pull requests: a new operation, a hotkey window, recent colors, undo for layer properties, a new language file.
- Not sure whether an idea fits? Open an issue first and describe the use case.

## How to send a pull request

1. Fork the repository and create a branch from `main` (one topic per branch).
2. Build and run the tests (Windows, .NET 10 SDK). `build.cmd --test` does a fresh build plus both test projects; the commands below are the manual way:

   ```
   dotnet build
   dotnet test tests/PixelEditor.Tests
   dotnet test tests/PixelEditor.ViewModels.Tests
   dotnet run --project tests/PixelEditor.GuiSmoke     # opens a window, needs a desktop
   ```

3. Make your change following the rules below, add tests, push, and open the pull request. Describe **what** changed, **why**, and how you checked it (screenshots for visible changes).

## Rules of the project

- **Logic belongs to `PixelEditor.Core` and the view models**, not to code-behind. The map and the GUI rules are in [AGENTS.md](AGENTS.md); read the section you touch.
- **Tests:** every behavior change needs a test. A new operation also needs a sample in `OperationsCatalogTests.Samples()`; the test runs every operation on a real picture.
- **Texts:** interface strings go through localization keys in **both** `ru.json` and `en.json` (`{loc:T key}` in XAML, `Loc.T` in models). Texts of the core, CLI and MCP use `CoreText.T/F` with an English translation in `core.en.json`.
  Log messages are **English only** (`Log.Error(...)`); show errors with `dialogs.ShowError(ex)` so they are logged.
- **Documentation:** user-visible changes update the docs in both languages (English in the root and `docs/`, Russian in `docs/ru/`; the AI guide `docs/AI-GUIDE.md` is English only). New CLI commands and MCP tools go into the AI guide.
- **AI access:** after adding an operation, command or tool run `python scripts/audit_ai_access.py`; it must end with "all reachable".
- **Style:** match the surrounding code; colors only as theme tokens; no new dependencies without a discussion.
- **Files:** every new `.cs` file starts with the two-line header

  ```csharp
  // SPDX-License-Identifier: GPL-3.0-or-later
  // Copyright (C) 2026 mrl4an
  ```

  (keep the original header when you edit a file; you may add your own copyright line for substantial work).

## License of contributions

The project is licensed under **GPL-3.0-or-later** (see [LICENSE](LICENSE)). By sending a pull request you agree that your contribution is released under the same license
and that you have the right to contribute it (do not copy code or assets with an incompatible license; sample pictures must be CC0 or public domain with the source recorded).

## Reporting bugs

Open an issue with: the version (**Help → About** or `pixed --version`), what you did, what you expected and what happened.
Attach the relevant lines of the error log (**Help → Open error log folder**; the entries are English and include the stack trace). The log contains file paths, so remove private ones.

## Be kind

Be respectful and constructive in issues and reviews. Maintainers may ask for changes or decline a pull request that does not fit the project's goals: lightweight, convenient, binary-transparency pixel art with strong automation for AI.
