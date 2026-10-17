# MandelF

Interactive Mandelbrot set viewer, one repo with one folder per environment:

- `windows-c/` — C# / WinForms / .NET 8, Windows only (v2.24.2).
- `macos-avalonia/` — C# / Avalonia 11 / .NET 8, macOS arm64 (v2.22.7).
- `macos-swift/` — native SwiftUI / Swift 6, macOS 14+, Apple Silicon (v0.2.0).

Shared math/render contracts: `AGENTS.md` (§1–§8).
Workflow, versioning and releases: `AGENTS.md`. Change tracker: `TODO.md`.

## Provenance

- `windows-c/` imported from `github.com/ciskje/MandelC` with full history
  (`git log -- windows-c/`).
- `macos-avalonia/` copied from the unversioned `MandelMET` folder; its prior
  changelog continues in `macos-avalonia/CHANGELOG.md`.
- `macos-swift/` is new in this repo (v0.1.0, no prior history).

Build only for the local platform — see the environment's own `AGENTS.md`.

## Releases

One channel per environment; tags always carry the environment prefix:

- `windows-c`: tag `windows-c-vX.Y.Z` + `MandelbrotViewer-Setup-X.Y.Z-win-x64.exe`
  (per-machine install in Program Files).
- `macos-avalonia`: tag `macos-avalonia-vX.Y.Z` + `MandelbrotViewer-X.Y.Z-arm64.dmg`
  (drag into Applications).
- `macos-swift`: tag `macos-swift-vX.Y.Z` + `MandelSwift-X.Y.Z-arm64.zip`
  (release binary).
- Keep the last 2 releases per environment. Release notes always in English.
