# AGENTS.md — MandelF (windows-c + macos-avalonia)

One repo, two native builds of the same Mandelbrot viewer:

- `windows-c/` — C# / WinForms / .NET 8, Windows only (imported from MandelC,
  history preserved).
- `macos-avalonia/` — C# / Avalonia 11 / .NET 8, macOS arm64 (copied from the
  unversioned MandelMET folder).
- `macos-swift/` — native SwiftUI / Swift 6, macOS 14+, Apple Silicon.

Normative math/render contracts: `SPEC_FOR_LLM_PLATFORM_AGNOSTIC.md` (root).
Platform details (build, gotchas, paths) live in `windows-c/AGENTS.md` and
`macos-avalonia/AGENTS.md`. This file holds only what is shared: workflow,
versioning and releases. Build on each machine **only for its own platform(s)**.

## Change workflow

1. A change request updates the shared spec/intents first (here or the SPEC file).
2. Implement **only for the local platform**; the other platform implements
   the same intent later on its own machine.
3. Same rule for fixes: if the bug is in a shared intent, mirror the fix.

## TODO.md (classify first)

On every change request, first decide the scope — `generic`, `windows-c`,
`macos-avalonia` or `macos-swift` — and append a row to `TODO.md` with the platform note
before implementing:

- `generic`: implement locally now; the row stays open until every environment
  has it (next build on another platform picks it up from TODO.md).
- environment-only: implement and rebuild the app locally; others ignore it.
- Mark the local environment done in the row; mirror generic intents here too.
- Done rows are archived (deleted here) at each release; history lives in
  tags and release notes, not in TODO.md.

Legend per row: `win-c:[x]` / `mac-avalonia:[x]` / `mac-swiftui:[x]` = shipped
in that environment.

## Versioning (independent per environment)

Each environment versions independently (`X.Y.Z` = its own intent set):

- Z = bugfix/tweak, Y = new feature/behavior, X = upheaval.
- Docs/refactor/build never change the version.
- Current: `windows-c` `2.24.2`, `macos-avalonia` `2.22.7`, `macos-swift` `0.1.0`.
- Single source per environment: `<Version>` in its `.csproj`
  (`windows-c` also mirrors it into `AppVersion.cs`; the two must match).
  `macos-swift` uses `Sources/MandelSwift/AppVersion.swift`.

## Tags & releases

One channel per environment, tags always carry the environment prefix:

- `windows-c`: tag `windows-c-vX.Y.Z` + `MandelbrotViewer-Setup-X.Y.Z-win-x64.exe`
  (Inno Setup, per-machine install). Command in `windows-c/AGENTS.md`.
- `macos-avalonia`: tag `macos-avalonia-vX.Y.Z` + `MandelbrotViewer-X.Y.Z-arm64.dmg`.
  Command in `macos-avalonia/AGENTS.md`.
- `macos-swift`: tag `macos-swift-vX.Y.Z` + `MandelSwift-X.Y.Z-arm64.zip`
  (release binary; `.app` bundle / dmg later). Command in `macos-swift/AGENTS.md`.
- Every version bump is published (tag + release with artifact), otherwise
  Latest lags behind the code. Keep the **last 2 releases per environment**.
- Release notes **always in English**.

## "Generate the application" (build for the current environment)

"Generate the application" always means: build **only for the platform(s) of the
machine the agent runs on**, never the other one(s). Commands in
`windows-c/AGENTS.md` (Build/run) and `macos-avalonia/AGENTS.md` (Build/run).
Don't touch the version (except releases), don't publish
to GitHub unless explicitly requested.

## Source language

Source everything in English: comments, docs, identifiers, commit messages.
Exception: UI strings through the platform loader where localized.

## Line endings

`* text=auto eol=lf` everywhere (LF working copies on all platforms —
the Mac builds from this repo too); only `*.sh` → LF and `*.bat` → CRLF
are enforced explicitly.
