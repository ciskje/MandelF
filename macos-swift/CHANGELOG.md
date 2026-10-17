# CHANGELOG — macos-swift

## v2.24.2

- Version alignment (lockstep): same SPEC intent set as windows-c 2.24.2.
  No behavior change since 0.3.3.

## v0.3.1

- Benchmark chart: windows-c reference bars (CUDA/DirectX/CPU) + no-overlap
  layout (label gutter, values inside long bars, gridlines).
- CLI `--shot` headless PNG renderer; README with screenshots.

## v0.3.0

- Metal GPU engine (§4.1): float render + bench MSL, 88-byte params,
  LUT texture, autotune, single gate, CPU fallback.
- Genuine SIMD4 vector cores (bit-identical to scalar double).
- UI: engine CPU/GPU + device combo, GPU status, bench/export/video/log/CLI wired.
- Measured (MacBookAir M1): CPU double 0.3, GPU float 9.3 Mpixel/s.

## v0.2.1

- Fix pan direction; render at the view size (square pixels).

## v0.2.0

- Full SPEC CPU parity: 4 paths, LUT, SSAA, tiles, Julia, history,
  zones/favorites, settings, export, benchmark+CSV, video, back-to-set,
  log, about, CLI subset.

## v0.1.0

- Native SwiftUI viewer: CPU scalar renderer, drag pan, pinch zoom,
  iteration stepper, SelfCheck.
