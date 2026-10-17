# CHANGELOG — MandelC

Versioning `X.Y.Z` (if `Z` is 0, short notation `X.Y`). Bump rules in
`AGENTS.md`. The version is shown in the window title. Entries are grouped
by minor series below, newest first.
## v7.3.x — MandelF lockstep
- **v7.3.1** — Ponytail-audit refactor, no behavior change: removed dead code,
  shared duplicated row/tail helpers, unified short-name/warmup/dialog
  helpers, dropped the `CommunityToolkit.Mvvm` dependency.
  Verified: clean build (0 warnings, 0 errors).
  Files: `Mandelbrot.cs`, `Palette.cs`, `MandelbrotForm.cs`,
  `BenchmarkForm.cs`, `CudaNative.cs`, `DxMandelbrot.cs`, `ExportForm.cs`,
  `TODO.md`, `CHANGELOG.md`, `AppVersion.cs`, `.csproj`.
- **v7.3.0** — Version alignment with MandelF monorepo (MandelPy 7.3.0 line);
  executable renamed `MandelF.exe`, installer `MandelF-Setup-7.3.0.exe`.
  No behavior change vs v2.24.2.
## v2.24.x — AVX512 CPU kernel
- **v2.24.2** — CPU history bars in the benchmark chart now say `AVX512`
  (`CPU 9900X AVX512`, `CPU 9900X float AVX512`; label gutter widened to
  fit), so the chart itself records how those bars were measured.
  Verified: clean build + re-captured benchmark screenshot.
  Files: `BenchmarkForm.cs`, `TODO.md`, `README.md`, `CHANGELOG.md`,
  `AppVersion.cs`, `.csproj`, `screenshots/dialog-benchmark.png`.
- **v2.24.1** — The active CPU SIMD path is now visible everywhere:
  new `Mandelbrot.CpuSimdLabel` (`AVX512` / `AVX2` / `SSE2` / `scalar`,
  single source with the dispatch flag `CpuUseAvx512`) shown in the main
  status bar (`CPU-double AVX512`), About/log `In use` line, diagnostics
  log (`CPU SIMD:`), benchmark/export/video labels and the `--bench-cpu`
  header (`AMD Ryzen 9 9900X [AVX512]`). Verified: clean build, CLI header
  shows `[AVX512]`, benchmark still 13.9–14 MPixel/s double.
  Files: `Mandelbrot.cs`, `MandelbrotForm.cs`, `BenchmarkForm.cs`,
  `ExportForm.cs`, `ZoomVideoForm.cs`, `Diagnostics.cs`, `TODO.md`,
  `SPECS.md`, `CHANGELOG.md`, `AppVersion.cs`, `.csproj`.
- **v2.24.0** — Native AVX512 CPU kernel for Zen5: new `Vector512`
  escape cores (`EscapeBlockDouble512` 8 lanes, `EscapeBlockFloat512`
  16 lanes) + matching row and benchmark blocks, dispatched when
  `Avx512F` is supported with fallback to the 256-bit path. Same op order
  and lane formulas, so double stays bit-identical to scalar (verified:
  0 differing pixels vs 256-bit on Mandelbrot/Julia, AA1/AA2, tile offsets;
  benchmark row sums identical at full 10915 iterations). A/B best-of-3
  Release on Ryzen 9 9900X vs v2.22.8 CPU core: double 9.7 → 13.8 (+42%),
  float 15.2 → 21.3 (+40%) — above the +20% keep gate. History bars
  remeasured. Implementation note: `Vector512<T>` has no public
  constructors in .NET 8 — broadcast via `Vector512.Create(x)`, span loads
  via `Vector512.LoadUnsafe(ref span[0])`.
  Files: `Mandelbrot.cs`, `BenchmarkForm.cs`, `TODO.md`, `SPECS.md`,
  `SPEC_FOR_LLM.md`, `SPEC_FOR_LLM_PLATFORM_AGNOSTIC.md`, `README.md`,
  `CHANGELOG.md`, `AppVersion.cs`, `.csproj`.
## v2.23.x — CPU-only SIMD (Rust removed)
- **v2.23.0** — Removed the Rust engine: deleted `RustMandelbrot.cs`
  (`mandel_ffi.dll` FFI), the `RenderEngine.Rust` value, the toolbar radio,
  and all view/export/video/benchmark branches (saved `Engine=Rust` falls
  back to CPU). `publish.ps1` no longer ships the DLL; README updated to
  three engines. Reason: single C# SIMD core is easier to evolve toward a
  native AVX512 Zen5 kernel (see TODO plan). Verified: clean build
  (0 warnings, 0 errors).
  Files: `RustMandelbrot.cs` (deleted), `RenderEngine.cs`,
  `MandelbrotForm.cs`, `MandelbrotForm.Designer.cs`, `BenchmarkForm.cs`,
  `ExportForm.cs`, `ZoomVideoForm.cs`, `publish.ps1`, `README.md`,
  `TODO.md`, `SPECS.md`, `CHANGELOG.md`, `AppVersion.cs`, `.csproj`.
## v2.22.x — Native CUDA backend
- **v2.22.8** — Rust SIMD engine selectable in the toolbar (`RustMandelbrot.cs`
  via `mandel_ffi.dll` from MandelR's `mandel-core`: same formulas, AVX512/AVX2
  dispatch; view/export/video/benchmark + CPU fallback; installer bundles the DLL).
  Ponytail-audit cleanup, no behavior change: deleted dead
  `Mandelbrot.ColorFromEscape` + `PaletteColors.ColorFor` (zero callers),
  dropped the ignored `maxIter` from the palette-table cache key (`GetLut` /
  both `EnsureLut`, fields removed), merged the CPU double/float benchmark
  frame loops into one `RunCpuBenchmark` helper (scalar bodies extracted to
  row functions), merged the three CLI trial loops into `Diagnostics.RunTrials`,
  replaced hand-rolled `CopyTile` (LockBits + MemoryCopy) with
  `Graphics.DrawImageUnscaled`, removed alias/wrapper one-liners (BenchmarkForm
  `BW…Budget` constants, `PixelsPerSecond` forwarder, `EffectiveAa`, stored
  `_useCpu` flag → property, preview overload forwarder → optional params,
  triple short-name strip → `GpuMandelbrot.Shorten`). Kept `SPEC_FOR_LLM.md`:
  audit-flagged docs duplication rejected, it is the Windows-specific contract
  doc with a distinct purpose from the platform-agnostic spec. Verified: clean
  build (0 warnings), `--diag-gpu` CUDA render OK, `--bench-cpu float`
  2.4 MPixel/s (9900X). Launcher fixes: `run.bat` uses PATH dotnet with
  user-path fallback (was hardcoded), new `setup.bat` (fresh publish +
  ISCC compile), same fallback in `publish.ps1`, AGENTS.md toolchain line.
  Files: `Mandelbrot.cs`, `Palette.cs`, `Diagnostics.cs`, `DxMandelbrot.cs`,
  `GpuMandelbrot.cs`, `BenchmarkForm.cs`, `ExportForm.cs`, `ZoomVideoForm.cs`,
  `run.bat`, `setup.bat`, `publish.ps1`, `AGENTS.md`,
  `TODO.md`, `SPECS.md`, `CHANGELOG.md`, `AppVersion.cs`, `.csproj`.
- **v2.22.7** — DirectX 5070 Ti history bar 322.4 → 338 (remeasured best of
  3, Release: 338.5; 4070 SUPER bar kept at 262.4, remeasured 262.2).
  Files: `BenchmarkForm.cs`, `TODO.md`, `CHANGELOG.md`, `AppVersion.cs`,
  `.csproj`.
- **v2.22.6** — Render kernels on the 2D grid too (same thread-to-pixel
  mapping as the benchmark kernels; no index division anywhere; the render
  path shares the tuned 32x8/32x4 layout, dead 1D `GridFor` removed).
  End-to-end A/B (median of repeats, incl. device-to-host copy) shows CUDA
  float render at or above DirectX on every scene (5070 Ti: fullset 960x540
  0.3 vs 0.7 ms, AA4 1.5 vs 1.4, deep 2.0 vs 2.5, Julia 0.3 vs 0.8, deep
  1080p 6.8 vs 7.8, deep 1080p AA4 97.8 vs 101.1). Regrouping is bit-identical
  by construction (pure function of x/y; verified: same CPU-diff counts
  before/after on all scenes). Double render is FP64-hardware-bound on
  GeForce (deep 960x540: CUDA 79 vs CPU 57 ms) — no kernel change can fix
  that; left untouched. Float-LUT and fast-math render variants skipped: no
  measurable headroom left. PTX regenerated with NVRTC 13.3.
  Files: `Cuda/mandelbrot.cu` + `mandelbrot.ptx`, `CudaNative.cs`,
  `GpuMandelbrot.cs`, `TODO.md`, `SPECS.md`, `QUESTIONS.md`,
  `CHANGELOG.md`, `AppVersion.cs`, `.csproj`.
- **v2.22.5** — CUDA benchmark kernels on a 2D grid (thread (x, y) maps
  directly to sample (x, y), no integer division/modulo per thread; 32-wide
  blocks keep warps on single rows, fully coalesced). C# side launches 2D
  (`CudaNative.LaunchBench` takes grid/block per axis) and the init probe
  sweeps 32x4/32x8/32x16/32x32 (same thread counts as before; fresh processes
  converge on 32x8 float / 32x4 double). Measured best-of-3, Release:
  5070 Ti 310.7 (+2-3% vs 301.7) and 4070 SUPER 250.1 (+1.5% vs 247.1);
  64-bit unchanged within frame-quantization noise (bars kept at 6.7/5.3).
  Rejected after A/B (same protocol): 4x loop unroll (−3.5%, divergence
  punishes coarser exit granularity), half-pixel bench sampling (−1.2%),
  HLSL-style loop form (+0.5%, under threshold), `--use_fast_math` (−0.4%).
  Evidence the rest of the gap is not our overhead: Nsight Systems shows
  kernel time == wall time (submit fully hidden by batching), ptxas shows no
  spills at 14/22 regs, SM clocks identical under both engines (~2.83 GHz).
  PTX regenerated with NVRTC 13.3 (no MSVC on the build machine, `nvcc`
  unusable; output byte-identical to the previous nvcc-built PTX for the same
  source). Bonus: `--diag-gpu` file now includes the tuned block layout.
  Files: `Cuda/mandelbrot.cu` + `mandelbrot.ptx`, `CudaNative.cs`,
  `GpuMandelbrot.cs`, `Diagnostics.cs`, `BenchmarkForm.cs`, `TODO.md`,
  `SPECS.md`, `QUESTIONS.md`, `CHANGELOG.md`, `AppVersion.cs`, `.csproj`.
- **v2.22.4** — Program icon replaced with `icon4.png`, converted to a
  multiresolution `.ico` 16/32/48/256. Files: `app.ico`, `AppVersion.cs`,
  `.csproj`.
- **v2.22.3** — Fixed flaky autotune picking a slow block size on the first
  run (the small cold-clock probe ranked a starved grid and chose 1024,
  scoring 246 instead of ~300 on the 5070 Ti until the GPU was reselected).
  The probe now warms up, runs on the real benchmark grid at reduced
  iterations, interleaves candidates (median of 5), and keeps 256 without a
  clear margin; fresh processes converge on 256/256 and ~300 MPixel/s.
  Bonus: the benchmark result line now names the device that actually ran
  (CUDA/DirectX/CPU). History bars remeasured: CUDA 5070 Ti 301.7/6.7 and
  4070 SUPER 247.1/5.3. Files: `GpuMandelbrot.cs`, `BenchmarkForm.cs`,
  `TODO.md`, `SPECS.md`, `CHANGELOG.md`, `AppVersion.cs`, `.csproj`.
- **v2.22.2** — FMA contraction enabled in the CUDA PTX (both precisions,
  plain operators everywhere): 32-bit gains little (−1.3% measured cost of
  disabling it), 64-bit gains ~15% (5070 Ti 5.8 → 6.7). Trade-off vs CPU
  bit-identity accepted by design: ≤0.7% boundary pixels differ on all
  scenes except 32-bit deep zoom (~12% sparse flips from chaos
  amplification, inside that path's documented imprecision). History bars
  remeasured (best of 3, Release): CUDA 5070 Ti 293.7/6.7 and 4070 SUPER
  246.2/5.3. Files: `Cuda/mandelbrot.cu`, `Cuda/mandelbrot.ptx`,
  `BenchmarkForm.cs`, `TODO.md`, `SPECS.md`, `QUESTIONS.md`,
  `CHANGELOG.md`, `AppVersion.cs`, `.csproj`.
- **v2.22.1** — Benchmark history refresh (best of 3, Release, same session):
  CUDA 5070 Ti 291.3/5.8 and 4070 SUPER 230.1/4.6 (32/64-bit), DirectX 5070 Ti
  322.4, 4070 SUPER 262.4 and Radeon 5.2, CPU 9900X 9.7 double / 15.3 float
  (unchanged). No code change besides the reference table. Files:
  `BenchmarkForm.cs`, `TODO.md`, `CHANGELOG.md`, `AppVersion.cs`, `.csproj`.
- **v2.22.0** — Native CUDA backend replacing ILGPU: the four kernels live
  in `Cuda/mandelbrot.cu`, compile to PTX at build time (`nvcc`, embedded
  resource, regenerable; checked-in PTX keeps toolkit-less builds working),
  and run through a minimal `nvcuda.dll` driver-API binding (`CudaNative.cs`,
  no NuGet wrapper). Explicit wave allocation: `grid = ceil(N/block)` with a
  per-precision init probe over 128/256/512/1024 threads per block
  (measured on RTX 5070 Ti: 512 float / 128 double; both saturate the SMs).
  PTX uses `--fmad=false` so rational arithmetic matches the CPU bit for
  bit. Bonus fix found by the new pixel-diff harness: the old backend
  smoothed with `log2(0.5*log2(|z|²))` instead of the correct
  `log2(0.5*ln(|z|²))`, shifting every exterior color; native output is now
  pixel-identical to the CPU in float/double, Mandelbrot/Julia, AA1x/AA2x
  and tiled offsets (0 diffs on all scenes). Benchmark history remeasured
  (best of 3, Release): CUDA 5070 Ti 289.8/5.8 and 4070 SUPER 230.2/4.6
  (32/64-bit; float slightly above the ILGPU values, double lower due to no
  FMA). Files: `Cuda/mandelbrot.cu` + `mandelbrot.ptx` (new),
  `CudaNative.cs` (new), `GpuMandelbrot.cs`, `MandelbrotViewer.csproj`,
  `BenchmarkForm.cs`, `Palette.cs`, `Mandelbrot.cs`, `MandelbrotForm.cs`,
  `MandelbrotForm.Designer.cs`, `RenderEngine.cs`, `TODO.md`, `SPECS.md`,
  `README.md`, `CHANGELOG.md`, `AppVersion.cs`.
## v2.21.x — CPU precision setting
- **v2.21.0** — CPU honors the 32/64-bit setting like the other engines, with no
  fallback: 64-bit renders everything in double, 32-bit in float (Mandelbrot and
  Julia, any zoom; less precise at deep zoom, by user choice). Precision radios
  enabled for CPU in the main window and both Generate dialogs; status bar shows
  `CPU-double`/`CPU-float`; benchmark window follows the setting for CPU too.
  Home button first in the toolbar row with a return arrow plus "Home" text.
  Verified with `dotnet build` (0 warnings/errors) and a headless differential
  test (double scenes bit-exact, float scenes bit-exact vs scalar float,
  tile-identity in all combinations). Files: `Mandelbrot.cs`,
  `MandelbrotForm.cs`, `MandelbrotForm.Designer.cs`, `ExportForm.cs`,
  `ZoomVideoForm.cs`, `BenchmarkForm.cs`, `TODO.md`, `SPECS.md`,
  `CHANGELOG.md`, `AppVersion.cs`, `.csproj`.
## v2.20.x — GPU render fast paths
- **v2.20.1** — GPU render fast paths (WP3): palette lookup through a cached
  4096-entry table on all engines (CUDA device table with lerp, DirectX texture
  with hardware linear filtering; single palette definition, no per-pixel Pow or
  stop search) plus the cardioid/bulb early-out in the CUDA and DirectX render
  kernels (Mandelbrot only; benchmark kernels untouched, history unchanged).
  Measured best-of-5 on RTX 5070 Ti, output within 1 LSB of baseline on all
  scenes: CUDA interior-heavy 1080p view 166 → 12 ms (~14x), DirectX same view
  5 → 2 ms (~2.5x); loop-bound deep views and overhead-floor small views
  unchanged. Files: `GpuMandelbrot.cs`, `DxMandelbrot.cs`, `Palette.cs`,
  `TODO.md`, `SPECS.md`, `CHANGELOG.md`, `AppVersion.cs`, `.csproj`.
- **v2.20.0** — CPU float benchmark mode: `--bench-cpu float` runs the
  standardized workload through the float vector core (own history, like CUDA
  32-bit vs 64-bit; default stays double, which is also what the benchmark window
  runs). New `Mandelbrot.BenchmarkCpuFloat` (float vector blocks + scalar tail,
  verified digit-exact against a scalar reference over 900 frames). History
  measured with the new mode (best of 3, Release): CPU 9900X float 15.3 MPixel/s
  (vs 9.7 double). Files: `Mandelbrot.cs`, `Diagnostics.cs`, `Program.cs`,
  `BenchmarkForm.cs`, `TODO.md`, `SPECS.md`, `CHANGELOG.md`, `AppVersion.cs`,
  `.csproj`.
## v2.19.x — CPU fast paths (LUT, SIMD, float)
- **v2.19.4** — CPU SIMD + float fast path (WP2): `System.Numerics` vector escape
  cores (double and float) with per-lane op order identical to scalar code, chunk
  row partitioning across all CPU paths and the benchmark. SIMD double is
  bit-identical to scalar (differential test vs reference: 0 diffs; tiled output
  still pixel-identical to whole-frame). SIMD float applies to Mandelbrot views at
  scale ≥ 1e-3 (same boundary as CUDA double-precision need): 0.1% boundary pixels
  differ there, none at deeper zooms. CPU benchmark remeasured with the vector
  core (best of 3, Release): CPU 9900X 4.8 → 9.7 MPixel/s (~2x); CUDA/DirectX
  references unchanged. Files: `Mandelbrot.cs`, `BenchmarkForm.cs`, `TODO.md`,
  `SPECS.md`, `CHANGELOG.md`, `AppVersion.cs`, `.csproj`.
- **v2.19.3** — CPU banding fix: the palette table grows to 4096 entries with
  linear interpolation on lookup (`PaletteColors.ColorFromLut`, used by the render
  loop), so output matches the legacy exact coloring within 1 LSB (verified with a
  headless differential test vs `ColorFromEscape` on full, zoomed, deep-zoom and
  Julia scenes: max channel difference 1, zero pixels above it). Files:
  `Palette.cs`, `Mandelbrot.cs`, `TODO.md`, `SPECS.md`, `CHANGELOG.md`,
  `AppVersion.cs`, `.csproj`.
- **v2.19.2** — CPU render fast path (WP1): 1024-entry palette LUT (no per-pixel
  `Pow`/stop search), fast smooth `log2(0.5*ln(mod2))` without `Sqrt`, main
  cardioid + period-2 bulb early-out in render and CPU benchmark, hoisted palette
  data, direct unsafe bitmap writes (no intermediate buffer or `Marshal.Copy`),
  `Buffer.MemoryCopy` tile assembly, `AllowUnsafeBlocks` + `InvariantGlobalization`.
  Slight color quantization possible (speed favored over exactness). CPU benchmark
  history remeasured with the new workload (best of 3, Release): CPU 9900X 4.8;
  CUDA/DirectX kernels untouched, their references unchanged. Verified with
  `dotnet build` (0 warnings/errors), `--bench-cpu` and a headless render smoke
  test (160x90 + tile + Julia). Files: `Mandelbrot.cs`, `Palette.cs`,
  `ExportForm.cs`, `BenchmarkForm.cs`, `TODO.md`, `SPECS.md`, `CHANGELOG.md`,
  `AppVersion.cs`, `.csproj`.
- **v2.19.1** — Full English pass on the remaining Italian UI strings and
  comments (Export/Benchmark dialogs, log window, diagnostic log, settings,
  CLI-adjacent comments) plus English identifiers (`vista` → `viewMenu`,
  `migliori` → `bestPerCard`/`bestPerDevice`). No behavior change. Files:
  `ExportForm.cs`, `ExportForm.Designer.cs`, `ZoomVideoForm.Designer.cs`,
  `BenchmarkForm.cs`, `BenchmarkForm.Designer.cs`, `BenchmarkCsv.cs`,
  `BenchmarkProgress.cs`, `RenderEngine.cs`, `Settings.cs`, `LogForm.cs`,
  `MandelbrotForm.cs`, `Diagnostics.cs`, `TODO.md`, `SPECS.md`,
  `CHANGELOG.md`, `AppVersion.cs`, `.csproj`.
- **v2.19.0** — Generate dialogs with own CUDA precision: Export PNG and Zoom
  video now carry 32-bit / 64-bit (slow) radios (default from the main window,
  enabled only with CUDA; the worker receives a captured copy, never touching
  controls). MIT license (`LICENSE`, copyright Francesco Ferrara) with the
  LLM-test project statement in README and SPECS (human-written spec usable to
  recreate the app; the test is how faithfully another LLM rebuilds it).
  Files: `ExportForm.cs`, `ExportForm.Designer.cs`, `ZoomVideoForm.cs`,
  `ZoomVideoForm.Designer.cs`, `LICENSE` (new), `README.md`, `TODO.md`,
  `SPECS.md`, `CHANGELOG.md`, `AppVersion.cs`, `.csproj`.
## v2.18.x — Tiled export and code comments
- **v2.18.5** — Generic Q&A moved out of `TODO.md` into new `QUESTIONS.md`
  (GPU architecture, 5070 Ti work split, hardware comparison, ffmpeg/PATH,
  updates audit, launch, AGENTS.md meta) so the todo stays strictly about the
  program. No code change. Files: `QUESTIONS.md` (new), `TODO.md`,
  `SPECS.md`, `CHANGELOG.md`, `AppVersion.cs`, `.csproj`.
- **v2.18.4** — Plain `//` comments instead of XML doc tags across all sources:
  every method documents its parameters as `Param name (Type): meaning` plus
  `Returns (Type):` where applicable; HLSL entry points keep in-shader `//`
  input/output docs. No code change. Files: all `MandelbrotViewer/*.cs`,
  `TODO.md`, `SPECS.md`, `CHANGELOG.md`, `AppVersion.cs`, `.csproj`.
- **v2.18.3** — Extensive input/output comments on the CPU compute functions
  in `Mandelbrot.cs` (`Render`, `RenderTile`, `ColorFromEscape`,
  `AutoIterForScale`, `BenchmarkCpu`): per-parameter roles, output bitmap
  contract, tile→image mapping and benchmark metric. No code change. Files:
  `Mandelbrot.cs`, `TODO.md`, `SPECS.md`, `CHANGELOG.md`, `AppVersion.cs`,
  `.csproj`.
- **v2.18.2** — Extensive input/output comments on all kernel functions: the
  four CUDA kernels plus the palette helper in `GpuMandelbrot.cs` (thread
  index decoding, device output buffers, view/palette parameters) and the HLSL
  entry points in `DxMandelbrot.cs` (`VS`, `Graded`, `PS`, `BenchPS` with
  cbuffer fields and shader inputs/outputs). No code change. Files:
  `GpuMandelbrot.cs`, `DxMandelbrot.cs`, `TODO.md`, `SPECS.md`,
  `CHANGELOG.md`, `AppVersion.cs`, `.csproj`.
- **v2.18.1** — On-chip SSAA without huge image (option B): the CPU tile
  renderer computes k×k subsamples per output pixel with a local accumulator,
  so memory stays O(W×H) and no W*k×H*k buffer is allocated (same sample
  coordinates, pixel-identical output; CUDA/DirectX already worked this way).
  Zoom video keeps the selected AA with no 128 MPixel auto-reduction. Busy
  cursor recheck: only the latest render restores it (no more stuck
  AppStarting) and the image panels go back to Crosshair. Benchmark untouched
  (AA1x iterations-only kernels). Files: `Mandelbrot.cs`,
  `ZoomVideoForm.cs`, `MandelbrotForm.cs`, `TODO.md`, `SPECS.md`,
  `CHANGELOG.md`, `AppVersion.cs`, `.csproj`.

- **v2.18.0** — High-resolution PNG export now renders in 512×512 tiles. The
  full-image coordinate system, sample order, supersampling and color math are
  preserved, so tiled output is pixel-identical to a whole-frame render while
  AA8x remains available for the Double 4K preset. CPU, CUDA and DirectX use
  bounded tile memory and the final bitmap is assembled with exact 32-bit row
  copies. Files: `ExportForm.cs`, `Mandelbrot.cs`, `GpuMandelbrot.cs`,
  `DxMandelbrot.cs`, `TODO.md`, `SPECS.md`, `CHANGELOG.md`, `AppVersion.cs`,
  `.csproj`.

## v2.17.x — Benchmark throughput fixes
- **v2.17.6** — Benchmark chart results are now ordered from highest to lowest
  MPixel/s, including the current measurement, so historical performance is
  easier to compare at a glance. Files: `BenchmarkForm.cs`, `TODO.md`,
  `SPECS.md`, `CHANGELOG.md`, `AppVersion.cs`, `.csproj`.

- **v2.17.5** — CUDA benchmark now uses an adaptive in-flight batch, with one
  synchronization per batch instead of one synchronization per frame, matching
  the DirectX throughput protocol. CUDA benchmark access is serialized with
  normal CUDA rendering through the shared render gate, so the MPixel/s result
  is driven by GPU compute throughput rather than per-frame submit latency.
  Historical values recalculated in Release with the new protocol: CUDA 5070 Ti
  278.2/6.7 and 4070 SUPER 222.1/5.3 (32/64-bit); DirectX 5070 Ti 337.4,
  4070 SUPER 262.6 and Radeon 5.2; CPU 9900X 4.9.
  Files: `GpuMandelbrot.cs`, `TODO.md`, `SPECS.md`, `CHANGELOG.md`,
  `AppVersion.cs`, `.csproj`.

- **v2.17.4** — DX benchmark: made it measure the card's true compute throughput. (1) Removed the per-frame submit/wait "contorno" that starved the GPU: the frames-in-flight depth is now sized at run time from a TDR-safe estimate (deep enough to saturate fast cards, capped at ~0.8 s of queued work so slow cards stay clear of TDR). (2) Aligned the DirectX and CUDA benchmark kernels to the same iteration loop (incremental squares), so the bars are a fair engine-to-engine comparison of the identical workload (the coordinate mapping was already identical). History recalculated (best of 3, same session): DirectX 5070 Ti 329.7, 4070 SUPER 261.4, Radeon 5.2; CUDA 5070 Ti 275.5 (32-bit)/6.7 (64-bit), 4070 SUPER 220.0/5.3; CPU 9900X 4.9. Files: `DxMandelbrot.cs`, `BenchmarkForm.cs`, `TODO.md`, `SPECS.md`, `CHANGELOG.md`, `AppVersion.cs`, `.csproj`.

- **v2.17.3** — Toolbar: replaced `TableLayoutPanel` with two `FlowLayoutPanel` (one per row); controls scroll to the left without spacing from shared columns. Files: `MandelbrotForm.Designer.cs`, `AppVersion.cs`, `.csproj`.

- **v2.17.2** — App icon replaced with `icon2.png`, converted to a multiresolution `.ico` 16/32/48/256. Files: `app.ico`, `AppVersion.cs`, `.csproj`.

- **v2.17.1** — Window resize fix: DirectX did not redraw after resize (missing `dxPanel.Resize` handler that sets `_dxDirty`); CPU/CUDA waited for the 300 ms debounce (now `RenderAsync(preview)` immediately on each event, as DX); `pictureBox.SizeMode` from `Normal` to `Zoom` (the image scales keeping the aspect ratio during the transition). Files: `MandelbrotForm.cs`, `MandelbrotForm.Designer.cs`, `TODO.md`, `SPECS.md`, `CHANGELOG.md`, `AppVersion.cs`, `.csproj`.

- **v2.17.0** — Palette color aligned to the Python reference: gamma curve `t = (nu/maxIter)^0.35` instead of the linear mapping (smooth iteration already present). Files: `Palette.cs`, `GpuMandelbrot.cs`, `DxMandelbrot.cs`.

## v2.16.x — Generate menu and realtime zoom
- **v2.16.0** — Main Generate menu (former File Export submenu: screenshot, zoom video) and Real Time zoom (animation on the main view from the current zone to the set, 120 frames at 30 fps, Esc to stop). Files: `MandelbrotForm.Designer.cs`, `ZoomVideoForm.cs`, `ZoomVideoForm.Designer.cs`, `TODO.md`, `SPECS.md`, `CHANGELOG.md`, `AppVersion.cs`, `.csproj`.

## v2.15.5 – v2.15.10 — Standard benchmark at AA1x
- **v2.15.10** — DX throughput fix at AA1x: event-query poll tight again (the Sleep was depressing frames from ~1 ms: 5070 Ti 67.4→292.4); anti-hang checks every 1024 polls. DX history updated. Files: `DxMandelbrot.cs`, `BenchmarkForm.cs`, `TODO.md`, `SPECS.md`, `CHANGELOG.md`, `AppVersion.cs`, `.csproj`.

- **v2.15.9** — Standard benchmark at AA1x (960x540 grid): the metric stays MPixel/s and the Radeon comes back (no more TDR or iGPU skip); history recalculated via CLI: CUDA 5070 Ti 276.3/6.7 and 4070 SUPER 220.7/5.3, DirectX 5070 Ti 65.9, 4070 SUPER 56.8 and Radeon 4.8, CPU 9900X 4.8 (9 bars). Files: `BenchmarkStandard.cs`, `DxMandelbrot.cs`, `BenchmarkForm.cs`, `TODO.md`, `SPECS.md`, `CHANGELOG.md`, `AppVersion.cs`, `.csproj`.

- **v2.15.8** — DX benchmark: preventive skip of cards with <1 GB dedicated (no more TDR/AMD popup), `QuerySignaled` safe on device removed (no more raw HRESULT in the log), single event log via GUI; history: DirectX 5070 Ti 436.3 + 4070 SUPER 355.2 (8 bars). Files: `DxMandelbrot.cs`, `BenchmarkForm.cs`, `TODO.md`, `SPECS.md`, `CHANGELOG.md`, `AppVersion.cs`, `.csproj`.

- **v2.15.7** — Log/diagnostics (Help menu): new Events log section with benchmark errors (in-memory AppLog, last 200 lines); DX errors (device removed, timeout, VRAM) and GUI errors now stay in the log. Files: `AppLog.cs`, `MandelbrotForm.cs`, `DxMandelbrot.cs`, `BenchmarkForm.cs`, `TODO.md`, `SPECS.md`, `CHANGELOG.md`, `AppVersion.cs`, `.csproj`.

- **v2.15.6** — DX benchmark anti-hang: VRAM check before the AA8x grid, 60 s timeout per frame and device-removed detection in `DrainOne` (the Radeon iGPU went into TDR and hung with no errors); errors visible in GUI and `run failed` in CLI. Files: `DxMandelbrot.cs`, `Diagnostics.cs`, `BenchmarkForm.cs`, `TODO.md`, `SPECS.md`, `CHANGELOG.md`, `AppVersion.cs`, `.csproj`.

- **v2.15.5** — Benchmark history recalculated via CLI on the new zone (best of 3): CUDA 5070 Ti 490.7/9.0 and 4070 SUPER 384.1/7.0 (32/64-bit), DirectX 5070 Ti 483.0, CPU 9900X 6.1; removed the DirectX 4070 SUPER bars (card absent in DXGI) and AMD Radeon (bench hangs). Chart with 7 bars. Files: BenchmarkForm.cs, TODO.md, SPECS.md, CHANGELOG.md, AppVersion.cs, .csproj.

## v2.15.0 – v2.15.4 — Benchmark window polish
- **v2.15.4** — Benchmark: export button renamed CSV… → Export CSV and Close always on the right (dock order: Start, Export CSV, Close). Files: BenchmarkForm.Designer.cs, TODO.md, SPECS.md, CHANGELOG.md, AppVersion.cs, .csproj.

- **v2.15.3** — Benchmark: iterations no longer fixed but computed with the auto formula at the test scale (BenchmarkStandard.MaxIter => Mandelbrot.AutoIterForScale(Scale) = 10915). Files: BenchmarkStandard.cs, BenchmarkForm.cs, TODO.md, SPECS.md, CHANGELOG.md, AppVersion.cs, .csproj.

- **v2.15.2** — Benchmark zone: center (-0.7499302568795561, -0.015139113925433963), scale 1.0453474311811176e-04 (half 5.226737155905588e-05), 10915 iterations (= zone auto); w×h 960×540 and 8 s budget unchanged. History unchanged (referenced to the old zone). Files: BenchmarkStandard.cs, TODO.md, SPECS.md, CHANGELOG.md, AppVersion.cs, .csproj.

- **v2.15.1** — Auto iterations 2000*(1+log10(1.5/half)) (half = scale/2, clamp 50-50000) centralized in Mandelbrot.AutoIterForScale, used by view and zoom video (at benchmark scale 5e-4 it is 9556, but the benchmark stays at 5000 fixed iterations). Files: Mandelbrot.cs, MandelbrotForm.cs, ZoomVideoForm.cs, TODO.md, SPECS.md, CHANGELOG.md, AppVersion.cs, .csproj.

- **v2.15.0** — File → Export submenu with Screenshot (Ctrl+Shift+E: dialog
  starting from current view with free preset and AA) and Zoom video (Ctrl+Shift+V)
  with its own AA selector (As view/1x/2x/4x/8x: the video can use a different AA
  than the view); removed the individual items. Files: `MandelbrotForm.cs`,
  `MandelbrotForm.Designer.cs`, `ExportForm.cs`, `ZoomVideoForm.cs`,
  `ZoomVideoForm.Designer.cs`, `TODO.md`, `SPECS.md`,
  `CHANGELOG.md`, `AppVersion.cs`, `.csproj`.

## v2.14.x — PNG export presets and palettes
- **v2.14.1** — 2-3 base color palettes: Forest redrawn (brown and green
  at the extremes, interior always black) and Purple with vivid magenta↔cyan opposites;
  the others were already 2-3 shades. Applies to all engines (generic stops). Files:
  `Palette.cs`, `TODO.md`, `CHANGELOG.md`, `AppVersion.cs`, `.csproj`.

- **v2.14.0** — PNG export with presets (Current view, Full HD, 2K, 4K, 8K,
  Double 4K 7680×2160, Custom), selectable AA (As view/1x/2x/4x/8x) and
  validated custom dimensions (integers 320…16384, fields active only on
  Custom); sample cap and dialog unchanged. Video encode fix: the
  view often has odd sides and libx264/yuv420p rejects them ("Could not open
  encoder" + "no packets", exit 0xDFABA7BB, reproduced) — pad to even dimensions,
  pre-flight on PNGs and more stderr lines in errors. Files: `ExportForm.cs`,
  `ExportForm.Designer.cs`, `ZoomVideoForm.cs`, `TODO.md`, `SPECS.md`,
  `CHANGELOG.md`, `AppVersion.cs`, `.csproj`.

## v2.13.x — Zoom video and icons
- **v2.13.2** — Exe and windows icon: 256 px render of the set in
  Fire palette, multiresolution `.ico` (16/32/48/256) via `ApplicationIcon` +
  embedded resource applied to all forms (`Program.ApplyIcon`). Files:
  `app.ico` (new), `MandelbrotViewer.csproj`, `Program.cs`,
  `MandelbrotForm.cs`, `BenchmarkForm.cs`, `ExportForm.cs`, `ZoomVideoForm.cs`,
  `LogForm.cs`, `TODO.md`, `SPECS.md`, `CHANGELOG.md`, `AppVersion.cs`.

- **v2.13.1** — Fix hang at the end of video export: the ffmpeg encoding ran on the
  UI thread and stderr was read after `WaitForExit` — with 60+ frames the
  pipe fills up, ffmpeg blocks on write and the app never returns (file
  present but UI dead; reproduced with a watchdog). Now encoding on worker, stderr
  drained asynchronously during the wait (`BeginErrorReadLine`) and process killed
  on cancel; inverted direction (from current view to the set, with
  error if already at the set instead of starting from 100x inside); center
  proportional to the zoom and not to t (before the starting point left the
  frame immediately: up to 46x the half-view, now max 0.17); transition
  cubic ease-out (fast at the start, slow at the end); the video uses the selected AA
  (auto-reduced beyond 128 MPixel of samples, like the export);
  Open button for the result (MP4 with the player, PNG folder in Explorer).
  Files: `ZoomVideoForm.cs`, `ZoomVideoForm.Designer.cs`, `TODO.md`, `SPECS.md`,
  `CHANGELOG.md`, `AppVersion.cs`, `.csproj`.

- **v2.13.0** — Zoom video MP4 (File → Ctrl+Shift+V): interpolates from the current view
  to the complete set (logarithmic scale, linear center, auto iterations per
  frame like `AutoIter`), renders 60…480 frames at view resolution and AA1x with
  the active engine (Julia included), encodes with ffmpeg (H.264, CRF 18) or — if
  absent — leaves the PNG sequence; deterministic progress + cancel. New
  `ZoomVideoForm.cs`/`.Designer.cs`. Files: `ZoomVideoForm.cs`,
  `ZoomVideoForm.Designer.cs` (new), `MandelbrotForm.cs`,
  `MandelbrotForm.Designer.cs`, `TODO.md`, `SPECS.md`, `CHANGELOG.md`,
  `AppVersion.cs`, `.csproj`.

## v2.10.x – v2.12.x — Julia mode, hi-res export, history
- **v2.12.0** — Julia mode (View menu, Ctrl+J): `z = z² + c` with c fixed
  (default −0.7 + 0.27015i, persisted in settings), left click = fix c
  on the point, zoom/pan/AA/palette unchanged, R also resets c; CPU, CUDA
  float/double kernels and HLSL shader with uniform branch (same coloring); zones
  (history, favorites, files) include mode and c with defaults for
  old files; benchmark always Mandelbrot; HR export follows the mode. Files:
  `Mandelbrot.cs`, `GpuMandelbrot.cs`, `DxMandelbrot.cs`, `MandelbrotForm.cs`,
  `ExportForm.cs`, `Settings.cs`, `TODO.md`, `SPECS.md`, `CHANGELOG.md`,
  `AppVersion.cs`, `.csproj`.

- **v2.11.0** — High-resolution PNG export (File → Ctrl+Shift+E): dialog with
  width 320…16384 px (presets 1280…7680, height from the view aspect),
  offscreen render with the active engine (CPU/CUDA/DirectX via offscreen preview),
  AA auto-reduced beyond 128 MPixel of samples, marquee +
  cancel, PNG save. New `ExportForm.cs`/`.Designer.cs`. Files:
  `ExportForm.cs`, `ExportForm.Designer.cs` (new), `MandelbrotForm.cs`,
  `MandelbrotForm.Designer.cs`, `TODO.md`, `SPECS.md`, `CHANGELOG.md`,
  `AppVersion.cs`, `.csproj`.

- **v2.10.0** — View history + favorite zones: new View menu with
  Back/Forward (Alt+Left/Right, in-memory stack cap 200, every zoom/pan/
  reset/load is committed), "Save favorite zone…" (Ctrl+D, JSON in
  `%APPDATA%\MandelbrotViewer\zone\`), jump and delete submenus
  (rebuilt on opening, with validation like Load zone). `LoadZone`
  refactored on shared `TryParseZone`/`ApplyZone`. Files:
  `MandelbrotForm.cs`, `TODO.md`, `SPECS.md`, `CHANGELOG.md`,
  `AppVersion.cs`, `.csproj`.

## v2.6.x – v2.9.x — CSV export, CPU bench, palettes, keyboard pan
- **v2.9.0** — CSV export of the benchmarks (one row per run: timestamp, engine,
  device, precision, run, frames, seconds, MPixel/s): "CSV…" button in
  the benchmark window (queues the measurement) and `--csv file` in the
  `--bench-dx/--bench-cuda/--bench-cpu` commands. New `BenchmarkCsv.cs`. Files:
  `BenchmarkCsv.cs` (new), `BenchmarkForm.cs`, `BenchmarkForm.Designer.cs`,
  `Diagnostics.cs`, `Program.cs`, `TODO.md`, `SPECS.md`, `CHANGELOG.md`,
  `AppVersion.cs`, `.csproj`.

- **v2.8.0** — CPU benchmark from CLI (`--bench-cpu`: 3 runs of 8 s in double) and
  history with model name (`Diagnostics.CpuName` from registry, e.g. "CPU 9900X"):
  measured 27.8 MPixel/s on AMD Ryzen 9 9900X (replaces the old "CPU"
  30.0). Files: `Diagnostics.cs`, `Program.cs`, `BenchmarkForm.cs`, `TODO.md`,
  `SPECS.md`, `CHANGELOG.md`, `AppVersion.cs`, `.csproj`.

- **v2.7.0** — Four fixed palettes (Ocean, Purple, Desert, Forest, 5 stops
  each like the existing ones): immediately valid on CPU, CUDA and DirectX because the
  stops are generic; enum extended only at the end so the persisted indices remain
  valid. Files: `Palette.cs`, `MandelbrotForm.cs`, `MandelbrotForm.Designer.cs`,
  `TODO.md`, `SPECS.md`, `CHANGELOG.md`, `AppVersion.cs`, `.csproj`.

- **v2.6.0** — Keyboard pan with the arrow keys: 1/10 of the view per
  step (Shift = fine step 1/100); ignored when the focus is on the iterations
  count (which already uses up/down). Commands guide updated. Files:
  `MandelbrotForm.cs`, `MandelbrotForm.Designer.cs`, `TODO.md`,
  `SPECS.md`, `CHANGELOG.md`, `AppVersion.cs`, `.csproj`.

## v2.5.15 – v2.5.23 — Offscreen benchmark, triple tests, docs
- **v2.5.23** — Documentation review: `SPECS.md` rewritten on the
  actual and current specs (removed the duplicated version history, the
  hardware comparison and the obsolete notes on Present/bench window; description
  of engines, benchmark, CLI, files and technical notes verified against the code) and
  version headings restored in `CHANGELOG.md` where missing.
  No code change. Files: `SPECS.md`, `CHANGELOG.md`, `TODO.md`,
  `AppVersion.cs`, `.csproj`.

- **v2.5.22** — AppStarting cursor (arrow+clock) instead of Wait during
  renders: the computation is async and the UI stays interactive, so it is the
  correct cursor; assigned recursively to forms and descendants (covers pictureBox,
  menu, status; `UseWaitCursor` removed because it forces the full hourglass). Wait
  remains only for the synchronous CUDA init with frozen UI. Files: `MandelbrotForm.cs`,
  `TODO.md`, `SPECS.md`, `AppVersion.cs`, `.csproj`.

- **v2.5.21** — Wait cursor fix: during long renders (e.g. hard zone AA8x
  switching CUDA from 32 to 64 bit) the cursor stayed an arrow if the mouse was
  on the image, because `pictureBox` was not in the `SetBusyCursor` list.
  Now the form also uses `UseWaitCursor`, which covers all controls and
  surfaces (image, menu, status). Files: `MandelbrotForm.cs`, `TODO.md`,
  `SPECS.md`, `AppVersion.cs`, `.csproj`.

- **v2.5.20** — DirectX offscreen benchmark without Present: solves the
  skewed measurement on cards without monitor (the 4070 SUPER, headless in a PCIe x4 slot,
  was paying the 132 MB/frame copy toward the 5070 Ti of the display: 1655 → 4441
  MPixel/s, ~2.7×). The test renders on an in-memory render target (headless, without
  window nor DWM) and counts the frames actually completed with a ring of event
  queries (`QueryType.Event`, `GetData` with pData NULL: S_OK = ready). Vortice detail:
  the `GetData(async, flags)` overload always returns a non-null DataStream,
  for the status the raw overload is needed (S_OK vs S_FALSE). Device/shader
  creation extracted to `EnsureDevice`, reused by `TryInitialize` (GUI) and
  `TryInitializeHeadless` (CLI). GUI and CLI use the same path; DX history
  replaced with the offscreen measurements (best of 3): 5070 Ti 5780.7 (+1%),
  4070 SUPER 4441.6, AMD Radeon 102.2. Files: `DxMandelbrot.cs`, `Diagnostics.cs`,
  `BenchmarkForm.cs`, `TODO.md`, `SPECS.md`, `AppVersion.cs`, `.csproj`.

- **v2.5.19** — Triple CUDA test per device and updated history: new command
  `--bench-cuda [device name]` that runs 3 runs of 8 s of the standardized test
  on each CUDA device in float 32-bit and double 64-bit and prints the values in
  MPixel/s with the best (analog of `--bench-dx`). Measured now (best of 3):
  RTX 5070 Ti 5653.6 (32-bit) / 140.7 (64-bit), RTX 4070 SUPER 4667.8 (32-bit) /
  110.3 (64-bit) — the old single reference "CUDA 5070 Ti 5940" is replaced
  by these four bars. Chart widened (9 bars: left margin and panel
  height increased). Files: `Diagnostics.cs`, `Program.cs`, `BenchmarkForm.cs`,
  `BenchmarkForm.Designer.cs`, `TODO.md`, `SPECS.md`, `AppVersion.cs`,
  `.csproj`.

- **v2.5.18** — Triple DirectX test per card and updated history: new command
  `--bench-dx [card name]` that runs 3 runs of 8 s of the standardized test on
  each available DXGI card and prints the values in MPixel/s with the best
  (minimum visible window: with Present(0) on a hidden window the compositor
  may skip the GPU work). Test parameters moved to the shared class
  `BenchmarkStandard` (used by GUI and CLI); measurement loop extracted to
  `DxMandelbrot.RunBenchmarkFrames` (reused by the GUI benchmark, now also on
  a worker thread); `DxMandelbrot.ShortAdapterName` for compressed names. In the
  benchmark chart the obsolete bar "DirectX 5070 Ti 1750" (old test
  with v-sync, not comparable) is replaced by the per-card references
  measured now (best of 3 runs, see SPECS); axis calculated from the bars.
  Files: `BenchmarkStandard.cs` (new), `DxMandelbrot.cs`, `BenchmarkForm.cs`,
  `Diagnostics.cs`, `Program.cs`, `TODO.md`, `SPECS.md`, `AppVersion.cs`,
  `.csproj`.

- **v2.5.17** — Fix: `AdapterNames()` always returned an empty list on
  machines with modern GPUs (≥4 GB): `DedicatedVideoMemory` is a PointerUSize
  (SIZE_T) and the `> 0` comparison went through the implicit 32-bit
  conversion of SharpGen (`UIntPtr.ToUInt32`), which throws OverflowException beyond 4 GB —
  the RTX 5070 Ti (16 GB, first adapter) was killing the entire enumeration, with
  the error swallowed by the catch (it is the "Vortice wrapper overflow" of the
  v2.3.8 note). Now software renderers are excluded by name ("Microsoft
  Basic*") and the list actually reports the cards. `DiagDx` prints the
  detailed enumeration per adapter and the eventual enumeration error; the
  GUI diagnostic log reports `EnumerationError`. Verified at runtime:
  full list (RTX 5070 Ti, AMD Radeon iGPU, RTX 4070 SUPER) and
  explicit selection working on 4070 SUPER and iGPU (`IsReady: True`, `Render: OK`).
  Files: `DxMandelbrot.cs`, `Diagnostics.cs`, `MandelbrotForm.cs`, `TODO.md`,
  `SPECS.md`, `AppVersion.cs`, `.csproj`.

- **v2.5.16** — Fix: the GPU dropdown did not apply the chosen DirectX card:
  `TryInitialize` always created the device on the default adapter
  (`D3D11CreateDevice(null, DriverType.Hardware)`). Now the requested card is
  looked up by name among the DXGI adapters of the same factory as the swapchain and
  passed explicitly to `D3D11CreateDevice` with `DriverType.Unknown`
  (mandatory when passing an adapter); nonexistent name → clear error in
  `LastError` ("Video card not found: …"). `--diag-dx` accepts the
  card name as argument for the test without UI. Files: `DxMandelbrot.cs`,
  `Diagnostics.cs`, `Program.cs`, `TODO.md`, `SPECS.md`, `AppVersion.cs`,
  `.csproj`.

- **v2.5.15** — Reorganization and cleanup refactor (no functional change):
  palettes/gradients extracted to `Palette.cs` (`PaletteColors`, single source for
  CPU/DirectX; CUDA receives them via `GpuPaletteParams`) with alignment
  comments between the three coloring implementations (CPU/CUDA/HLSL);
  `BenchmarkProgress` in its own file; CLI diagnostics `--diag-dx`/`--diag-gpu`
  moved from `Program.cs` to `Diagnostics.cs`; in `DxMandelbrot.cs` extracted
  shared `BuildParams`/`DrawFrame` from Render/RenderBenchmark/
  RenderPreviewToBitmap, indentation unified and `System.Drawing.Bitmap` →
  `Bitmap`; removed the dead `total` field in the CUDA benchmark (the meter is
  per-frame, `TotalIters` = 0 as in DirectX); "Save image" menu
  handler renamed to `SaveImageItem_Click`; removed the `RenderGpu` wrapper, double dispose
  of the bitmap, redundant usings and obsolete comments. Files: `Palette.cs`,
  `BenchmarkProgress.cs`, `Diagnostics.cs` (new), `Program.cs`,
  `Mandelbrot.cs`, `GpuMandelbrot.cs`, `DxMandelbrot.cs`, `MandelbrotForm.cs`,
  `MandelbrotForm.Designer.cs`, `RenderEngine.cs`, `LogForm.cs`, `AppVersion.cs`,
  `.csproj`, `TODO.md`, `SPECS.md`.


## v2.5.0 – v2.5.14 — Benchmark window and measurements
- **v2.5.14** — Documentation note: theoretical comparison RTX 4070 SUPER vs RTX 5070 Ti.
  FP32 35.5 → 43.9 TFLOPS (+24%), FP64 0.55 → 0.69 TFLOPS (+24%), memory
  504 → 896 GB/s (+78%, irrelevant for the compute-bound benchmark), tensor
  cores 224 (4th gen) → 280 (5th gen) but marketing AI TOPS on different formats
  (616 FP8 sparse vs 1406 FP4 sparse; at equal format ~+25%). For the
  benchmark of this app (only FP32/FP64 iterations, no tensor cores)
  expected ~+20-25%; raster gaming (TechPowerUp) ~+37%. Files: `SPECS.md`,
  `TODO.md`, `AppVersion.cs`, `.csproj`.


- **v2.5.13** — DirectX benchmark fix: the measurement gave unrealistic results because
  `BeginBenchmark` set the `_benchmarking` flag **before** calling `Resize`,
  which with the flag active ignores the request: the swapchain stayed at panel size
  (~1 MPixel/frame) while the meter credited 33.18 MPixel/frame
  (results ~30-40× inflated). Now the resize happens before the flag. The
  DirectX preview was black because `ReadTextureToBitmap` called `CopyResource` with
  the arguments reversed (copying the empty staging over the rendered texture);
  corrected to `CopyResource(staging, source)` (fix that also restores Save PNG)
  and the preview texture now uses `B8G8R8A8_UNorm`, same byte layout as
  `Format32bppArgb`, for non-inverted channels. Files: `DxMandelbrot.cs`,
  `AppVersion.cs`, `.csproj`.


- **v2.5.12** — Colored preview of the benchmark zone also for DirectX: the
  benchmark renders one offscreen frame with the normal shader (Fire palette) and
  shows it in the BenchmarkForm box, as for CUDA/CPU. During the test the
  main window no longer shows the gray sample frame: the DX panel
  is hidden and restored on close. Added `DxMandelbrot.
  RenderPreviewToBitmap` + helper `ReadTextureToBitmap` (extracted from `Capture`).
  Files: `DxMandelbrot.cs`, `BenchmarkForm.cs`, `MandelbrotForm.cs`,
  `AppVersion.cs`, `.csproj`.

- **v2.5.11** — The benchmark shows the first frame of the tested zone also for
  CUDA and CPU: 960x540 preview (AA1x) rendered with the active engine in a
  dedicated box of the Benchmark window, before starting the measurement. DirectX already showed
  the first frame in the swapchain. Files: `BenchmarkForm.cs`,
  `BenchmarkForm.Designer.cs`, `AppVersion.cs`, `.csproj`.

- **v2.5.10** — Benchmark standardized across engines: zone 960x540 AA8x without
  sample averaging (grid of 7680x4320 elementary samples, same work for
  CPU, CUDA and DirectX). The DirectX benchmark uses an iterations-only shader and
  presents without v-sync (`Present(0)`), eliminating the monitor refresh limit
  that capped the measurement at ~60 fps. The swapchain is enlarged to the
  grid during the test and restored at the end. Files: `DxMandelbrot.cs`
  (`BenchPsSource`, `BeginBenchmark`/`EndBenchmark`/`RenderBenchmark`),
  `BenchmarkForm.cs`, `AppVersion.cs`, `.csproj`.

- **v2.5.9** — Updated the historical references of the chart: CUDA RTX 5070 Ti
  5940 MPixel/s, DirectX RTX 5070 Ti 1750 MPixel/s, CPU 30 MPixel/s.

- **v2.5.8** — The benchmark immediately shows `0%` and updates percentage and
  MPixel/s every second.

- **v2.5.7** — The DirectX benchmark runs on a separate worker; the UI stays
  responsive and shows the intermediate `percentage | MPixel/s` updates.

- **v2.5.6** — Made visible the intermediate percentage updates of the
  benchmark on CPU, CUDA and DirectX, including the label repaint.

- **v2.5.5** — Removed the progress bar from the benchmark; only the textual
  percentage remains visible.

- **v2.5.4** — During the benchmark only the percentage is shown; added
  time for the first redraw and corrected the axis label spacing.

- **v2.5.3** — Resized the main benchmark result and converted
  the chart to horizontal bars to avoid truncated text.

- **v2.5.2** — Added `avvia.bat` to try the current version with
  `dotnet run`, without using the self-contained publish.

- **v2.5.1** — Corrected the RTX 5070 Ti CUDA reference in the chart to
  5880 MPixel/s.

- **v2.5.0** — The benchmark starts automatically on opening and shows a
  bar chart with the measured result and the references 5070 Ti CUDA (5880
  MPixel/s) and AMD 9900X (80 MPixel/s).

## v2.2.x – v2.4.x — Coloring unification and CUDA/DirectX work
- **v2.4.1** — Corrected the CUDA colorizer: the palette value is
  clamped to `[0,1]`, avoiding magenta variations at high iterations.

- **v2.4.0** — Restored smooth coloring on CPU, CUDA float/double and
  DirectX using the final module of `z`; CUDA enables `ILGPU.Algorithms` to
  compile `XMath.Log2` in the kernels.

- **v2.3.15** — Removed the `avvia.bat` and `avvia.ps1` launchers; the launch happens
  via the published file or the documented .NET commands.

- **v2.3.14** — The log window no longer shows all text selected in blue
  on opening; the initial focus goes to the `Close` button.

- **v2.3.13** — Lightened the palettes with a common curve on CPU, CUDA and
  DirectX (`t × 1.35 + 0.03`, saturated at 1), keeping the interior black.

- **v2.3.12** — Added the real DirectX benchmark: runs the shader on the
  swapchain and counts the presented frames, showing pixel/s and completed frames.

- **v2.3.11** — Corrected the benchmark text: the DirectX fallback now shows
  `CPU precision`; `CUDA 32/64-bit` appears only when CUDA is actually used.

- **v2.3.10** — Unified the CPU, CUDA and DirectX coloring: same map
  based on iterations and same palette stops.

- **v2.3.9** — Aligned the CUDA/DirectX coloring on the same
  palette interpolation; kept the AA computation and downsampling on GPU. Verified
  both engines at runtime.

- **v2.3.8** — Corrected the CUDA kernel for ILGPU runtime compilation and
  verified the render on the RTX 5070 Ti; DirectX now initializes the
  hardware adapter without the faulty marshalling of the DXGI description.

- **v2.3.7** — Moved the coloring and AA downsampling to CUDA;
  only the final bitmap is transferred to the CPU. Files: `GpuMandelbrot.cs`,
  `AppVersion.cs`, `.csproj`.

- **v2.3.6** — GPU render and CUDA benchmark optimization: serialization of
  computation and coloring to avoid races between cancelled renders; the benchmark
  now respects the selected 32/64 precision and no longer recopies
  the full buffer to the CPU. Files: `GpuMandelbrot.cs`, `BenchmarkForm.cs`,
  `MandelbrotForm.cs`, `AppVersion.cs`, `.csproj`.

- **v2.3.5** — DirectX fix: `D3D11CreateDevice` now uses `DriverType.Unknown`
  when it receives the selected DXGI adapter. Files: `DxMandelbrot.cs`,
  `AppVersion.cs`, `.csproj`.

- **v2.3.4** — DirectX diagnostics: log with the exact step that fails
  the initialization (e.g. `[IDXGIFactory2.CreateSwapChainForHwnd] ...`); swapchain
  changed from `FlipDiscard` to `FlipSequential` (more compatible) and `SampleDescription`
  explicitly fixed to (1,0). Files: `DxMandelbrot.cs` (`TryInitialize`), `AppVersion.cs`,
  `.csproj`.

- **v2.3.3** — Help item → log/diagnostics: a read-only window with
  the status of the engines (DirectX and CUDA: ready, card in use, last error,
  cards available), the GPU selection, the precision and the settings
  saved — useful to understand why an engine is disabled. Files:
  `MandelbrotForm.cs` (`LogItem_Click`, `BuildDiagnosticLog`),
  `MandelbrotForm.Designer.cs`, new `LogForm.cs`, `AppVersion.cs`, `.csproj`.

- **v2.3.2** — GPU engine unavailable: the exact reason (`LastError`) is now
  shown in the status bar instead of leaving the radio simply
  gray. Files: `MandelbrotForm.cs` (`Shown` handler), `AppVersion.cs`, `.csproj`.

- **v2.3.1** — GPU dropdown hidden (instead of disabled) with CPU engine:
  the bar stays clean and the video card choice appears only with CUDA or
  DirectX. Files: `MandelbrotForm.cs` (`ApplyEngineVisibility`),
  `MandelbrotForm.Designer.cs` (`lblGpu`/`cmbGpu` `Visible`), `AppVersion.cs`,
  `.csproj`.

- **v2.3** — CUDA precision at choice: radio 32/64 (32 = float 32-bit, faster;
  64 = double 64-bit, more precise, default). Before the precision was
  decided automatically from the scale (`WantsDouble`); now it is at the user's choice.
  The two radios are enabled only with CUDA engine (CPU = always double,
  DirectX = always float) and the choice is persisted in settings.json. Files:
  `MandelbrotForm.cs`/`MandelbrotForm.Designer.cs` (`precisionPanel`,
  `radPrec32`/`radPrec64`, `UseDoublePrecision`), `GpuMandelbrot.cs`
  (`RenderFrame` with `useDouble`), `Settings.cs` (`Single`), `AppVersion.cs`,
  `.csproj`.

- **v2.2** — ToolTips on File/Help controls (they explain function and shortcut);
  the "wait" cursor now visible also with the mouse over a control (e.g. the
  AA dropdown) during the computation, because it is imposed on the form and the input controls
  and not only on the form. Files: `MandelbrotForm.cs` (`SetBusyCursor`,
  `RenderAsync`), `MandelbrotForm.Designer.cs` (`toolTip` component +
  `SetToolTip`), `AppVersion.cs`, `.csproj`.

- GPU dropdown disabled with CPU engine (the card is chosen only with CUDA
  or DirectX). Files: `MandelbrotForm.cs` (`ApplyEngineVisibility`),
  `MandelbrotForm.Designer.cs`.

## v2.0.x – v2.1.x — Multi-GPU choice, DirectX engine, settings
- **v2.1** — Choice of the video card (CUDA + DirectX) from dropdown: bar
  (Auto = most capable card, otherwise the named card). Enumerates the union
  of the DirectX cards (DXGI) and the CUDA devices (ILGPU) and creates the
  device/accelerator on the choice; the preference is persisted in
  `settings.json` (`Gpu`) and applied on engine change. Files:
  `DxMandelbrot.cs` (`AdapterNames`, `TryInitialize(..., adapterName)`),
  `GpuMandelbrot.cs` (`DeviceNames`, `TryInitialize(deviceName)`,
  `ResetAccelerator`), `MandelbrotForm.cs`, `MandelbrotForm.Designer.cs`
  (`lblGpu`, `cmbGpu`), `Settings.cs`, `AppVersion.cs`, `.csproj`.

- Initial view always from the full set (the previous view is no longer
  saved in `settings.json`; the benchmark stays standard at 5000). Files:
  `MandelbrotForm.Designer.cs`, `Settings.cs`, `MandelbrotForm.cs`.

- **v2.0** — DirectX realtime engine: fractal per frame (float,
  fullscreen triangle), ~60 fps loop with immediate pan/zoom, AA 2x/4x/8x as
  supersampling inside the shader, PNG save from the backbuffer, automatic CPU
  fallback on error. DirectX radio enabled at startup. Files:
  `DxMandelbrot.cs` (new), `MandelbrotForm.cs`, `MandelbrotForm.Designer.cs`
  (`dxPanel`, `radioDx`), `.csproj` (Vortice 3.8.3 dependencies).

- Persisted settings (`%APPDATA%\MandelbrotViewer\settings.json`):
  iterations auto/manual and value, palette, AA, preferred engine, last view
  and window position; GPU preference respected at startup. Files: `Settings.cs`
  (new), `MandelbrotForm.cs`.

- Fast CUDA benchmark: iterations-only kernel (no |z|² buffer) + reused
  buffers for all frames (no extra alloc/transfer). Files:
  `BenchmarkForm.cs`, `GpuMandelbrot.cs`.

- File menu → Save image with name. Files: `MandelbrotForm.Designer.cs`.

- Benchmark: UI updates limited to every 3 s + final report, so the
  Invokes do not skew the measurement (`BenchmarkProgress.ReportInterval`). Files:
  `Mandelbrot.cs`, `GpuMandelbrot.cs`, `BenchmarkForm.cs`.

- Total iterations removed from the benchmark result (they remain in the detail);
  `BenchmarkProgress` also reports the frames. Files: `BenchmarkForm.cs`,
  `Mandelbrot.cs`, `GpuMandelbrot.cs`.

- Standard benchmark (8 s, max iterations, measurement of iter/s of the selected
  engine with CPU fallback), result in large (Giter/s, Miter/s…) with a progress
  bar and cancel. Files: `BenchmarkForm.cs` + `.Designer.cs` (new),
  `Mandelbrot.cs` (`BenchmarkCpu`), `GpuMandelbrot.cs` (`BenchmarkGpu`),
  `MandelbrotForm.*` (menu item).

- Preview during pan (half per side) with bilinear upscale, full render on
  release; state marked "(preview)". Files: `MandelbrotForm.cs`
  (`RenderAsync(preview)`, `Upscale`).

- Antialias with checkbox 1x/2x/4x/8x: k times higher resolution + RGB
  average of each kxk block (same logic on CPU with `AverageBlock` and on GPU);
  auto iterations from radio to checkbox (default manual); default window
  1152x720. Verified with smoke test (CPU 1x vs 2x, GPU 2x vs CPU 2x). Files:
  `Mandelbrot.cs`, `GpuMandelbrot.cs`, `MandelbrotForm.cs`,
  `MandelbrotForm.Designer.cs`. Auto iter formula `200 + 790·log10(zoom)`
  (~2000 at scale 1.95e-4, ~4550 at scale 1e-5).

## v1.4 and earlier — First CUDA backend and project setup
- **v1.4** — CUDA backend with ILGPU (auto precision for scale < 1e-3), one
  thread per pixel, most capable device automatically, CPU fallback if no
  GPU, CUDA radio enabled at startup if the GPU responds; status and About
  show engine/precision/device. Refactor: shared `ColorFromEscape` between
  CPU and GPU. Verified with smoke test on RTX 5070 Ti (GPU iterations == CPU).
  New auto iter formula `200 + 430·log10(zoom)` (~2000 at scale 1.95e-4); fix
  Manual/CPU radio groups (separate container panels). Files:
  `GpuMandelbrot.cs` (new), `Mandelbrot.cs`, `MandelbrotForm.cs`,
  `MandelbrotForm.Designer.cs`, `RenderEngine.cs`, `.csproj` (ILGPU dependency).

- Engine selection with radios: `RenderEngine` enum + `RenderEngineInfo`; CUDA and
  DirectX disabled (roadmap v1.4/v2.0). Help moved to the `StatusStrip`,
  default window 1024x680, active engine shown in the status. Files:
  `RenderEngine.cs` (new), `MandelbrotForm.cs`, `MandelbrotForm.Designer.cs`.

- Renamed `Form1` to `MandelbrotForm` (view preserved). Files:
  `MandelbrotForm.cs`, `MandelbrotForm.Designer.cs`, `Program.cs`.
- Iteration count updated also in auto mode: shows the automatic
  value used for the render. Files: `Form1.cs`.

- Palette dropdown (Fire, Ice, Thermal) with dedicated gradients in
  `Mandelbrot.cs` (removed the `Palette` enum); top bar rebuilt with
  `TableLayoutPanel` so iteration count, label, radios and dropdown are
  vertically aligned; Auto/Manual radios for the iterations
  (`AutoIter = 150 + 150·log10(zoom)`, clamp 50–5000, count disabled in
  auto); status moved to the `StatusStrip` at the bottom. Files: `Form1.cs`,
  `Form1.Designer.cs`, `Mandelbrot.cs`.
- File menu (JSON zones, named image Ctrl+Shift+S) + Help menu with
  About (version and commands); zoom with the wheel on the cursor (automatic
  focus on mouse hover); single keys R/S/+/- ignored with Ctrl/Alt
  pressed. Files: `Form1.cs` (`ViewZone`, `SaveZone`, `LoadZone`, `ShowAbout`),
  `Form1.Designer.cs` (`MenuStrip`).
- Pan with dragging on button pressed (throttle 80 ms,
  click/drag threshold 5 px). Removed zoom on rectangle and zoom with wheel.
  Files: `Form1.cs`, `Form1.Designer.cs` (help text updated).
- Project setup (user-level SDK; self-contained publish in `published/` so
  the Desktop Runtime doesn't need to be installed; version in source (`AppVersion.cs`
  + `<Version>` in the csproj, shown in the title); project setup (AGENTS.md,
  TODO.md, SPECS.md, `.gitignore`, tracking in the `test/` repo).
- Initial C# viewer (WinForms): configurable iterations,
  PNG save. Files: `Form1.*`, `Mandelbrot.cs`.
