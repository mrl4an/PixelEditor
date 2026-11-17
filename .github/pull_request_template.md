## What and why

<!-- Describe the change and the problem it solves. Screenshots help for visible changes. -->

## How it was checked

- [ ] `dotnet build` and both test projects pass
- [ ] Added or updated tests (a new operation also has a sample in `OperationsCatalogTests.Samples()`)
- [ ] Interface texts are in both `ru.json` and `en.json`; core texts use `CoreText` with an English translation
- [ ] Docs updated in English and Russian (the AI guide is English only)
- [ ] `python scripts/audit_ai_access.py` ends with "all reachable" (if operations, commands or tools changed)
- [ ] New `.cs` files have the SPDX/copyright header; I agree my contribution is released under GPL-3.0-or-later
