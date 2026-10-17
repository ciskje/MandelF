# TODO.md — change tracker

Every requested change is classified **first** by scope, then appended here
before implementing:

- `generic` — shared intent: implement in **every** environment (each on its own
  machine/build). The row is done only when all environments have it.
- `windows-c` / `macos-avalonia` / `macos-swift` — environment-only: implement
  (and rebuild the app) only where relevant; others ignore the row.
- Implement only for the current platform, update the row; Done rows are
  archived (deleted here) at each release — history lives in tags and
  release notes, not in this file.
- Generic intents are also mirrored in `AGENTS.md` / the SPEC file.

Legend per row: `win-c:[x]` / `mac-avalonia:[x]` / `mac-swift:[x]` = shipped
in that environment.

## Pending

- [ ] `macos-swift`: feature parity with siblings (palette, auto-iter, history,
  settings, export, benchmark, video — prioritize below).

- [ ] `macos-avalonia`: install .NET 8 SDK (arm64) on this Mac, then
  `dotnet build MandelbrotViewer` + `dotnet test MandelbrotViewer.Tests`.
- [ ] `generic`: create GitHub repo `ciskje/MandelF` and push `main`
  (needs explicit user request).

## Done

- [x] `generic`: monorepo bootstrap — `windows-c` imported from MandelC
  (history preserved), `macos-avalonia` copied from MandelMET, root
  AGENTS/TODO/README/LICENSE/gitignore/gitattributes plus SPEC to root.
- [x] `macos-swift`: native SwiftUI port v0.1.0 (CPU scalar, pan/zoom/iter,
  SelfCheck green, release zip in `macos-swift/dist/`).
