# Mandelbrot Viewer Specification

Mandelbrot Viewer is an interactive application for exploring the Mandelbrot
set and Julia sets. It renders the escape-time fractal with one of three
engines selected by the user: a multicore CPU engine, a GPU compute engine,
or a realtime GPU engine.

This document is platform-agnostic: it specifies behavior, formulas,
constants, and engine contracts without naming any operating system, UI
toolkit, programming language, processor instruction set, or GPU technology.
The implementer chooses all of those; only the contracts below bind. A
separate repository history (changelog) records how the reference
implementation evolved — it is not part of this contract.

## Fractal Rendering

- Mandelbrot mode renders `z = z² + c` from `z = 0`.
- Julia mode renders `z = z² + c` with a fixed complex constant `c`; the default is `-0.7 + 0.27015i`.
- Escaping points use smooth iteration coloring. Interior points are black.
- The smoothed escape iteration count is normalized to the iteration limit and mapped with `pow(value, 0.35)` before interpolation.
- Palettes are `Fire`, `Ice`, `Thermal`, `Ocean`, `Violet`, `Desert`, and `Forest`, each with five color stops shared by all renderers.
- The iteration limit is clamped to 50...50000.
- Automatic iterations use `2000 * (1 + log10(1.5 / half))`, where `half = scale / 2`, clamped to 50...50000. The same calculation is used by the main view and zoom video.

## Navigation and Commands

- Primary/secondary click zoom by 2x around the clicked point.
- The mouse wheel zooms by `0.7x` or `1/0.7x` around the cursor.
- Dragging pans the view. A movement threshold of 5 pixels distinguishes a drag from a click.
- Arrow keys pan by one tenth of the view. Shift plus an arrow key pans by one hundredth. Arrow panning is ignored while the iterations control has focus.
- `R` resets the view to the full set. In Julia mode it also resets `c`.
- `S` saves a PNG. `+` and `-` change the iteration limit by 50.
- In Julia mode, clicking fixes the Julia constant to the clicked point.
- During drag and resize previews, rendering uses one-quarter of the output pixels, no antialiasing, and bilinear image scaling. The complete render is produced when the operation ends. Pan updates are throttled to 80 ms.
- Resizing starts an immediate preview redraw and preserves the image aspect ratio during the transition.
- Every zoom, pan, reset, and loaded zone is added to history. Back and Forward support `Alt+Left` and `Alt+Right` and retain up to 200 views.
- Key bindings above are the reference defaults; a port maps them to the platform-standard equivalents.

## Antialiasing

The view offers `1x`, `2x`, `4x`, and `8x` antialiasing. `1x` disables antialiasing. Other values compute a `k x k` supersampling grid per output pixel and average the samples on the active renderer. Work is approximately proportional to `k^2`; no full `kW x kH` image buffer is allocated.

High-resolution PNG export uses the selected antialiasing without automatic reduction. Tiled rendering preserves global coordinates and sample order, so the result matches a whole-image render.

## Rendering Engines

### CPU

- Always available. Precision follows an explicit 32/64-bit setting with no
  fallback: 64-bit renders everything in double precision, 32-bit renders
  everything in single precision (Mandelbrot and Julia, any zoom; less
  precise at deep zoom, by user choice). Default is 64-bit.
- Uses all available cores with row-chunk scheduling, and the widest SIMD
  vector width the processor offers for the escape loop (double path
  bit-identical to scalar regardless of block width; float path
  lane-consistent with its scalar tail), with scalar fallback where no SIMD
  exists. The active SIMD path is reported to the user (status bar,
  benchmark, export, video, log, CLI) so it is always clear which core
  computed the image.
- Supports Mandelbrot and Julia rendering, supersampling, cancellation, and tiled export.

### GPU compute

- Available where a programmable GPU and its driver exist; the application
  falls back to CPU rendering otherwise.
- Kernels are precompiled at build time and shipped with the application
  (float/double render + float/double iterations-only benchmark, with the
  cardioid/bulb test, smooth coloring through the cached palette table, and
  on-chip supersampling); end-user machines need only the GPU driver, never
  a GPU toolkit.
- Thread-to-sample mapping is a 2D grid with no index division; the
  threads-per-block layout per precision is autotuned once at
  initialization on the real benchmark grid (median of repeated rounds, kept
  only on a clear margin so noise cannot flip it).
- Kernels may use fused multiply-add (speed over CPU bit-identity): output
  matches the CPU except for rare single-pixel flips on chaotic boundaries
  (32-bit deep zoom amplifies them to sparse speckle, within the documented
  imprecision of the 32-bit path there).
- Supports 32-bit float and 64-bit double precision, with 64-bit as the default.
- Performs fractal computation, coloring, and antialiasing on the GPU, then transfers the final bitmap to the host.
- Reuses device and host buffers between frames where possible.
- Rendering and benchmark operations are serialized through one gate.

### Realtime GPU

- Uses a float-precision pixel shader.
- Provides realtime rendering on a ~16 ms update timer with immediate pan/zoom updates.
- Renders with on-chip supersampling. PNG capture reads the rendered frame or an offscreen render target.
- Falls back to CPU rendering if initialization or rendering fails.
- At very small scales, float precision is insufficient; the status identifies this condition and CPU or GPU-compute double precision should be used.

The GPU selector lists available devices. `Auto` selects the most capable one. A chosen device is persisted and applied when the engine changes. If an engine is unavailable, the reason is shown in the status bar.

## Optimizations

Each item is tagged with its scope: benchmark only, interactive and export
rendering, or both. The benchmark paths of all engines run the identical
incremental-squares escape loop, so the benchmark compares engine overhead,
not different math.

- Iterations-only benchmark paths (no coloring, smoothing, or sample
  averaging) with reused buffers [benchmark].
- 2D thread-to-sample mapping for every GPU kernel, with no index division
  [both].
- Autotuned GPU block layout, probed once at initialization [both, tuned on
  the benchmark workload].
- Deep batching / in-flight frames (bounded so as not to stall the display
  driver watchdog), so fast GPUs are measured on compute throughput, not
  submission latency [benchmark].
- Full render on the GPU (escape, smooth coloring, palette lookup, and
  antialiasing downsampling) with only the final bitmap transferred to the
  host; buffers reused between frames [interactive and export].
- On-chip supersampling: each output unit loops over its `k x k` subsamples,
  so memory stays O(W x H) and the `W*k x H*k` grid is never materialized
  [interactive and export].
- Main cardioid + period-2 bulb early-out in the render paths (Mandelbrot
  mode only); the benchmark paths omit it to keep the measured workload
  identical across engines [interactive and export].
- Cached 4096-entry palette table with linear interpolation, uploaded once
  per palette/iteration combination and shared by all frames
  [interactive and export].
- Realtime-engine benchmark on an offscreen target in the tested GPU's
  memory, excluding presentation/composition and cross-GPU copies
  [benchmark].
- Rendering and benchmark operations serialized through one gate [both].

## Menus and Export

### File

- Load and save zone JSON.
- Save the current image as PNG.
- Open the benchmark.
- Exit.

A zone stores center, scale, iterations, Julia mode, and Julia constant.

### Generate

PNG export offers the current view, Full HD, 2K, 4K, 8K, Double 4K, or Custom presets. Custom width and height must be integers from 320 through 16384. The dialog selects antialiasing and, when the compute engines are active, 32-bit or 64-bit precision. Rendering is cancellable and uses 512x512 working tiles.

Zoom video export renders a transition from the current view to the full set and is unavailable when already at the full set. It keeps the starting point in frame, uses logarithmic scale interpolation with ease-out timing, and computes automatic iterations for every frame. The dialog supports 60...480 frames, 24/30/60 fps, selected antialiasing, and the compute-engine precision choice. Frames are rendered at the view resolution.

When an `ffmpeg` executable is available on the system path, frames are encoded as H.264 MP4. Odd dimensions are padded to even dimensions for `yuv420p`. Without `ffmpeg`, the PNG sequence is retained and can be opened from the dialog. Encoding runs off the UI thread and is cancellable.

Back-to-set animates the current main view to the full set in 120 frames at 30 fps and is stoppable.

### View

The View menu provides history navigation, named favorite zones, and Julia mode. Favorites are stored as JSON files under the platform application-data directory; they can be opened or deleted from the menu. Julia mode and its constant are included in saved zones.

### Help and Status

About displays the application version and command list. The log/diagnostics window displays engine status, detected devices, settings, errors with their operation, and the in-memory event log.

The status bar shows the center, view width, iterations, palette, engine (including the CPU SIMD path in use), Julia mode, antialiasing, and preview state. Controls and menu items provide tooltips. The window persists its position, size, and maximized state, and displays the application version in its title.

Asynchronous renders keep the UI responsive. A busy cursor is shown while an asynchronous render is active and is restored only by the latest render.

## Settings

Settings are stored as JSON in the platform application-data directory.

Persisted settings include automatic/manual iterations and value, palette, antialiasing, engine, GPU selection, compute-engine precision, Julia mode and constant, and window placement. Settings loading tolerates a missing or corrupt file; saved iterations are validated and clamped to 50...50000. The current view is not persisted; each launch starts at the full set.

## Benchmark

The benchmark measures the same iterations-only workload for all engines:

- Area: 960x540.
- Antialiasing: `1x`, one elementary sample per output pixel without coloring or sample averaging.
- Center: `(-0.7499302568795561, -0.015139113925433963)`.
- Scale: `1.0453474311811176e-04`.
- Iterations: the automatic value for this scale, currently 10915.
- Budget: 8 seconds per run.
- Metric: elementary samples per second, displayed as MPixel/s.

The CPU benchmark accumulates escape iterations (double precision by default; a float mode runs the float workload instead, with its own history). Each GPU engine runs an equivalent iterations-only path. Cancellation and device failures are reported instead of being treated as successful measurements.

The benchmark window renders a colored preview before measuring, reports progress approximately once per second, and displays the current result with stored history. Results are ordered by MPixel/s. CSV export writes one row per run containing timestamp, engine, device, precision, run, frame, seconds, and MPixel/s. History bars are remeasured per engine/precision on reference hardware; a port establishes its own bars with the same protocol (best of 3 standardized runs).

## Command-Line Interface

The executable supports these non-UI commands:

- One benchmark command per engine (CPU with optional float mode), each with an optional device/card selector and CSV output.
- One diagnostics command per GPU engine, verifying initialization and rendering.

The optional device argument selects one target; without it, available targets are enumerated. CSV output appends benchmark rows in the same format as the benchmark window export.

## Portability Contract

- Only this document's formulas, constants, workloads, and behavior bind.
  Everything else — language, UI toolkit, OS packaging, installers, file
  locations beyond the JSON schemas, fonts, default window metrics, SIMD
  instruction sets, GPU APIs, kernel languages, device identification
  mechanisms — is the implementer's choice.
- Floating-point latitude: the double-precision CPU paths are bit-identical
  by construction (same op order); GPU and 32-bit paths may differ by rare
  single boundary pixels as documented above. A replica passes when its
  double CPU output is pixel-identical to the reference on the acceptance
  views and its other engines stay within the stated tolerances.

## Acceptance Checklist

- Full-set view plus a deep-zoom view render pixel-identical (double CPU)
  between whole-frame and tiled/export paths at AA 1x/2x/4x/8x.
- Julia mode matches at the default constant and at a user-set constant.
- Auto iterations equal `2000 * (1 + log10(1.5 / half))` clamped
  50...50000 (10915 at the benchmark scale).
- History back/forward restores exact views (200 cap); favorites round-trip
  through JSON; corrupt/missing settings fall back to defaults.
- Benchmark completes the 8 s protocol on every available engine and
  reports MPixel/s with history and CSV output.
- Zoom video produces the requested frame count and a playable MP4 when an
  encoder is present, else retains the PNG sequence.
- Unavailable engines fall back to CPU with the reason shown; cancellation
  never freezes the UI.
