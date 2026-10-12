# Mandelbrot Viewer Specification

Mandelbrot Viewer is a Windows Forms application for exploring the Mandelbrot set and Julia sets. It targets .NET 8 on Windows and renders the escape-time fractal with the CPU, CUDA, or DirectX engine selected by the user.

This document describes the current application behavior and is the source of truth for its user-visible features and active execution contracts.

## Fractal Rendering

- Mandelbrot mode renders `z = z² + c` from `z = 0`.
- Julia mode renders `z = z² + c` with a fixed complex constant `c`; the default is `-0.7 + 0.27015i`.
- Escaping points use smooth iteration coloring. Interior points are black.
- The smoothed escape iteration count is normalized to the iteration limit and mapped with `pow(value, 0.35)` before interpolation.
- Palettes are `Fire`, `Ice`, `Thermal`, `Ocean`, `Violet`, `Desert`, and `Forest`, each with five color stops shared by all renderers.
- The iteration limit is clamped to 50...50000.
- Automatic iterations use `2000 * (1 + log10(1.5 / half))`, where `half = scale / 2`, clamped to 50...50000. The same calculation is used by the main view and zoom video.

## Navigation and Commands

- Left click and right click zoom by 2x around the clicked point.
- The mouse wheel zooms by `0.7x` or `1/0.7x` around the cursor.
- Dragging pans the view. A movement threshold of 5 pixels distinguishes a drag from a click.
- Arrow keys pan by one tenth of the view. Shift plus an arrow key pans by one hundredth. Arrow panning is ignored while the iterations control has focus.
- `R` resets the view to the full set. In Julia mode it also resets `c`.
- `S` saves a PNG. `+` and `-` change the iteration limit by 50.
- In Julia mode, clicking fixes the Julia constant to the clicked point.
- During drag and resize previews, rendering uses one-quarter of the output pixels, no antialiasing, and bilinear image scaling. The complete render is produced when the operation ends. Pan updates are throttled to 80 ms.
- Resizing starts an immediate preview redraw and preserves the image aspect ratio during the transition.
- Every zoom, pan, reset, and loaded zone is added to history. Back and Forward support `Alt+Left` and `Alt+Right` and retain up to 200 views.

## Antialiasing

The view offers `1x`, `2x`, `4x`, and `8x` antialiasing. `1x` disables antialiasing. Other values compute a `k x k` supersampling grid per output pixel and average the samples on the active renderer. Work is approximately proportional to `k^2`; no full `kW x kH` image buffer is allocated.

High-resolution PNG export uses the selected antialiasing without automatic reduction. Tiled rendering preserves global coordinates and sample order, so the result matches a whole-image render.

## Rendering Engines

### CPU

- Always available. Precision follows the 32/64-bit setting like the CUDA
  engine, with no fallback: 64-bit renders everything in double precision,
  32-bit renders everything in single precision (Mandelbrot and Julia, any
  zoom; less precise at deep zoom, by user choice). Default is 64-bit.
- Uses parallel SIMD computation and locked bitmap access.
- Supports Mandelbrot and Julia rendering, supersampling, cancellation, and tiled export.

### CUDA

- Available on NVIDIA hardware; the application falls back to CPU rendering when CUDA is unavailable.
- Native backend (no runtime compiler framework): the four kernels in
  `Cuda/mandelbrot.cu` (float/double render + float/double iterations-only
  benchmark, with the cardioid/bulb test, smooth log/log coloring through
  the cached palette table, and on-chip SSAA) are compiled to PTX at build
  time and embedded in the executable; the C# side drives them through a
  minimal `nvcuda.dll` driver-API binding (`CudaNative.cs`). End-user
  machines need only the NVIDIA driver, never the CUDA toolkit.
- Explicit wave allocation: all launches (render and benchmark) use a 2D
  grid (`grid = ceil(W/bx) x ceil(H/by)`, thread (x, y) maps directly to
  sample (x, y) with no index division; 32-wide blocks keep warps on single
  rows) with a tuned threads-per-block per precision (default 32x8 = 8 warps). At
  initialization, after a warmup (steady clocks, compiled kernels), a probe
  on the real benchmark grid at reduced iterations times 32x4/32x8/32x16/32x32
  interleaved (median of 5 rounds); the winner replaces 32x8 only with a
  clear margin, so noise cannot flip the layout between runs. The log window
  reports SM count, compute capability, chosen blocks, and resident
  blocks/SM. PTX is built with FMA
  contraction enabled (speed over CPU bit-identity): output matches the CPU
  except for rare single-pixel flips on chaotic boundaries (a few per mille;
  32-bit deep zoom amplifies them to sparse speckle, within the documented
  imprecision of the 32-bit path there).
- Supports 32-bit float and 64-bit double precision, with 64-bit as the default.
- Performs fractal computation, coloring, and antialiasing on the GPU, then transfers the final bitmap to the host.
- Reuses device and host buffers between frames where possible.
- Rendering and CUDA benchmark operations are serialized through one gate.

### DirectX 11

- Uses a Vortice Direct3D 11 shader with float precision.
- Provides realtime rendering with a 16 ms update timer and immediate pan/zoom updates.
- Uses a fullscreen triangle and on-chip supersampling. PNG capture reads the rendered backbuffer or an offscreen render target.
- Falls back to CPU rendering if initialization or rendering fails.
- At very small scales, float precision is insufficient; the status identifies this condition and CPU or CUDA double precision should be used.

The GPU selector lists available CUDA devices and DirectX adapters. `Auto` selects the most capable CUDA device or the default DirectX adapter. A chosen device is persisted and applied when the engine changes. If an engine is unavailable, the reason is shown in the status bar.

## Optimizations

Each item is tagged with its scope: benchmark only, interactive and export
rendering, or both. The benchmark kernels of both engines run the identical
incremental-squares escape loop, so the benchmark compares engine overhead,
not different math.

### CUDA

- Iterations-only benchmark kernels (no coloring, smoothing, or sample
  averaging) with reused device buffers [benchmark].
- 2D launch grid for every kernel: thread (x, y) maps directly to its
  sample, with no index division; 32-wide blocks keep warps on single rows
  so writes stay coalesced [both].
- Autotuned block layout: at initialization a probe on the real benchmark
  grid times 32x4/32x8/32x16/32x32 interleaved (median of 5 rounds after
  warmup) and keeps 32x8 without a clear winner [both, tuned on the
  benchmark workload].
- Adaptive launch batching: several kernels are queued per synchronization
  (4 to 256, capped at ~0.8 s of queued work against TDR), so fast GPUs are
  measured on compute throughput, not submit latency [benchmark].
- Full render on the GPU (escape, smooth coloring, palette lookup, and
  antialiasing downsampling per thread) with only the final bitmap
  transferred to the host; device and host buffers are reused between frames
  [interactive and export].
- On-chip supersampling: threads loop over their `k x k` subsamples, so VRAM
  stays O(W x H) and the `W*k x H*k` grid is never materialized
  [interactive and export].
- Main cardioid + period-2 bulb early-out in the render kernels (Mandelbrot
  mode only); the benchmark kernels skip it to keep the measured workload
  identical across engines [interactive and export].
- Cached 4096-entry device palette table with linear interpolation, uploaded
  once per palette/iteration combination and shared by all frames
  [interactive and export].
- PTX built with FMA contraction enabled (speed over CPU bit-identity;
  rare single-pixel flips on chaotic boundaries) [both].
- Rendering and benchmark operations serialized through one gate [both].

### DirectX 11

- Fullscreen triangle with one float pixel-shader invocation per pixel; the
  engine is float-only, so deep zoom needs CUDA or CPU double precision
  [both].
- On-chip supersampling in the shader, same O(W x H) memory behavior as
  CUDA [interactive and export].
- Palette through a 1D texture holding the same 4096 entries as the CPU
  table, with hardware linear filtering between entries [interactive and
  export].
- Main cardioid + period-2 bulb early-out in the render shader (Mandelbrot
  mode only); the benchmark shader omits it like its CUDA counterpart
  [interactive and export].
- Realtime main view on a 16 ms timer with immediate pan/zoom updates;
  resize stretches the swapchain image during the drag and renders full
  quality on release [interactive].
- Benchmark on an offscreen target in the tested GPU's memory with no
  `Present`, so DWM composition and cross-GPU copies are excluded
  [benchmark].
- Benchmark pipeline state (constants, shaders, render target) set once;
  per frame only `Draw` plus an event-query marker, with a deep in-flight
  ring (4 to 256, capped at ~0.8 s of queued work) and completion counted
  through the queries [benchmark].
- Iterations-only benchmark shader with no coloring, smoothing, or
  averaging [benchmark].
- PNG capture and previews via a staging-texture copy of the render target
  [interactive and export].

## Menus and Export

### File

- Load and save zone JSON with `Ctrl+O` and `Ctrl+S`.
- Save the current image with `Ctrl+Shift+S`.
- Open the benchmark with `Ctrl+B`.
- Exit with `Alt+F4`.

A zone stores center, scale, iterations, Julia mode, and Julia constant.

### Generate

PNG export uses the current view, Full HD, 2K, 4K, 8K, Double 4K, or Custom presets. Custom width and height must be integers from 320 through 16384. The dialog selects antialiasing and, when CUDA or CPU is active, 32-bit or 64-bit precision. CPU and CUDA export follow the dialog precision choice; DirectX export always uses float precision. Rendering is cancellable and uses 512x512 working tiles.

Zoom video export renders a transition from the current view to the full set and is unavailable when already at the full set. It keeps the starting point in frame, uses logarithmic scale interpolation with ease-out timing, and computes automatic iterations for every frame. The dialog supports 60...480 frames, 24/30/60 fps, selected antialiasing, and the CUDA/CPU precision choice. Frames are rendered at the view resolution.

When `ffmpeg` is available on `PATH`, frames are encoded as H.264 MP4. Odd dimensions are padded to even dimensions for `yuv420p`. Without `ffmpeg`, the PNG sequence is retained and can be opened from the dialog. Encoding runs off the UI thread and is cancellable.

Back to set (`Ctrl+Shift+R`) animates the current main view to the full set in 120 frames at 30 fps. `Esc` stops the animation.

### View

The View menu provides history navigation, named favorite zones, and Julia mode (`Ctrl+J`). Favorites are stored as JSON files under `%APPDATA%\MandelbrotViewer\zone\`; they can be opened or deleted from the menu. Julia mode and its constant are included in saved zones.

### Help and Status

About displays the application version and command list. The log/diagnostics window displays engine status, detected devices, settings, errors with their operation, and the in-memory event log.

The status bar shows the center, view width, iterations, palette, engine, Julia mode, antialiasing, and preview state. Controls and menu items provide tooltips. The window starts at 1152x720, persists its position, size, and maximized state, and displays the application version in its title.

Asynchronous renders keep the UI responsive. The `AppStarting` cursor is shown while an asynchronous render is active and is restored only by the latest render. Synchronous CUDA initialization may temporarily freeze the UI.

## Settings

Settings are stored in `%APPDATA%\MandelbrotViewer\settings.json`.

Persisted settings include automatic/manual iterations and value, palette, antialiasing, engine, GPU selection, CUDA precision, Julia mode and constant, and window placement. Settings loading tolerates a missing or corrupt file; saved iterations are validated and clamped to 50...50000. The current view is not persisted; each launch starts at the full set.

## Benchmark

The benchmark measures the same iterations-only workload for all engines:

- Area: 960x540.
- Antialiasing: `1x`, one elementary sample per output pixel without coloring or sample averaging.
- Center: `(-0.7499302568795561, -0.015139113925433963)`.
- Scale: `1.0453474311811176e-04`.
- Iterations: the automatic value for this scale, currently 10915.
- Budget: 8 seconds per run.
- Metric: elementary samples per second, displayed as MPixel/s.

The CPU benchmark accumulates escape iterations. CUDA uses an iterations-only kernel with reused buffers and batched synchronization. DirectX uses an offscreen iterations-only shader with event-query completion tracking. Cancellation, timeout, and device-removal failures are reported instead of being treated as successful measurements.

The CPU benchmark runs in double precision by default; `--bench-cpu float` runs the float workload instead (own history, like CUDA 32-bit vs 64-bit). The benchmark window follows the precision setting for CPU and CUDA (64-bit double by default).

The benchmark window renders a colored preview before measuring, reports progress approximately once per second, and displays the current result with stored history. Results are ordered by MPixel/s. CSV export writes one row per run containing timestamp, engine, device, precision, run, frame, seconds, and MPixel/s.

## Command-Line Diagnostics

The executable supports these non-UI commands:

- `--bench-dx [card] [--csv file]`
- `--bench-cuda [device] [--csv file]`
- `--bench-cpu [float] [--csv file]`
- `--diag-dx [card]`
- `--diag-gpu`

The optional card or device argument selects one target; without it, available targets are enumerated. `--csv file` queues benchmark rows in the same format as the benchmark window export.

## Application Status

The current application version is defined by `AppVersion` and the project file.

`SPEC_FOR_LLM.md` (project root) is a single self-contained replication
specification: full architecture, all algorithms and constants, engine
contracts (CPU/CUDA/DirectX, including the HLSL and CUDA kernel semantics),
UI behavior, persistence schemas, and an acceptance checklist, written so an
LLM can reproduce the application as faithfully as possible (docs only, no
version bump).

`SPEC_FOR_LLM_PLATFORM_AGNOSTIC.md` (project root) is the fully
platform-agnostic variant: the same spec with **no reference to any
particular platform** — no operating system, no UI toolkit, no GPU
technology — and an explicit freedom-of-implementation clause (the
destination LLM chooses target OS, UI toolkit, GPU API, and packaging; only
the behavioral contracts, formulas, and constants bind). The GPU engine is
described purely as an interface and behavior contract, and the acceptance
checklist carries OS-agnostic portability expectations (docs only, no
version bump). Documentation is kept separate from release history, development tasks, generic questions, and repository workflow instructions.

Release v2.24.1 is published on GitHub with the self-contained
`MandelbrotViewer.exe` asset (v2.24.1, ~155 MB); the README links to the latest release download.
A Windows installer `MandelbrotViewer-Setup-2.22.7.exe` (~47 MB, Inno Setup,
LZMA2, EN/IT wizard, admin scope, Start-menu + optional desktop shortcut,
post-install launch option) is attached to the same GitHub release and built from a fresh `published\` self-contained
build via `MandelbrotViewer-Setup-2.22.7.iss` (AppId
`{B88299BB-C271-4C1A-90EC-D290CF499055}` — reuse it for upgrades);
`*.pdb` excluded. Build note: the setup-generator script mangles `#` in
`SetupIconFile` (`MandelC##`) and writes backslashes into the URL — fix both
by hand, then compile with ISCC (`MSYS2_ARG_CONV_EXCL='*'` under Git Bash
so `/O` is not path-mangled).

v2.19.2 render notes: the CPU path colors through a cached 4096-entry palette
table with linear interpolation (v2.19.3; output within 1 LSB of the exact
coloring), skips known-interior points via the main cardioid + period-2 bulb
test (Mandelbrot mode only, also applied to the CPU benchmark), and writes the
bitmap directly without an intermediate buffer. Since v2.19.4 the escape loop
runs on SIMD vectors and rows are scheduled in chunks. Since v2.21.0 the CPU
precision is explicit (32/64-bit setting, no fallback): 64-bit is double
everywhere (bit-identical to scalar), 32-bit is float everywhere including
Julia and deep zoom. Since v2.20.1 the GPU render kernels
sample the same cached 4096-entry palette table (CUDA device buffer, DirectX
texture with linear filtering) and skip known-interior points via the
cardioid/bulb test in Mandelbrot mode; GPU benchmark kernels are
iterations-only and unchanged.

Source review (no code change, no version bump): ponytail over-engineering
pass found one dead path (`Mandelbrot.ColorFromEscape` + `PaletteColors.ColorFor`,
zero callers — "kept for reference"), one ignored parameter (`maxIter` in
`PaletteColors.GetLut`, never read yet part of the cache key in three places),
and three duplications worth shrinking (BenchmarkCpu/BenchmarkCpuFloat frame
loop, `RenderPreviewToBitmap` 7-arg forwarder, `DeviceShortName`/`ShortAdapterName`).
Details in chat; nothing applied.

Full-repo audit (no code change, no version bump): same verdicts, repo-wide —
biggest item is docs duplication (`SPEC_FOR_LLM.md` vs
`SPEC_FOR_LLM_PLATFORM_AGNOSTIC.md`, ~1200 lines each), followed by the
BenchmarkCpu/BenchmarkCpuFloat frame-loop duplication and the
BenchDx/BenchCuda/BenchCpu triplication in `Diagnostics.cs`. No dependency
bloat found (hand-rolled `CudaNative` P/Invoke instead of a wrapper package is
the lean choice). Findings in chat; nothing applied.

v2.22.8 applied the audit (code only): dead `ColorFromEscape`/`ColorFor`
deleted, `maxIter` dropped from the LUT cache key, CPU benchmark loops merged
(`RunCpuBenchmark` + scalar row functions), CLI trial loops merged
(`RunTrials`), `CopyTile` → `DrawImageUnscaled`, wrapper/alias one-liners
removed. `SPEC_FOR_LLM.md` kept: the duplication flag was rejected on review —
it is the Windows-specific replication contract (backend names, paths, WinForms),
while `SPEC_FOR_LLM_PLATFORM_AGNOSTIC.md` delegates all implementation choices;
merging them would destroy one of the two purposes. Verified with a clean build
(0 warnings), `--diag-gpu` CUDA render, and `--bench-cpu float`.

v2.24.1 shows the active CPU SIMD path everywhere via
`Mandelbrot.CpuSimdLabel` (`AVX512`/`AVX2`/`SSE2`/`scalar`, sharing the
`CpuUseAvx512` dispatch flag): main status bar, About/log engine line,
diagnostics log, benchmark/export/video labels, `--bench-cpu` header.

v2.24.0 added a native AVX512 CPU kernel: `Vector512` escape cores (8
doubles / 16 floats per block) with matching row and benchmark blocks,
dispatched when `Avx512F` is supported, 256-bit fallback otherwise. Same op
order and lane formulas (double bit-identical to scalar; verified 0 pixel
diffs and identical benchmark sums). Best-of-3 Release on Ryzen 9 9900X:
double 9.7 → 13.8 MPixel/s (+42%), float 15.2 → 21.3 (+40%); history bars
remeasured. `.NET 8 note: Vector512<T>` has no public constructors —
broadcast via `Vector512.Create(x)`, span loads via
`Vector512.LoadUnsafe(ref span[0])`.

v2.23.0 removed the Rust engine (`RustMandelbrot.cs` + `mandel_ffi.dll` FFI,
`RenderEngine.Rust`, toolbar radio, all view/export/video/benchmark branches;
old `Engine=Rust` settings fall back to CPU; `publish.ps1` ships only the
single-file exe). Rationale: one C# SIMD core to evolve toward a native
AVX512 Zen5 kernel (plan in TODO). Verified with a clean build (0 warnings,
0 errors).

Launcher fix (no code change, no version bump): `run.bat` hardcoded
`%USERPROFILE%\.dotnet\dotnet.exe`, but this machine has dotnet in
`C:\Program Files\dotnet` on PATH — now PATH first, user path as fallback
(same fallback added to `publish.ps1`, AGENTS.md toolchain line corrected).
Two self-inflicted issues found while verifying: a `)` inside the batch `echo`
text prematurely closed the if-block (`. non atteso.`, fell into the error
branch), and the rewritten file had LF-only endings cmd cannot parse — both
fixed; verified with `run.bat --diag-gpu` (exit 0, CUDA render OK).
