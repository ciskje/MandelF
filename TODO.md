# TODO.md — change tracker

Every requested change is classified **first** by scope, then appended here
before implementing:

- `generic` — shared intent: implement in **every** environment (each on its own
  machine/build). The row is done only when all environments have it.
- `windows-c` / `macos-avalonia` — environment-only: implement (and rebuild the
  app) only where relevant; other environments ignore the row.
- Implement only for the current platform, update the row; Done rows are
  archived (deleted here) at each release — history lives in tags and
  release notes, not in this file.
- Generic intents are also mirrored in `AGENTS.md` / the SPEC file.

Legend per row: `c:[x]` / `mac:[x]` = shipped in that environment.

## Pending

- [ ] `macos-swift`: native SwiftUI port (CPU scalar renderer, drag pan,
  pinch zoom, iteration stepper).

- [ ] `macos-avalonia`: install .NET 8 SDK (arm64) on this Mac, then
  `dotnet build MandelbrotViewer` + `dotnet test MandelbrotViewer.Tests`.
- [ ] `generic`: create GitHub repo `ciskje/MandelF` and push `main`
  (needs explicit user request).

## Done

- [x] `generic`: monorepo bootstrap — `windows-c` imported from MandelC
  (history preserved, `c:[x]`), `macos-avalonia` copied from MandelMET
  (`mac:[x]`), root AGENTS/TODO/README/LICENSE/gitignore/gitattributes
  plus SPEC promoted to root.
