# AGENTS.md — macos-swift (native SwiftUI, Apple Silicon)

Native SwiftUI port of the Mandelbrot viewer: Swift 6, macOS 14+, arm64.
Pure SwiftPM executable, no Xcode project (`import SwiftUI` for UI,
`import AppKit` only for the pixel-buffer bitmap).

## Build / run / test

```sh
cd macos-swift
export SCRATCH="$TMPDIR/mandel-swift-build"   # .build on SSD: repo lives on NAS
swift build --scratch-path "$SCRATCH"
swift run SelfCheck --scratch-path "$SCRATCH"  # escape-time asserts
swift run --scratch-path "$SCRATCH"           # opens the viewer window
```

## Versioning

- `0.2.0`. Single source: `Sources/MandelCore/AppVersion.swift`.
- Tag `macos-swift-vX.Y.Z` + `MandelSwift-X.Y.Z-arm64.zip` (release binary;
  `.app` bundle / dmg later).

## Scope notes (v0.2.0)

- Full SPEC CPU parity (§1–§3, §5–§8): 4 code paths, LUT, SSAA, tiles,
  Julia, history, zones/favorites, settings, export, benchmark+CSV,
  zoom video (ffmpeg or kept PNGs), back-to-set, log, about, CLI subset.
- GPU/Metal: pending (§4). Engine row shows CPU only; `--diag-gpu` /
  `--bench-gpu` report unavailability without crashing.
- Deviations: window bounds not restored (SwiftUI default 1152x720);
  benchmark chart shows session runs only (SPEC examples are other
  machines' values); no busy cursor (status text shows progress instead).
