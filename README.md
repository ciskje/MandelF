# MandelF

Interactive Mandelbrot set viewer — **one repo, one native build per
platform and environment**, all implementing the same app.

| Environment | Stack | Platform |
|---|---|---|
| `windows-c/` | C# / WinForms / .NET 8 | Windows only |
| `macos-avalonia/` | C# / Avalonia 11 / .NET 8 | macOS arm64 |
| `macos-swift/` | native SwiftUI / Swift 6 | macOS 14+, Apple Silicon |

What is shared: the math, the behavior and the constants — the full
normative contract lives in `AGENTS.md` §1–§8 (rendering model, CPU/GPU
engines, main window, benchmark, export, video, settings). UI toolkit, GPU
API and packaging are per-environment choices; what binds them is the
intent set, the lockstep version (`X.Y.Z`) and the release process.

## Repo scheme

Every environment folder follows the same layout:

```text
<env>/
  AGENTS.md README.md CHANGELOG.md TODO.md   // docs
  source/          // code (source.tests/ for C# tests)
  packaging/      // Info.plist/.iss + make script (reproducible artifacts)
  dist/           // final artifacts (.app+dmg / setup.exe), regenerable
  screenshots/    // README images
```

Rules: build only for the local platform (`windows-c` builds on Windows,
`macos-*` on Mac); build outputs (`bin/`, `obj/`, `.build/`, `published/`,
`dist/`) are gitignored and regenerated; tags carry the env prefix
(`windows-c-vX.Y.Z`, `macos-avalonia-vX.Y.Z`, `macos-swift-vX.Y.Z`);
release notes in English. Details in `AGENTS.md`.

## Build

```powershell
# windows-c (Windows, .NET 8 SDK)
dotnet build "windows-c\source\MandelbrotViewer.csproj"
```

```sh
# macos-avalonia (macOS, .NET 8 SDK)
cd macos-avalonia
export PATH="$HOME/.dotnet:$PATH"
dotnet build source && dotnet test source.tests
./packaging/make-app-dmg.sh <version>   # .app + DMG in dist/
```

```sh
# macos-swift (macOS, Xcode CLT Swift 6)
cd macos-swift
export SCRATCH="$TMPDIR/mandel-swift-build"   # SSD: repo lives on NAS
swift build --scratch-path "$SCRATCH"
swift run --scratch-path "$SCRATCH" SelfCheck  # SPEC assertions
swift run --scratch-path "$SCRATCH" MandelSwift # viewer window
./packaging/make-app-dmg.sh <version>   # .app + DMG in dist/
```

## Releases

One channel per environment (see `AGENTS.md` Tags & releases); keep the
last 2 per environment:

- `windows-c-vX.Y.Z` + `MandelbrotViewer-Setup-X.Y.Z-win-x64.exe`
- `macos-avalonia-vX.Y.Z` + `MandelbrotViewer-X.Y.Z-arm64.dmg`
- `macos-swift-vX.Y.Z` + `MandelSwift-X.Y.Z-arm64.dmg`

## License

MIT — see `LICENSE`.
