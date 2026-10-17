# AGENTS.md — macos-swift (native SwiftUI, Apple Silicon)

Native SwiftUI port of the Mandelbrot viewer: Swift 6, macOS 14+, arm64.
Pure SwiftPM executable, no Xcode project (`import SwiftUI` for UI,
`import AppKit` only for the pixel-buffer bitmap).

## Build / run / test

```sh
cd macos-swift
export SCRATCH="$TMPDIR/mandel-swift-build"   # .build on SSD: repo lives on NAS
swift build --scratch-path "$SCRATCH"
swift run --scratch-path "$SCRATCH" SelfCheck  # SPEC spot checks (no Xcode needed)
swift run --scratch-path "$SCRATCH" MandelF    # opens the viewer window
```

## Versioning

- `7.3.1` (lockstep, see root AGENTS.md). Single source:
  `source/MandelCore/AppVersion.swift`.
- Tag `macos-swift-vX.Y.Z` + `MandelF-swift-X.Y.Z-arm64.dmg` (`.app` bundle +
  Applications symlink, ad-hoc signed; Apple Developer ID needed for
  notarized distribution).
- Publish: `packaging/make-app-dmg.sh <version>` (release build on SSD
  scratch, bundle, sign, DMG).

## Scope notes (v0.3.0)

- Full SPEC CPU parity (§1–§3, §5–§8): 4 code paths (genuine SIMD4 lanes),
  LUT, SSAA, tiles, Julia, history, zones/favorites, settings, export,
  benchmark+CSV, zoom video (ffmpeg or kept PNGs), back-to-set, log,
  about, CLI subset.
- Metal GPU engine (§4.1): float render + float bench MSL, 88-byte params,
  LUT texture (linear), 32xN autotune with 1.5% margin, single gate,
  CPU fallback. No FP64 on Apple Silicon: GPU mode forces 32-bit.
- UI: engine row CPU/GPU + device combo (Auto + adapters), GPU status
  (+ `[beyond float!]`), benchmark/export/video run on the active engine.
- CLI: `--diag-gpu` / `--bench-gpu` work headless; `--bench-cpu` unchanged.
- Measured (MacBookAir M1): CPU double 0.3 Mpixel/s, GPU float 9.3 Mpixel/s
  (pipelined 16 in-flight; chaotic-zone kernel is divergence-bound).
- Deviations: window bounds not restored (SwiftUI default 1152x720);
  benchmark chart shows session runs only (SPEC examples are other
  machines' values); no busy cursor (status text shows progress instead).
