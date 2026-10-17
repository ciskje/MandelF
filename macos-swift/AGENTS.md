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

- `0.1.0`. Single source: `Sources/MandelSwift/AppVersion.swift`.
- Tag `macos-swift-vX.Y.Z` + `MandelSwift-X.Y.Z-arm64.zip` (release binary;
  `.app` bundle / dmg later).

## Scope notes (v0.1.0)

- CPU scalar renderer, flat per-iteration sine palette (no smooth coloring yet).
- Gestures: drag = pan, pinch = zoom to center, double-click / Reset = home.
- Live gestures render a 1/3 preview, full-res on release (keeps drag smooth).
