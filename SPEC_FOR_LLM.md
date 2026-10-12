# Mandelbrot Viewer (MandelC) — Replication Specification

This document is a complete, self-contained specification of the Mandelbrot
Viewer application. It is written for an LLM agent whose task is to **replicate
the application as faithfully as possible** in a new codebase: same user-visible
behavior, same algorithms, same constants, same engine architecture. Follow the
sections in order. Where a value is given, use exactly that value. Where a
contract is stated (input/output/return), your implementation must honor it.

How to read this spec:

- Sections 1-2 define the project and the shared mathematical model.
- Sections 3-5 specify the three rendering engines (CPU, CUDA, DirectX).
- Section 6 specifies the main window and all interactive behavior.
- Sections 7-8 specify the auxiliary dialogs, persistence, and CLI.
- Section 9 is an acceptance checklist: after implementing, verify each item.

---

## 1. Project Overview

### 1.1 What the application is

An interactive Mandelbrot/Julia fractal explorer for Windows. The user can
zoom/pan in real time, choose a color palette, an antialiasing level, a
precision (32/64-bit for CPU/CUDA), and one of three rendering engines:

- **CPU**: always available, multicore, SIMD-vectorized, 32- or 64-bit.
- **CUDA**: NVIDIA GPU via embedded PTX kernels driven through the
  `nvcuda.dll` driver API (no CUDA toolkit needed at runtime), float or double.
- **DirectX 11**: real-time shader rendering (float precision only) on any
  D3D11 hardware adapter.

Auxiliary features: standardized cross-engine benchmark window, high-resolution
tiled PNG export, zoom-video MP4 export (via ffmpeg), "back to set" animation,
view history, favorite zones (JSON files), settings persistence, log/diagnostics
window, and command-line diagnostics/benchmarks.

### 1.2 Tech stack (replicate with equivalent choices)

| Concern | Choice |
|---|---|
| Language / runtime | C# 12, .NET 8 (`net8.0-windows`) |
| UI | Windows Forms (`UseWindowsForms`), Windows only |
| Threading | `System.Threading.Tasks.Parallel` (partitioner), async/await, `CancellationToken` |
| SIMD | `System.Numerics.Vector` (double and float) with scalar fallback |
| Unsafe | `AllowUnsafeBlocks` for `LockBits` pointer writes |
| GPU (DX) | Vortice.Direct3D11 / Vortice.DXGI / Vortice.D3DCompiler 3.8.3; HLSL shaders compiled at runtime with D3DCompiler (`vs_5_0` / `ps_5_0`) |
| GPU (CUDA) | Kernels in C (`Cuda/mandelbrot.cu`) compiled to PTX at build time with `nvcc --ptx -arch=compute_75`, PTX embedded as an assembly resource, loaded via `nvcuda.dll` driver API (cuModuleLoadDataEx path). End users need only the NVIDIA driver. If `nvcc` is absent at build time the checked-in PTX is kept (warning only). FMA contraction enabled (speed over CPU bit-identity). |
| JSON | `System.Text.Json` |
| Packaging | Self-contained single-file win-x64 publish; app icon `app.ico` embedded as a manifest resource |

Project-level settings: `OutputType=WinExe`, `Nullable=enable`,
`ImplicitUsings=enable`, `InvariantGlobalization=true`, version `2.22.7`
(`Major=2, Minor=22, Patch=7`; UI displays `X.Y` when `Patch==0`, else `X.Y.Z`).

### 1.3 Source file map

Each source file, its role, and its key public surface. A faithful replica
should keep the same module decomposition (one file per area listed below).

```
MandelbrotViewer/
├── Program.cs            Entry point. STAThread Main. Parses CLI commands
│                         (--diag-dx, --bench-dx, --bench-cuda, --bench-cpu,
│                         --diag-gpu; --bench-cpu takes "float" mode, all
│                         --bench-* accept optional device positional and
│                         "--csv file"). Applies embedded app.ico to any form.
├── AppVersion.cs         Version constants (2.22.7), Full and Display strings.
├── RenderEngine.cs       enum RenderEngine {Cpu, Cuda, DirectX} +
│                         RenderEngineInfo.IsAvailable / DisplayName.
├── AppLog.cs             In-memory ring of the last 200 timestamped log lines
│                         ("HH:mm:ss  message"), thread-safe.
├── Settings.cs           AppSettings record (JSON, see §8.1) + Load/Save with
│                         tolerance for missing/corrupt files, MaxIter clamp.
├── Palette.cs            Palette enum (order is the dropdown index, persisted)
│                         + PaletteColors: 7 palettes × 5 stops, LUT builder
│                         (4096 entries) and LUT color lookup.
├── Mandelbrot.cs         CPU engine: Render/RenderTile (4 code paths:
│                         scalar/vector × float/double), early-out test,
│                         AutoIterForScale, CPU benchmarks (double + float).
├── GpuMandelbrot.cs      CUDA host side: GpuViewParams (88-byte blittable
│                         struct), init/device selection, block autotune,
│                         render + benchmark, device LUT, gating, disposal.
├── CudaNative.cs         Minimal nvcuda.dll driver-API binding (context,
│                         module, function, alloc/copy, launch, sync,
│                         occupancy query). ~30 P/Invoke entry points.
├── Cuda/
│   ├── mandelbrot.cu     4 kernels: FloatKernel, DoubleKernel (render, on-chip
│   │                     SSAA + LUT + Julia + tile mapping), FloatBenchKernel,
│   │                     DoubleBenchKernel (iterations only).
│   └── mandelbrot.ptx    Generated artifact (embedded resource
│                         "MandelbrotViewer.Cuda.mandelbrot.ptx").
├── DxMandelbrot.cs       DirectX 11 backend: HLSL VS/PS/BenchPS sources,
│                         device/adapter selection, swapchain, palette LUT
│                         texture, real-time render, offscreen benchmark with
│                         event-query ring, capture, preview-to-bitmap.
├── MandelbrotForm.cs     Main window logic: navigation, mouse/keyboard,
│                         history, favorites, settings, engine switching,
│                         back-to-set animation, diagnostic text builder.
├── MandelbrotForm.Designer.cs  Main window layout (2-row toolbar, view
│                         panels, menu, status bar, tooltips).
├── BenchmarkForm.cs      Benchmark dialog: standard workload, live progress,
│                         colored preview, horizontal bar chart with historical
│                         reference bars, CSV append.
├── BenchmarkForm.Designer.cs
├── BenchmarkStandard.cs  Standard benchmark parameters shared by GUI and CLI.
├── BenchmarkProgress.cs  Progress record + 1 s report interval.
├── BenchmarkCsv.cs       CSV row format + append with header on first write.
├── ExportForm.cs         High-res PNG export dialog: presets, custom 320-16384,
│                         AA choice, precision, 512x512 tiled render.
├── ExportForm.Designer.cs
├── ZoomVideoForm.cs      Zoom video dialog: frames 60-480, fps 24/30/60,
│                         ffmpeg H.264 MP4 or PNG fallback sequence.
├── ZoomVideoForm.Designer.cs
├── Diagnostics.cs        CLI: --diag-dx (DXGI enumeration + init test),
│                         --diag-gpu (writes diag_gpu.txt), --bench-dx/cuda/cpu.
├── LogForm.cs            Read-only log/diagnostics dialog with copy button.
├── app.ico               Application icon (embedded + csproj ApplicationIcon).
└── MandelbrotViewer.csproj
```

Supporting documentation (not part of the replica, but the replica should keep
equivalents): `AGENTS.md` (workflow), `SPECS.md` (user-visible behavior),
`CHANGELOG.md`, `README.md`, `TODO.md`, `QUESTIONS.md`.

### 1.4 Build and run

- Build: `dotnet build` (SDK 8.0, Windows).
- Run (dev): `dotnet run --project MandelbrotViewer`.
- Publish: self-contained, single-file, win-x64, with native libraries
  extracted (`IncludeNativeLibrariesForSelfExtract=true`), output to `published/`.
- The CUDA PTX build target runs before build when nvcc is available and
  regenerates `Cuda/mandelbrot.ptx` from `Cuda/mandelbrot.cu`
  (`nvcc --ptx ... -arch=compute_75`); it is skipped with a warning otherwise.

### 1.5 Global invariants (all engines)

1. **Identical workload contract.** Every engine renders the exact same
   per-subsample math (same escape loop, same smooth formula, same early-out,
   same LUT entries) so engines are comparable. Benchmark kernels of all
   engines run the identical iterations-only escape loop (no coloring, no
   early-out in GPU benchmark kernels; the CPU benchmark *does* apply the
   cardioid/bulb early-out — this asymmetry is intentional and must be kept).
2. **Tiling invariance.** Tiled output must be pixel-identical to a
   whole-image render at the same view/AA because every engine computes
   subsample positions on the *global* k-times grid
   (`(offsetX + x) * k + sx`), never on a tile-local grid.
3. **Interior points are black** (`0xFF000000` ARGB, opaque).
4. **Palette mapping** `t = (smooth / maxIter)^0.35` is applied identically
   everywhere (gamma is baked into the LUT entries).
5. **Cancellation.** Every long operation is cancellable; superseded renders
   are aborted and must not corrupt the displayed image or the cursor.

---

## 2. Core Rendering Model (shared by all engines)

### 2.1 Fractal definitions

- **Mandelbrot mode**: iterate `z(0) = 0`, `z(n+1) = z(n)² + c`, where `c` is
  the complex coordinate of the point. If `|z|² > 4` within `maxIter` steps,
  the point is exterior; otherwise it is interior (renders black).
- **Julia mode**: same iteration but `z(0) =` the point coordinate and `c` is
  a fixed constant. Default `c = -0.7 + 0.27015i` (constants
  `DefaultJcx = -0.7`, `DefaultJcy = 0.27015`). Reset (`R`) also resets `c`
  to the default.
- **Escape loop form (mandatory, all engines)**: incremental-squares loop:

```text
zx2 = zx*zx; zy2 = zy*zy; iter = 0
while (iter < maxIter and zx2+zy2 <= 4):
    zy = 2*zx*zy + ccy
    zx = zx2 - zy2 + ccx
    zx2 = zx*zx; zy2 = zy*zy
    iter++
```

The operation order (`zy` before `zx`, then the squares) is part of the
bit-exactness contract of the CPU double path. Float paths and GPU paths may
differ by FMA contraction (documented: rare single-pixel flips on chaotic
boundaries).

### 2.2 View coordinate system

A view is `(centerX, centerY, scale)` where `scale` is the **complex width**
of the whole image (complex units). The complex height is derived from the
bitmap aspect ratio, so the image never stretches.

Given output size `W x H`, antialias factor `k`, whole-image size `fullW x
fullH`, and tile offset `(offsetX, offsetY)`:

```text
pixelSize = scale / (fullW * k)
topY      = centerY - (fullH * k * 0.5) * pixelSize
originX   = centerX - (fullW * k * 0.5) * pixelSize
```

Subsample `(globalBx, globalBy)` of output pixel `(x, y)` maps to complex
`(originX + globalBx * pixelSize, topY + globalBy * pixelSize)` where
`globalBx = (offsetX + x) * k + sx`, `globalBy = (offsetY + y) * k + sy`,
`0 <= sx, sy < k`. The DirectX engine expresses the same mapping with
`invW = 1/fullW`, `invH = 1/fullH`, `aspect = fullH/fullW`:
`px = cx + (p.x*invW - 0.5)*scale`, `py = cy + (p.y*invH - 0.5)*scale*aspect`
with sub-pixel offset `sub = ((jx,jy)+0.5)/k - 0.5`.

Initial view ("full set"): `centerX = -0.5`, `centerY = 0.0`,
`scale = 9.36` (re in [-2.68, 1.68], im in [-2.34, 2.34]).

### 2.3 Iteration limits

- Manual limit clamped to **50 ... 50000** (UI numeric: Min 50, Max 50000,
  Increment 50, default value 256). `+`/`-` keys step by 50.
- **Automatic iterations** (single source for view, zoom video, benchmark):

```text
AutoIterForScale(scale) = clamp( 2000 * (1 + log10(1.5 / (scale/2))), 50, 50000 )
```

  (i.e. 2000 at the initial view, +2000 per 10x of zoom; ≈1944 at scale 9.36,
  10915 at the benchmark scale). Guard: `half = max(epsilon, scale*0.5)`.

### 2.4 Smooth coloring and palette LUT

For an escaped point with iteration count `iter < maxIter` and squared
modulus `mod = zx2 + zy2` at escape (clamped to `>= 4`):

```text
smooth = iter + 1 - log2( 0.5 * ln(mod) )
```

(the float path uses `iter + 1f - log2f(0.5f*logf(mod))`; this equals the
legacy `iter + 1 - ln(ln(sqrt(mod)))/ln(2)` with no sqrt). Interior
(`iter >= maxIter`) → opaque black `0xFF000000`.

**Palette mapping** (identical everywhere, gamma baked into the LUT):
`t = clamp(smooth / maxIter, 0, 1)^0.35`.

**Palettes** (enum order = dropdown index = persisted index; add only at end):
`0 Fire, 1 Ice, 2 Thermal, 3 Ocean, 4 Violet, 5 Desert, 6 Forest`.
Each palette has exactly five `(t, R, G, B)` stops at t = 0.00, 0.25, 0.50,
0.75, 1.00:

| t | Fire | Ice | Thermal | Ocean | Violet | Desert | Forest |
|---|---|---|---|---|---|---|---|
| 0.00 | (0,0,0) | (0,0,0) | (0,0,0) | (0,0,0) | (60,0,80) | (0,0,0) | (55,32,12) |
| 0.25 | (170,20,0) | (0,35,110) | (75,10,105) | (0,40,95) | (210,0,190) | (95,50,10) | (115,78,32) |
| 0.50 | (255,120,0) | (0,130,220) | (195,25,85) | (0,120,175) | (120,40,220) | (185,110,40) | (95,125,45) |
| 0.75 | (255,220,90) | (130,215,255) | (250,135,30) | (85,200,225) | (0,200,230) | (235,190,110) | (60,170,70) |
| 1.00 | (255,255,225) | (240,255,255) | (252,250,180) | (235,250,255) | (210,255,255) | (255,245,220) | (205,235,175) |

**Palette LUT (single source of truth, shared by all three engines):**

- Size: **4096** entries (`LutSize`), packed 32-bit ARGB (alpha 0xFF),
  cached per `(palette, maxIter)` pair (one shared table; do not mutate).
- Entry `i` holds the color for `raw = (i+0.5)/4096` with `t = raw^0.35`.
  All palettes use even stop spacing, so `segment = t*4`, `seg = min(3,
  (int)segment)`, `f = segment - seg`, and entry color =
  `lerp(stop[seg], stop[seg+1], f)` per channel (truncate to int).
- **Lookup** (CPU `ColorFromLut`, CUDA `LutColor`): given `smooth` and
  `invMaxIter = 1/maxIter`:

```text
pos = smooth * invMaxIter * 4096 - 0.5
if pos <= 0 → lut[0];  if pos >= 4095 → lut[4095]
i0 = (int)pos;  f = pos - i0
r = lutR(i0) + f * (lutR(i0+1) - lutR(i0))   (same for g, b; truncate)
```

  CPU/CUDA run the lerp in double; DirectX samples the 4096-entry 1D texture
  with hardware linear filtering (equivalent within 1 LSB).

### 2.5 Interior early-out (cardioid + period-2 bulb)

Mandelbrot mode only (applied to render paths and the CPU benchmark; **not**
to GPU benchmark kernels or Julia mode). A point known interior skips the
escape loop and renders black:

```text
q  = (px - 0.25)^2 + py^2
if (q * (q + (px - 0.25)) <= 0.25 * py^2)  → interior
else if ((px + 1)^2 + py^2 <= 0.0625)      → interior
```

The test is conservative (exact math: only true interior). In the CPU float
render path and the CUDA float kernel it runs in **double** on the
double-precision coordinates (host-order), only the escape loop is float.
The DirectX shader runs the same test in float (`InteriorBulb`).

### 2.6 Precision model

- **CPU and CUDA**: user chooses 64-bit (double, default) or 32-bit (float).
  No automatic fallback: 32-bit renders everything in float (any zoom,
  Mandelbrot and Julia) and is less precise at deep zoom by user choice.
- **DirectX**: float only, always.
- `WantsDouble(scale) = scale < 1e-3`: when true, the main-window status bar
  appends ` [beyond float!]` with the DirectX engine — float precision is
  insufficient and CUDA/CPU double is recommended.

### 2.7 Antialiasing (SSAA)

- Factors: `1x` (off), `2x`, `4x`, `8x` (dropdown; factor = `1 << index`).
- Each output pixel computes a `k x k` grid of subsamples and averages the
  ARGB channels with integer accumulation and truncating division by `k*k`.
- **On-chip SSAA**: threads/lanes loop over their own `k x k` subsamples;
  memory stays O(W x H), the `W*k x H*k` buffer is never allocated.
- Work ≈ `k²`. Subsample coordinates use the global k-times grid, so tiled
  output is pixel-identical to a whole-image render.
- Preview renders (drag/resize) use **half size per side** (quarter pixels),
  AA disabled, then bilinear upscale to the panel size.

### 2.8 Render entry-point contract (CPU)

```text
Render(bmp, cx, cy, scale, maxIter, palette, k, ct, jcx=0, jcy=0,
       julia=false, useDouble=true)
  → whole-frame render, zero offsets, size = bitmap size; runs on worker
    thread; on cancellation the bitmap is left untouched and an
    OperationCanceledException propagates.

RenderTile(bmp, cx, cy, scale, maxIter, palette, k, ct,
           offsetX, offsetY, fullWidth, fullHeight, jcx, jcy, julia, useDouble)
  → renders the tile region using the whole-image coordinate system;
    pixel-identical to the whole-frame render.
```

CUDA/DirectX expose the same `Render`/`RenderTile` shape (DirectX via
`RenderPreviewToBitmap` returning a `Bitmap?`, `null` on failure).

---

## 3. CPU Engine (`Mandelbrot.cs`)

### 3.1 Rendering pipeline

1. Compute the shared view math (§2.2) and fetch the cached 4096-entry LUT
   (§2.4) for `(palette, maxIter)`; precompute `invMaxIter`.
2. Lock the bitmap once (`Format32bppArgb`, WriteOnly) and write through a raw
   `int*` pointer (`AllowUnsafeBlocks`). Never use an intermediate color buffer.
3. Parallelize **rows** with `Parallel.ForEach` over a custom partitioner:
   `Partitioner.Create(0, h, chunk)` where
   `chunk = max(8, h / (max(1, ProcessorCount) * 8))` (row chunk per task:
   small enough to balance uneven interior rows, large enough to amortize
   scheduling).
4. Observe the `CancellationToken` (checked per row range by the
   parallelism infrastructure); on cancellation the bitmap is left untouched
   past `UnlockBits`.
5. Unlock in `finally`.

### 3.2 Code paths (replicate all four)

The row renderer is selected by (SIMD available?) × (double|float):

| Path | Condition | Core |
|---|---|---|
| `RenderRowScalar` | no SIMD | scalar double pixel loop |
| `RenderRowScalarFloat` | no SIMD | scalar float pixel loop |
| `RenderRowVectorDouble` | SIMD without AVX512 | blocks of `Vector<double>.Count` lanes + scalar tail |
| `RenderRowVectorFloat` | SIMD without AVX512 | blocks of `Vector<float>.Count` lanes + scalar tail |
| `RenderRowVectorDouble512` | AVX512 (`Avx512F`) | blocks of `Vector512<double>.Count` (8) lanes + scalar tail, same op order (bit-identical to scalar) |
| `RenderRowVectorFloat512` | AVX512 (`Avx512F`) | blocks of `Vector512<float>.Count` (16) lanes + scalar tail, same float formulas |

- The vector core processes one subsample row for a full lane block:
  lane `i` sits at global grid `gridBase + i*k`. Per-lane coordinates are
  `originX + gb*pixelSize` (exact integer grid, one multiply + one add), so
  **the double vector path is bit-identical to the scalar path** regardless
  of block/tile decomposition. The float path is approximate vs double
  (like CUDA 32-bit) and must agree lane-by-lane with its scalar tail.
- The vector escape core runs a lane-wide loop with alive masks:
  `escaped = zx2+zy2 > 4`; `alive = select(escaped, 0, alive)`; break when no
  lane alive; `iters += alive`; then the same op order as the scalar loop
  (`nzy = 2*zx*zy + ccy; nzx = zx2 - zy2 + ccx;` squares), freezing escaped
  lanes' state via `ConditionalSelect` so the smooth term is correct.
- Interior (cardioid/bulb) lanes are seeded with `alive=0, iter=maxIter`
  (forced black, no work) — Mandelbrot mode only, test in double.
- Julia seeding: `z(0) = (px,py)`, `c = (jcx,jcy)`; Mandelbrot:
  `z(0) = (0,0)`, `c = (px,py)`.

### 3.3 CPU benchmark (`BenchmarkCpu` / `BenchmarkCpuFloat`)

- Counts **total escape iterations** over repeated frames until the time
  budget elapses (standard: 8 s, §7.1). No coloring, no smoothing, no SSAA
  averaging; each grid cell is one elementary sample.
- `BenchmarkCpuFloat`: identical workload through the float vector core
  (double bulb classification + float escape loop), a separate workload with
  its own history (like CUDA 32-bit vs 64-bit).
- Same row partitioning as rendering (`chunk = max(8, H/(procs*8))`).
- **Applies the cardioid/bulb early-out** (known interior counts `maxIter`
  without looping) — intentionally different from the GPU benchmark kernels.
- Progress reported to the UI at most every 1 s (`BenchmarkProgress.
  ReportInterval`) plus a final report, to avoid stealing time from the test.
- Returns `(TotalIters, Seconds, Frames)`; the metric is
  `frames * (W*H*AA²) / seconds` elementary samples/s (§7.1).
- Cancellation checked per frame and per row range.

---

## 4. CUDA Engine (`GpuMandelbrot.cs`, `CudaNative.cs`, `Cuda/mandelbrot.cu`)

### 4.1 Architecture

- Kernels written in C (`Cuda/mandelbrot.cu`), compiled to **PTX at build
  time** with `nvcc --ptx -arch=compute_75`; the PTX is embedded as assembly
  resource `MandelbrotViewer.Cuda.mandelbrot.ptx` (size guard: >0 and <=
  10 MB; read fully; trailing zero byte appended for
  `cuModuleLoadDataEx`).
- The C# side (`CudaNative.cs`) is a minimal **driver API** binding to
  `nvcuda.dll`: initialization, device enumeration (count, name, total
  memory, attributes: SM count, compute capability, active blocks/SM),
  context create/destroy/set-current, module load/unload, function lookup,
  device alloc/free, HtoD/DtoH copies, kernel launch (render + bench
  variants), and synchronize. End users need only the NVIDIA driver — never
  the CUDA toolkit.
- Fallback: if initialization or rendering fails, the app falls back to CPU
  and reports the reason in the status bar (`LastError`, human-readable,
  e.g. "NVIDIA driver (nvcuda.dll) not available", "No CUDA device
  enumerated.", "CUDA device not found: <name>").
- All rendering and benchmark operations are serialized through **one gate**
  (a single lock), so they never interleave.

### 4.2 View parameters struct (bit-exact contract)

`GpuViewParams` is a blittable struct shared with the C side: **same field
order, 8-byte packing, exactly 88 bytes** (4 doubles = 32, 5 ints = 20, pad
4, 2 doubles = 16, 4 ints = 16). A `static_assert(sizeof == 88)` in the .cu
and a `Marshal.SizeOf` check in C# enforce it at init time:

```text
CenterX   double
CenterY   double
PixelSize double      // scale / (fullW*k)
TopY      double
W         int         // tile width (output pixels)
H         int         // tile height
MaxIter   int
Supersample int       // k
JuliaOn   int         // 0/1
Jcx       double
Jcy       double
FullW     int         // whole-image width (0 → W)
FullH     int         // whole-image height (0 → H)
OffsetX   int         // tile origin X
OffsetY   int         // tile origin Y
```

### 4.3 Kernels (`Cuda/mandelbrot.cu`, 4 extern "C" __global__)

All kernels use a **2D launch grid**: thread `(blockIdx.x*blockDim.x+
threadIdx.x, blockIdx.y*blockDim.y+threadIdx.y)` maps directly to pixel/
sample `(x, y)` — no index division/modulo; 32-wide blocks keep warps on a
single row (coalesced writes).

- `FloatKernel(int* pixels, GpuViewParams p, const int* lut)` and
  `DoubleKernel(...)`: one thread per **output pixel**. Computes `originX =
  CenterX - FullW*k*0.5*PixelSize` (host op order), then loops its `k x k`
  subsamples on the global k-times grid
  (`globalBx = (OffsetX+x)*k+sx`), per subsample: double cardioid/bulb test
  (Mandelbrot only) → escape loop (§2.1, float/double respectively) →
  `smooth = iter + 1 - log2(0.5*log(max(zx2+zy2, 4)))` → `LutColor(lut,
  smooth, MaxIter)` (§2.4 lookup, double arithmetic). Accumulates channels in
  64-bit integers and writes the averaged ARGB to `pixels[y*W+x]`.
  Interior → `0xFF000000`. Julia branch: `z(0) = (px,py)`, `c = (Jcx,Jcy)`.
- `FloatBenchKernel(int* iters, GpuViewParams p)` / `DoubleBenchKernel(...)`:
  one thread per **sample** (`W x H` = the elementary grid), `cx = CenterX +
  (x - W*0.5)*PixelSize`, `cy = TopY + y*PixelSize`, runs the §2.1 loop, and
  stores the iteration count. **No coloring, no smoothing, no early-out** —
  so GPU benchmark kernels measure the identical workload across engines.
- `LutColor`: mirrors `PaletteColors.ColorFromLut` (double lerp from the
  precomputed `1/maxIter`; FMA may fuse the final adds — documented
  difference from CPU).
- Compiled with **FMA contraction enabled** (nvcc default): speed over CPU
  bit-identity; rare single-pixel flips on chaotic boundaries (few per mille;
  amplified to sparse speckle only in 32-bit deep zoom).

### 4.4 Initialization and device selection (`TryInitialize(deviceName?)`)

1. Verify `Marshal.SizeOf<GpuViewParams>() == 88`; load the PTX resource.
2. Enumerate CUDA devices (name + total memory). No name → candidates sorted
   by **memory descending** (auto = most capable); with a name → exact match.
3. For each candidate: create the primary context on that device, load the
   module, look up the four functions (`FloatKernel`, `DoubleKernel`,
   `FloatBenchKernel`, `DoubleBenchKernel`), then autotune (§4.5). On failure
   record `LastError` and try the next candidate.
4. `DeviceDetails` (shown in diagnostics): `"SM x{sm}, CC {major}.{minor},
   {mem} MB, block {fx}x{fy}/{dx}x{dy} (32/64-bit), occ {occF}/{occD}
   blocks/SM"`.

### 4.5 Launch-layout autotune (`TuneBlocks`)

- Candidates: `(32,4), (32,8), (32,16), (32,32)` threads/block per
  precision (width 32 keeps warps on single rows; heights sweep 128/256/512/
  1024 threads).
- Probe grid: the **real benchmark grid 960x540** at reduced iterations
  (2000), center `(-0.75, 0)`, pixel `0.01/960` — same occupancy regime as
  the measured workload (a small grid would starve the SMs and rank
  differently).
- After a warmup (8 launches of the float bench kernel at 32x8 + sync, to
  reach steady clocks and compiled kernels), time each candidate with rounds
  **interleaved** (candidate order inside each round), **median of 5 rounds**.
- Keep the default `32x8` unless the winner's median is clearly faster:
  only replace it when `(defaultMedian - bestMedian)/defaultMedian >= 0.015`
  (1.5% margin), so noise cannot flip the layout between runs.
- Failure of any timing → `+inf`; default 32x8 is the safe outcome.

### 4.6 Rendering (`Render` / `RenderTile`)

Under the single gate:
1. Set the context current; ensure the device LUT matches `(palette,
   maxIter)` — the host 4096-entry table (§2.4) is uploaded once per
   combination and shared by all frames (`EnsureLut`).
2. Reuse device + host pixel buffers between frames (reallocation only when
   the pixel count changes).
3. Launch the float or double render kernel on the tuned 2D grid
   (`gridX = ceil(W/bx)`, `gridY = ceil(H/by)`), synchronize, check the
   cancellation token, copy back to the host `int[]`, and `Marshal.Copy` into
   the locked bitmap (`Format32bppArgb`).
4. Returns `useDouble` (the form uses this for the status label
   `CUDA-{double|float} {DeviceShortName}`).
- `DeviceShortName` strips the `"NVIDIA GeForce "` prefix from the device
  name.
- `WantsDouble(scale)` = `scale < 1e-3` (status hint, §2.6).
- `Dispose()`/`ResetCore()`: free device buffers, unload the module, destroy
  the context, reset tuned block sizes to 32x8 — best-effort, never throws.

### 4.7 CUDA benchmark (`BenchmarkGpu`)

Under the same gate:
1. Allocate the iterations buffer once (`W*H*4` bytes) and reuse it.
2. **Adaptive batching**: estimate per-frame time from `batchMin = 4`
   launches, then queue `batch = clamp(round(0.8 s / frameSeconds), 4, 256)`
   kernels per synchronization (≤ ~0.8 s of queued work, keeping slow GPUs
   clear of TDR while fast GPUs run deep). This measures compute throughput,
   not submit latency.
3. Loop `launch batch → synchronize` until the budget (8 s) elapses; count
   frames; report progress at most every 1 s (`TotalIters` = 0 — CUDA/DX
   measure in frames).
4. Cancellation checked around each batch; returns `(0, seconds, frames)`.

---

## 5. DirectX 11 Engine (`DxMandelbrot.cs`)

Realtime float-precision backend: a fullscreen triangle drives an HLSL pixel
shader that computes the fractal every frame; a flip-sequential swapchain
presents to the panel's window handle. The same shaders/pipeline also serve
offscreen colored previews, PNG capture, tiled export, and the benchmark.

### 5.1 Shaders (HLSL, compiled at runtime with D3DCompiler to vs_5_0/ps_5_0)

Replicate the following sources (they encode the shared math contract):

**Vertex shader** — fullscreen triangle from `SV_VertexID` (no vertex buffer):

```hlsl
float4 VS(uint id : SV_VertexID) : SV_Position
{
    float2 p = float2((id << 1) & 2, id & 2);
    return float4(p * float2(2.0, -2.0) + float2(-1.0, 1.0), 0.0, 1.0);
}
```

**Render pixel shader** — on-chip SSAA, LUT texture palette, cardioid/bulb
early-out, Julia branch, tile mapping (identical to §2):

```hlsl
cbuffer Params : register(b0)
{
    float cx; float cy; float scale; float aspect;
    float invW; float invH; int maxIter; int aa;
    int fullW; int fullH; int offsetX; int offsetY;
    float jcx; float jcy; int julia; int jpad;
};
Texture1D<float4> Lut : register(t0);
SamplerState LutSampler : register(s0);

float3 Graded(float raw)
{
    return Lut.SampleLevel(LutSampler, saturate(raw), 0).rgb;
}

bool InteriorBulb(float2 c)
{
    float2 q = c - float2(0.25, 0.0);
    float q2 = dot(q, q);
    if (q2 * (q2 + q.x) <= 0.25 * c.y * c.y) return true;
    float2 d = c + float2(1.0, 0.0);
    return dot(d, d) <= 0.0625;
}

float4 PS(float4 pos : SV_Position) : SV_Target
{
    int n = max(aa, 1);
    float3 acc = 0.0;
    for (int jy = 0; jy < n; jy++)
        for (int jx = 0; jx < n; jx++)
        {
            float2 sub = (float2((float)jx, (float)jy) + 0.5) / (float)n - 0.5;
            float2 p = float2((float)offsetX, (float)offsetY) + pos.xy + sub;
            float2 px = float2(cx + (p.x * invW - 0.5) * scale,
                               cy + (p.y * invH - 0.5) * scale * aspect);
            float3 col = 0.0;
            if (julia == 0 && InteriorBulb(px)) { /* col stays black */ }
            else
            {
                float2 z = julia != 0 ? px : 0.0;
                float2 cc = julia != 0 ? float2(jcx, jcy) : px;
                int iter = 0;
                while (iter < maxIter && dot(z, z) <= 4.0)
                {
                    z = float2(z.x * z.x - z.y * z.y, 2.0 * z.x * z.y) + cc;
                    iter++;
                }
                if (iter < maxIter)
                {
                    float mod2 = max(dot(z, z), 4.0);
                    float smooth = (float)iter + 1.0 - log(log(sqrt(mod2))) / log(2.0);
                    col = Graded(saturate(smooth / (float)maxIter));
                }
            }
            acc += col;
        }
    return float4(acc / (float)(n * n), 1.0);
}
```

**Benchmark pixel shader** — iterations-only (identical loop to the CUDA
bench kernels; output depends on `iter` to defeat dead-code elimination):

```hlsl
cbuffer Params : register(b0)
{
    float cx; float cy; float scale; float aspect;
    float invW; float invH; int maxIter; int aa;
};

float4 BenchPS(float4 pos : SV_Position) : SV_Target
{
    float2 c = float2(cx + (pos.x * invW - 0.5) * scale,
                     cy + (pos.y * invH - 0.5) * scale * aspect);
    float zx = 0.0, zy = 0.0, zx2 = 0.0, zy2 = 0.0;
    int iter = 0;
    while (iter < maxIter && zx2 + zy2 <= 4.0)
    {
        zy = 2.0 * zx * zy + c.y;
        zx = zx2 - zy2 + c.x;
        zx2 = zx * zx; zy2 = zy * zy;
        iter++;
    }
    float v = iter >= maxIter ? 0.0 : frac(float(iter) * 0.125);
    return float4(v, v, v, 1.0);
}
```

### 5.2 Device and adapter selection

- `AdapterNames()`: enumerate via `IDXGIFactory1`, up to 32 adapters,
  **exclude** names starting with `"Microsoft Basic"` (WARP software
  renderer). Do **not** read `DedicatedVideoMemory` through the wrapper's
  implicit 32-bit conversion (throws `OverflowException` on GPUs with > 4 GB;
  read it as MB explicitly — `DedicatedVideoMemory / (1024*1024) * 1024 * 1024`;
  on failure keep `ulong.MaxValue` = unknown, which skips VRAM checks).
- `TryInitialize(hwnd, w, h, adapterName?)` → `EnsureDevice` + swapchain:
  - `EnsureDevice`: if ready and same adapter, reuse; else dispose and
    recreate. With a named card, find the matching DXGI adapter and create
    the D3D11 device with `DriverType.Unknown` (required when passing an
    adapter); otherwise default hardware adapter.
    `D3D11CreateDevice(adapter, type, BgraSupport, [Level_11_0, Level_10_0])`.
    Then compile the three shaders (D3DCompiler, entries `VS`/`PS`/
    `BenchPS`, `mandelbrot-*` names), create the dynamic constant buffer
    (`DxParams`, see below), and the LUT sampler (`MinMagMipLinear`, clamp).
  - Swapchain: `R8G8B8A8_UNorm`, `BufferCount = 2`, `Scaling.Stretch`,
    `SwapEffect.FlipSequential`, `AlphaMode.Ignore`, windowed, via
    `CreateSwapChainForHwnd`.
  - Every failing step records `LastError = "[{step}] {Type}: {message}"`
    and disposes.
- `AdapterName` is `adapterName ?? "Auto (default hardware adapter)"`.
- `ShortAdapterName(full)`: strips `"NVIDIA GeForce "` and `"(TM)"`, trims
  (used in the benchmark history/chart).

### 5.3 Constant buffer layout

`DxParams` (sequential, packed into the dynamic buffer; one `WriteDiscard`
map/unmap per frame batch):

```text
float cx, cy, scale, aspect;   // aspect = fullH/fullW
float invW, invH;             // 1/fullW, 1/fullH
int   maxIter, aa;
int   fullW, fullH, offsetX, offsetY;
float jcx, jcy;  int julia(0/1), jpad;
```

### 5.4 Palette LUT texture

- 1D texture, width **4096**, `R8G8B8A8_UNorm`, 1 mip, dynamic, write-only.
- Entries are the same `PaletteColors.GetLut(palette, maxIter)` ARGB ints,
  unpacked to bytes (R, G, B, 0xFF); refilled only when palette or maxIter
  changes.
- Shader samples with `SampleLevel(..., saturate(raw), 0)` — hardware linear
  filtering interpolates between entries (no banding), matching CPU lerp.

### 5.5 Frame pipeline

`DrawFrame(Params, rtv, ps, w, h)` (shared by render/preview/benchmark):
map+write the constant buffer → `OMSetRenderTargets` → set VS/PS →
`PSSetConstantBuffer(0)` → bind LUT SRV + sampler (skip while the LUT is
missing, e.g. headless benchmark-only sessions) → `TriangleList` → viewport
(w, h) → `Draw(3, 0)`.

- **Realtime render** (`Render`): build params (aspect from view size),
  `DrawFrame` into the backbuffer, then `Present(0, None)`.
- The main window drives it with a **16 ms timer** (~60 fps) and a `_dxDirty`
  flag: each tick resizes as needed and renders one frame only when dirty;
  pan/zoom set dirty and are therefore immediate.
- **Resize**: `ResizeBuffers(0, w, h, Unknown)` then recreate the render
  target view (stretch during the drag, full quality on release).

### 5.6 Capture and preview

- `Capture()` → `ReadTextureToBitmap` of the swapchain backbuffer:
  create a **staging** texture (same size, `CPUAccess.Read`), `CopyResource`,
  map read, copy row-by-row into a `Format32bppArgb` bitmap (handles row
  pitch), unmap.
- `RenderPreviewToBitmap(...)`: render the colored shader into an offscreen
  `B8G8R8A8_UNorm` target (byte layout identical to `Format32bppArgb`, so
  channels do not come out inverted), read back as above, then restore the
  swapchain target + viewport. Returns `null` on failure. Supports full
  tile parameters (fullW/fullH/offset) for pixel-identical tiled export.

### 5.7 Offscreen benchmark

- `BeginBenchmarkOffscreen(gridW, gridH)`: create an in-GPU-memory
  `R8G8B8A8_UNorm` render target (no swapchain, **no Present** — excludes DWM
  composition and cross-GPU copies) plus a ring of **256 event queries**
  (`QueryType.Event`). VRAM pre-check: required = `w*h*4*2` bytes vs
  dedicated+shared (when known); fail fast with a clear error instead of a
  TDR hang.
- `RunBenchmarkFramesOffscreen(...)` (shared by the GUI and `--bench-dx`):
  - Pipeline state (constants, shaders, render target, viewport) is set
    **once** before the loop; per frame only `Draw(3,0)` + `End(query)`.
  - **In-flight depth** sized from a measured frame time: run a small batch
    (`BenchFlightMin = 4`) to estimate, then
    `inflight = clamp(round(BenchTdrSafeSeconds / frameTime), 4, 256)` with
    `BenchTdrSafeSeconds = 0.8` (never queue more than ~0.8 s of work —
    avoids TDR/device-removed on slow cards; deep queues saturate fast cards).
  - Main loop: submit draws with their event queries; drain completed ones
    (`GetData` status check with `DoNotFlush`); tick callback every ≥ 0.5 s.
  - **DrainOne** (blocking wait for the oldest frame): tight spin-poll
    (`GetData` with `None`), with cancel check, device-removed check, and a
    **60 s per-frame timeout** every 1024 polls — a hung/removed device
    (`DeviceRemovedReason.Failure`) becomes a readable
    `InvalidOperationException("GPU hung during the benchmark...")`, and an
    unresponsive GPU a `TimeoutException`. Never wait forever.
  - On exit: `Flush()` and drain all pending; the time stops at the
    completion of the last frame; returns `(completedSeconds, completed)`.
- `EndBenchmarkOffscreen()` releases target, views, queries, and restores
  the swapchain target/viewport.

### 5.8 Failure and fallback

- Init failure → the DirectX radio stays disabled; `LastError` shown in the
  status bar / diagnostics.
- Runtime render failure (e.g. in the 16 ms tick) → `FallbackToCpu(reason)`:
  dispose the engine, disable the radio, switch to CPU, status shows
  `"{reason} — falling back to CPU"`.

---

## 6. Main Window (`MandelbrotForm`)

### 6.1 Window frame

- Title: `"Mandelbrot Set Viewer v{AppVersion.Display}"`; app icon applied
  from the embedded resource (never blocking).
- Client size **1152x720**; `AutoScaleMode.Font`; `KeyPreview = true` (form
  handles shortcuts); position/size/maximized state restored from settings
  when the saved rectangle intersects a screen.
- Closes via `Alt+F4`; on close: save settings, stop timers, cancel pending
  renders/animation, dispose both GPU engines.

### 6.2 Layout

Top-to-bottom: menu strip → toolbar (top panel, dock top, height 72, padding
8/4, two FlowLayout rows, no wrap, controls left-clustered, margins 3/1) →
view area (Fill) → status strip.

**Row 0** (left to right): `↩ Home` button (70x23, Segoe UI Symbol font, →
back-to-set animation) → `Reset` (60x23) → `Benchmark` (100x23) →
"Iterations:" label + NumericUpDown (width 70, Min 50, Max 50000, Increment
50, value 256) + `Auto` checkbox → "Palette:" + ComboBox (width 110,
drop-down list, items `Fire, Ice, Thermal, Ocean, Violet, Desert, Forest`,
index 0 default) → "AA:" + ComboBox (width 60, items `1x, 2x, 4x, 8x`,
index 0 default).

**Row 1**: "Engine:" + radio group `CPU` (checked by default) / `CUDA`
(disabled until initialized) / `DirectX` (disabled until initialized) →
"Precision:" + radio `32` / `64` (both disabled with CPU/DX until engine
gives them meaning; **64 checked by default**; hidden-effectively disabled
with DirectX — precision applies to CPU and CUDA only) → "GPU:" label +
ComboBox (width 200; initially hidden with CPU engine, visible with CUDA/
DirectX; item 0 = `Auto` plus the sorted union of DirectX adapter names and
CUDA device names, filled on first show).

**View area**: two overlapping Fill-docked surfaces — a `PictureBox`
(black background, crosshair cursor, `SizeMode.Zoom`) for bitmap engines
and a `Panel` (`dxPanel`) hosting the DirectX swapchain; the active one is
shown per engine. Both share the same mouse handlers.

**Status strip**: a spring label (`lblStatus`, left, showing the live
status text) + a static hint label (gray, Segoe UI 8pt):
`"Left/Right click: zoom | Wheel: zoom | Drag/arrows: pan | R: reset | S: save"`.

**Tooltips** (required, on every control/menu item):
- Home: "Back to set with RealTime animation (Ctrl+Shift+R)"
- Reset: "Restore the initial set view (R key)"
- Benchmark: "Open the standard benchmark (Ctrl+B)"
- numIter: "Maximum iterations per pixel (+/- keys)"
- Auto: "Automatic iterations: grow with zoom level"
- palette: "Fractal color palette (Fire, Ice, Thermal, Ocean, Violet, Desert, Forest)"
- AA: "Antialiasing: 1x disabled, 2x/4x/8x average of nearby pixels"
- GPU: "Video card to use (Auto = the most powerful)"
- 32/64: "CPU/CUDA precision 32-bit (float): faster, less precise. Ignored with DirectX." / "...64-bit (double): more precise, slower. ..."
- CPU: "CPU multicore engine (always available)"
- CUDA: "CUDA engine: NVIDIA GPU via native kernels (float/double)"
- DirectX: "DirectX engine: real-time GPU (float)"
- File items: load zone "Reload the view saved in a JSON file (Ctrl+O)"; save
  zone "Save the current view (center, scale, iterations) to a JSON file (Ctrl+S)";
  save image "Save the current image as PNG (Ctrl+Shift+S)"; benchmark
  "Standard benchmark: 8s at high iterations (Ctrl+B)"
- Generate: "Generate output from the view: PNG screenshot or MP4 zoom video";
  screenshot "Screenshot at view resolution with optional AA (Ctrl+Shift+E)";
  video "MP4 zoom video with optional AA (Ctrl+Shift+V, requires ffmpeg)";
  back to set "Back to set with RealTime animation (Esc to stop, Ctrl+Shift+R)"
- About: "About MandelC (F1)"

### 6.3 Menus

**&File**: Load &zone... (Ctrl+O) | Save &zone... (Ctrl+S) | — | Save &image
as... (Ctrl+Shift+S) | — | Bench&mark... (Ctrl+B) | — | E&xit (Alt+F4).

**View** (inserted at position 1, built in code — dynamic favorites):
Back (Alt+Left) | Forward (Alt+Right) | — | &Julia mode (Ctrl+J, CheckOnClick,
checked state persisted) | — | Save favorite zone… (Ctrl+D) | Favorite zones
(submenu, rebuilt on open: `*.json` files in the zones dir, ordered
insensitively, `(none)` disabled placeholder) | Delete favorite (submenu,
same list).

**&Generate**: &Screenshot... (Ctrl+Shift+E) | &Zoom video... (Ctrl+Shift+V) |
Back to set (&RealTime) (Ctrl+Shift+R; while running the text switches to
"Stop (Esc)").

**&Help**: &About... (F1) | — | Show &log / diagnostics....

### 6.4 Status text formats

- While computing: `"Computing [preview ]{W}x{H}, iter={maxIter}..."`.
- After a CPU/CUDA render:
  `"Center {cx:+0.000000;-0.000000} {cy:+0.000000;-0.000000}i | width {scale:E2} | iter {maxIter}[ (auto)] | {Palette}[ AA{k}x] | engine CUDA-{double|float} {DeviceShortName} | CPU-{double|float}[ | Julia c={jcx} {jcy}i][ (preview)]"`
- After a DirectX render: same but `engine DirectX (float)` plus ` [beyond
  float!]` when `scale < 1e-3`.
- Engine problems, GPU switches, and errors are reported verbatim in the
  status bar (e.g. `"CUDA on '{name}': {error} — switching to CPU"`).

### 6.5 Mouse interaction (shared by pictureBox and dxPanel)

- **MouseEnter**: the control takes focus (so the wheel arrives at it).
- **MouseDown** (Left/Right): begin a potential drag; record position; push
  the current zone as `dragStartZone`.
- **Drag vs click threshold: 5 px** (Euclidean distance from the down
  position; below = click, above = drag).
- **MouseMove while down**: once moved, pan so the point under the cursor
  stays under the cursor (`center += (from - to) * (scale / viewWidth)`),
  and re-render as **preview** (bitmap: half size, AA off, bilinear upscale;
  DirectX: full realtime frame) **throttled to 80 ms** via
  `Environment.TickCount`.
- **MouseUp**: if it was a click — Julia mode + Left click sets
  `c = pixel point` (push history); else Left = **zoom in 2x** at the point
  (factor 0.5), Right = **zoom out 2x** (factor 2.0), both centered on the
  point, both pushed to history. If it was a drag — commit the pan
  (`dragStartZone` goes to back-history, forward cleared) and render the
  final position.
- **MouseWheel**: zoom at the cursor, factor **0.7** (wheel up) or **1.43**
  (≈ 1/0.7, wheel down); pushed to history.
- Zoom math (around a complex point `p`): `center = p + (center - p) * factor`,
  `scale *= factor`.

### 6.6 Keyboard (form-level, `KeyPreview`)

- `Esc` cancels the running back-to-set animation (only).
- Ctrl/Alt combinations are ignored (they belong to menus).
- `R` reset | `S` save PNG | `+`/`-` iterations ±50 (clamped to 50...50000).
- Arrow keys pan by **1/10 of the view** (Shift: **1/100**), pushed to
  history; **ignored while the iterations control has focus** (arrows adjust
  the numeric there).

### 6.7 Reset, Save PNG

- **Reset**: push history; restore `(-0.5, 0, 9.36)`; in Julia mode also
  reset `c` to `(-0.7, 0.27015)` (the mode stays on).
- **Save PNG** (S / File menu): capture the current image (DirectX: backbuffer
  capture; bitmap: the last rendered bitmap) via a save dialog (filter PNG,
  default name `mandelbrot.png`); failures show a warning dialog.

### 6.8 History

- Two stacks of view zones; **back history capped at 200 entries** (overflow
  drops the oldest, preserving order); pushing to back always clears forward.
- Events pushed: zoom (click/wheel), committed pan (at drag start zone),
  reset, zone load, favorite load, Julia toggle, Julia c-set, arrow pan.
- Back/Forward enable/disable the menu items; `Alt+Left`/`Alt+Right`.
- A zone record is `(CenterX, CenterY, Scale, MaxIter, Julia, Jcx, Jcy)`
  (JSON-serializable; Julia fields have defaults so old files load as
  Mandelbrot).

### 6.9 Zones and favorites

- **Save zone** (Ctrl+S): serialize the current zone to JSON (indented),
  default name `zone.json`, filter "Mandelbrot Zone (*.json)".
- **Load zone** (Ctrl+O): parse; validate finite coordinates and
  `Scale > 0`; on failure show a warning with the reason; on success push
  history and apply (explicit iterations, auto checkbox unchecked, value
  clamped to 50...50000; Julia state restored).
- **Favorites** (`%APPDATA%\MandelbrotViewer\zone\`): Save favorite (Ctrl+D)
  must stay inside that folder (full-path prefix check, warning otherwise);
  list = `*.json` files ordered case-insensitively; opening one loads it;
  deletion via the Delete favorite submenu. All IO errors surface as
  warning dialogs and never crash.

### 6.10 Engine and GPU switching

- Radio change: set engine; if the GPU engine is not yet initialized, try
  now (CUDA with WaitCursor — synchronous init may briefly freeze the UI;
  DirectX on the panel handle at current size). Failure → revert to CPU,
  disable the failed radio, status shows the reason.
- `ApplyEngineVisibility`: show dxPanel vs pictureBox; GPU label + combo
  visible only with a GPU engine; precision radios enabled only for CPU/CUDA;
  re-render (or dirty the DX frame).
- **GPU combo change**: with DirectX → re-init on that card (fallback to CPU
  on failure); with CUDA → re-init (switch to CPU on failure); with CPU →
  selection is remembered and applied when the engine changes.
- **Busy cursor** during async bitmap renders: recursive `AppStarting`
  (arrow + hourglass, UI stays interactive) on the form tree; restore only
  by the **latest** render (token identity check), and the image panels go
  back to `Cross`, not `Default`. Superseded renders must not touch the cursor.
- New install default: engine radio is CPU until a GPU initializes on first
  show; if the saved engine is CUDA and GPU init succeeds, CUDA is checked;
  saved DirectX + ready → DirectX checked.

### 6.11 Back to set (realtime animation)

- Blocked when `StartScale / scale < 2.0` (already at the full set): warning
  dialog "Already at the full set: frame a zone first."
- Runs **120 frames at 30 fps** (≈ 33 ms delay per frame) on the UI thread
  via async `Task.Delay`; each frame interpolates:
  - `t = i/(frames-1)`, ease-out `te = 1 - (1-t)^3`
  - `scale = startScale * (StartScale/startScale)^te` (logarithmic)
  - center follows the scale linearly toward `(-0.5, 0)`
  - AA is forced to `1x` during the animation and restored after.
- `Esc` (or clicking the item again) cancels; status shows progress
  `"Back to set {i+1}/{frames}... (Esc to stop)"` then a completion or
  interruption message. Every frame goes through the active engine
  (DirectX realtime or bitmap preview-quality render).

### 6.12 Settings wiring (see §8.1)

- On load: iterations (clamped), auto checkbox, palette index (clamped), AA
  index (clamped), precision (32 if `Single`, else 64), Julia mode + c
  (finite check, else defaults), GPU selection, window bounds/maximized.
- **The current view is NOT persisted** — every launch starts at the full set.
- On close (best effort, never blocks): write the full settings JSON.

---

## 7. Auxiliary Features (benchmark, export, video, log, CLI)

### 7.1 Standard benchmark (shared constants, `BenchmarkStandard`)

Identical workload for all engines so results are comparable:

| Parameter | Value |
|---|---|
| Area | **960 x 540** |
| Antialiasing | **1x** (one elementary sample per pixel, no averaging) |
| Samples/frame | `960*540*1*1 = 518,400` |
| Center | `(-0.7499302568795561, -0.015139113925433963)` |
| Scale | `1.0453474311811176e-04` (half = `5.226737155905588e-05`) |
| Iterations | `AutoIterForScale(scale)` = **10915** |
| Budget | **8 seconds** per run |
| Metric | elementary samples/s, displayed as MPixel/s (`frames*518400/seconds`) |

The GPU benchmark kernels are iterations-only (no coloring, no early-out);
the CPU benchmark additionally applies the cardioid/bulb early-out and runs
the SIMD vector core. CPU defaults to double; `--bench-cpu float` (or the
32-bit precision selection in the GUI) runs the float workload — a separate
workload with its own history, like CUDA 32-bit vs 64-bit.

**Benchmark window** (`BenchmarkForm`):
- Auto-starts when shown. Info line: engine (+ fallback note), zone
  `960x540 1x AA = 960x540 elementary samples without averaging, 10915 max
  iterations, scale 1.0453474311811176e-04, precision {CUDA|DirectX|CPU}
  {64-bit|32-bit|float} — minimum duration 8 seconds (DirectX offscreen, no
  Present).`
- Before measuring, renders a **colored preview** of the zone (Fire palette,
  AA 1x) — CPU/CUDA via the normal render, DirectX via offscreen colored
  render — shown in the preview box.
- Live progress: `"{pct}%  |  {rate}"` at most every 1 s; rate shown only
  after 0.2 s of elapsed time. Units: Gpixel/s ≥ 1e9, Mpixel/s ≥ 1e6,
  kpixel/s ≥ 1e3, else pixel/s.
- Result: `"{value} Mpixel/s"` + detail line
  `"{frames} frame [({fps} frame/s)] 960x540 AA1x in {s} s on {device}"`.
  Device label: CUDA short name / DX short adapter name / CPU name
  (`Diagnostics.CpuName()`).
- **Bar chart** (custom GDI+ paint, left margin 132, right 56, top 22,
  bottom 24; title "Confronto prestazioni (MPixel/s)", Segoe UI 8pt bold):
  horizontal bars sorted descending by value, max = max*1.15, two gridlines,
  bar height ≤ 22 px (62% of row). Historical reference bars (MPixel/s,
  best of 3 standardized runs):
  - `CUDA 5070 Ti 32-bit 310.7`, `CUDA 4070 SUPER 32-bit 250.1`,
    `CUDA 5070 Ti 64-bit 6.7`, `CUDA 4070 S. 64-bit 5.3`
  - `DirectX 5070 Ti 338`, `DirectX 4070 SUPER 262.4`, `DirectX AMD Radeon 5.2`
  - `CPU 9900X 13.8`, `CPU 9900X float 21.3`
  plus the current `Risultato` bar. Colors: current blue (36,113,163),
  CUDA orange (226,126,34), DX green (74,143,87), CPU gray (112,128,144).
- **CSV export** (button, enabled after a run): appends one row via
  `BenchmarkCsv.AppendRow` with header on first write:
  `timestamp,engine,device,precision,run,frames,seconds,mpixel_s`
  (UTC ISO-8601 `o` timestamp; seconds `0.00`, mpixel `0.#`, invariant
  culture; fields quoted when they contain `,` `"` or newline).
- Errors: logged to `AppLog` ("Benchmark {engine}: {message}"), shown in the
  detail label, chart refreshed. Cancellation supported (Start button is the
  cancel button).
- While the benchmark runs, if DirectX is the active engine its timer stops
  and the panel hides (gray sample frame stays on the swapchain); both
  restore afterwards.

### 7.2 PNG export (`ExportForm`)

- Presets (dropdown, default index 3 = 4K): **Current view** (view size
  clamped to ≥ 320x240), Full HD 1920×1080, 2K 2560×1440, 4K 3840×2160,
  8K 7680×4320, Double 4K 7680×2160, **Custom…** (both sides integers
  **320 ... 16384**, strict validation with the message "Custom dimensions:
  integers 320…16384 px."). The screenshot entry (Generate menu) opens with
  preset = Current view.
- AA dropdown: `As view, 1x, 2x, 4x, 8x` (As view = the active view AA);
  **no automatic AA reduction** at high resolutions (on-chip SSAA).
- Precision radios (32/64) enabled for CUDA and CPU only; DirectX always float.
- Render: **tiled 512×512** working tiles, row-major, on the active engine
  (CPU `RenderTile`, CUDA `RenderTile`, DirectX `RenderPreviewToBitmap`
  with full-image tile parameters — pixel-identical to a whole render),
  copied row-by-row into the result bitmap; cancellable between tiles
  (progress label `"Rendering {w}×{h} AA{k}x…"`); then a PNG save dialog
  (default `mandelbrot.png`).
- Info line shows engine, mode (Mandelbrot / `Julia c=... i`), palette,
  iterations, and the sample budget: `"{w}×{h} ({MPixel} MPixel), AA{k}x —
  {w*k*h*k/1e6} MPixel of samples."`.

### 7.3 Zoom video (`ZoomVideoForm`)

- Renders the transition **from the current view to the full set** at view
  resolution (clamped ≥ 320x240); unavailable when `StartScale/scale < 2.0`
  (warning "You are already at the full set: frame a region first...").
- Frame count dropdown: **60, 120, 240, 480** (default 120); fps: **24, 30,
  60** (default 30); AA: As view/1x/2x/4x/8x; precision as in export.
- Frame i of N: `t = i/(N-1)`, ease-out `te = 1-(1-t)^3`,
  `scale = startScale * (9.36/startScale)^te`, center linear in scale toward
  `(-0.5, 0)`, iterations = `AutoIterForScale(scale)` per frame.
- Frames render on the active engine, saved as `f0000.png ... f{N-1:0000}.png`
  in a temp folder `MandelC-video-{guid8}`; progress bar +
  `"Frame {i+1}/{N}…"`; cancellable; the temp folder is deleted on success
  or failure, **kept** when ffmpeg is absent (it is then the result, with an
  Open button).
- **ffmpeg** lookup: first `ffmpeg.exe` on `PATH`.
  Encoding (worker thread, cancellable, kill tree on cancel):
  `-y -framerate {fps} -i f%04d.png -frames:v {N} -vf "pad=ceil(iw/2)*2:ceil(ih/2)*2" -c:v libx264 -pix_fmt yuv420p -crf 18 {out}`
  (pad to even dimensions for yuv420p). stderr drained asynchronously during
  the wait (reading after `WaitForExit` deadlocks once the pipe fills);
  non-zero exit → error with the last 10 stderr lines.

### 7.4 Log / diagnostics (`LogForm` + `BuildDiagnosticLog`)

- Read-only, Consolas 9.75, 720x480 (min 440x320), centered on parent,
  with "Copy to clipboard" and "Close" buttons.
- Content (in order): header `"Mandelbrot Set Viewer   v{Full}"` + open time
  + OS + .NET runtime; **=== Engine ===** (selected + in use); **===
  DirectX (D3D11) ===** (Ready, card in use, last error, DXGI cards, enum
  error); **=== CUDA (native) ===** (Ready, device, details incl. tuned
  block layout, last error, device list); **=== GPU selection ===**
  (selected card / Auto, precision); **=== Saved settings ===** (engine, GPU,
  palette, AA, iterations, precision); **=== Event log ===** (the AppLog
  ring: last 200 lines `HH:mm:ss  message`, or `(empty)`).

### 7.5 About dialog

Version `v{Display}`, engine description, then the full command list (left
zoom 2x, right zoom out 2x, wheel, drag, R/S/±/arrows, Alt+Left/Right,
Ctrl+D favorites, Ctrl+J Julia, palette list, auto iterations, AA, engines,
GPU Auto, File menu zones).

### 7.6 Command-line diagnostics (`Program.cs`, `Diagnostics.cs`)

- `--diag-dx [card]`: list DXGI cards (with enumeration error if any),
  detailed per-adapter enumeration (name + VRAM or failure per adapter),
  then a hidden-form initialization test (800x600) printing IsReady,
  AdapterName, LastError, and a test render (200 iterations).
- `--diag-gpu`: enumerate CUDA devices, initialize, render a 320x240 test
  frame, and write `diag_gpu.txt` next to the executable (devices, ready,
  device, details, error; or the exception).
- `--bench-dx [card] [--csv file]`: standardized benchmark, **3 runs** of the
  8 s budget per card (all cards, or the named one), offscreen with event
  queries; prints MPixel/s per card and the best; `--csv file` appends one
  row per run (same format as §7.1).
- `--bench-cuda [device] [--csv file]`: same standard test via the CUDA
  bench kernel (3 runs), float/double per the standard (64-bit).
- `--bench-cpu [float] [--csv file]`: CPU benchmark (3 runs); `float`
  switches to the float workload.
- Argument parsing: first positional = card/device (for `--bench-cpu` it is
  the precision mode); `--csv file` anywhere after the flag.

---

## 8. Persistence (settings, zones, favorites)

### 8.1 Settings (`%APPDATA%\MandelbrotViewer\settings.json`)

JSON, indented, `System.Text.Json`. Fields (defaults in parentheses):

```text
IterAuto   bool  (false)          // automatic iterations on/off
MaxIter    int   (256)            // manual iteration limit, saved clamped 50..50000
Palette    int   (0)              // palette index (0 Fire .. 6 Forest)
AaIndex    int   (0)              // AA dropdown index (0=1x,1=2x,2=4x,3=8x)
Engine     string ("Cuda")        // enum name: Cpu | Cuda | DirectX
Gpu        string ("")            // chosen card ("" = Auto)
Single     bool  (false)          // true = 32-bit float, false = 64-bit double
Julia      bool  (false)          // Julia mode
Jcx        double(-0.7)           // Julia constant real
Jcy        double(0.27015)        // Julia constant imaginary
WinX/WinY/WinW/WinH int (0)       // window bounds
Maximized  bool  (false)
```

- **Load tolerance**: missing or corrupt file → defaults, no error. Loaded
  `MaxIter` is clamped 50..50000; palette/AA indexes clamped to the dropdown
  ranges; Julia constants must be finite (else defaults). Saved engine is
  applied only if the engine is actually available (else CPU).
- **Save**: on window close, best-effort (exceptions swallowed — never block
  closing); `MaxIter` clamped 50..50000 before writing.
- The current view is never persisted (each launch starts at the full set).

### 8.2 Zones and favorites

- Zone JSON (save/load + favorites, indented): `ViewZone` record
  `{CenterX, CenterY, Scale, MaxIter, Julia, Jcx, Jcy}`; Julia fields
  optional with defaults `false, -0.7, 0.27015` (old files load as
  Mandelbrot).
- Validation on load: finite center/scale and `Scale > 0`.
- Favorites directory: `%APPDATA%\MandelbrotViewer\zone\`; favorite saves
  must stay inside it.
- App event log (`AppLog`): in-memory ring of the last **200** lines,
  thread-safe, timestamps `HH:mm:ss` + two spaces; used by the log dialog and
  benchmark error reporting. (In-memory only — not written to disk.)

---

## 9. Acceptance Checklist (verify your replica against this)

After implementing, check each item. **Math/behavior** (runnable on any
machine):

- [ ] Initial view is centered at `(-0.5, 0)` with complex width `9.36` and
      renders the full Mandelbrot set with black interior.
- [ ] `AutoIterForScale(9.36)` ≈ 1944; `AutoIterForScale(1.0453474311811176e-04)`
      = 10915; manual limit clamps to 50...50000; `+`/`-` keys step by 50.
- [ ] Smooth coloring: escaped point color = LUT at
      `(iter + 1 - log2(0.5*ln(max(|z|²,4))) / maxIter)^0.35`; interior
      points exactly `#000000` opaque.
- [ ] All 7 palettes render with the exact 5-stop tables of §2.4 (sample a
      few known stops, e.g. Fire t=0.5 → (255,120,0), Ice t=0.75 →
      (130,215,255), Forest t=0 → (55,32,12)).
- [ ] LUT has 4096 entries, entry `i` at `raw=(i+0.5)/4096`, lookup is the
      §2.4 interpolated form; switching palette or maxIter rebuilds it.
- [ ] Cardioid/bulb early-out: points in the main cardioid or the period-2
      bulb (e.g. c = -1, c = 0.3) render black in Mandelbrot mode; Julia mode
      never applies the test.
- [ ] Left click zooms 2x in at the point; right click 2x out; wheel 0.7 / ≈
      1.43 at the cursor; drag pans (5 px threshold) and commits to history;
      arrows pan 1/10 (Shift 1/100), ignored while the iterations box is
      focused.
- [ ] `R` resets to the initial view (and Julia c to -0.7+0.27015i); `S`
      saves a PNG; `Alt+Left/Right` navigate history (≤ 200 back entries,
      forward cleared on new navigation).
- [ ] AA 2x/4x/8x average a k×k grid on-chip; output pixel-identical between
      tiled (512×512) and whole-image renders of the same view/AA.
- [ ] Julia mode (Ctrl+J): left click sets c to the clicked point; render is
      `z(0)=pixel, c=fixed`; zone save/load round-trips the Julia state.
- [ ] Zone JSON: saving and reloading a zone restores center, scale,
      iterations (auto off) and Julia fields; invalid files are rejected with
      a readable error.
- [ ] Favorites save only inside `%APPDATA%\MandelbrotViewer\zone\`; menu
      lists, opens, and deletes them.
- [ ] Settings persist (engine, GPU, palette, AA, iterations + auto,
      precision, Julia, window bounds) and a corrupt settings.json falls back
      to defaults; the view itself is not persisted.
- [ ] Benchmark window: 960×540, AA 1x, 10915 iterations, 8 s budget,
      colored preview first, live progress ~1/s, MPixel/s result, CSV row
      format of §7.1; cancel stops the run.
- [ ] Export dialog: presets produce the exact resolutions of §7.2; custom
      values outside 320...16384 are rejected; result PNG matches the view.
- [ ] Zoom video: blocked at the full set; frames ease-out logarithmically to
      scale 9.36 with auto iterations per frame; ffmpeg MP4 when available,
      otherwise the PNG sequence is kept.
- [ ] Back to set: 120 frames @ 30 fps, AA forced 1x during, Esc cancels,
      blocked when already at the full set (`StartScale/scale < 2.0`).
- [ ] Status bar shows center (6 decimals), width (E2), iterations (with
      "(auto)"), palette, AA, engine label; `[beyond float!]` appears with
      DirectX when scale < 1e-3.
- [ ] Busy cursor is AppStarting during async renders and is restored only by
      the latest render; panels return to the crosshair.
- [ ] Log/diagnostics window shows engine, DirectX (cards, last error), CUDA
      (device, details), GPU selection, saved settings, and the 200-line
      event log; copy button works.
- [ ] CLI: `--diag-dx`, `--diag-gpu`, `--bench-dx [card] [--csv f]`,
      `--bench-cuda [device] [--csv f]`, `--bench-cpu [float] [--csv f]`
      behave as in §7.6 without opening the UI.

**Engines** (where hardware allows):

- [ ] CPU: 4 code paths exist (scalar/vector × float/double); double vector
      output is bit-identical to scalar double; 32-bit setting renders in
      float with no automatic fallback; rows are parallelized with the
      §3.1 chunking.
- [ ] CPU and CUDA renders of the same zone are near-identical (differences
      limited to rare chaotic-boundary pixels; 32-bit may differ more at deep
      zoom — both expected).
- [ ] CUDA: `GpuViewParams` is exactly 88 bytes on both sides; the 4 kernels
      exist; init selects by device memory (Auto) or exact name; tuned block
      is one of 32x4/32x8/32x16/32x32 with the 1.5% margin rule; device LUT
      uploaded once per (palette, maxIter); render and benchmark share one
      gate; unavailability falls back to CPU with a readable status reason.
- [ ] DirectX: fullscreen-triangle VS, the §5.1 render PS (with LUT texture,
      cardioid/bulb, Julia, tile mapping) and bench PS; swapchain 2 buffers
      flip-sequential; 16 ms timer renders only when dirty; WARP adapters
      excluded from the GPU list; adapter memory read without 32-bit overflow;
      capture and preview go through staging copies; offscreen benchmark uses
      event queries with in-flight 4..256, ≤ 0.8 s queued, 60 s frame timeout,
      and readable TDR/device-removed errors.
- [ ] GPU combo lists the union of DirectX adapters and CUDA devices (sorted,
      distinct), remembers the choice, and re-initializes on change with the
      §6.10 fallbacks.

**Fidelity spot values** (grep your implementation for these exact
constants): `9.36`, `-0.5`, `-0.7`, `0.27015`, `0.35`, `4096`, `50`,
`50000`, `256`, `2000`, `1.5`, `0.0625`, `0.25`, `1e-3`, `88`, `32x8`,
`0.015`, `960`, `540`, `10915`, `1.0453474311811176e-04`, `8` (s),
`512` (tile), `320`, `16384`, `120`, `30`, `60`, `480`, `24`, `0.7`,
`1.43`, `5` (px), `80` (ms), `16` (ms), `200` (log lines), `256`, `4`,
`0.8`, `60` (s timeout), `0.5` (s tick), `1.15` (chart scale).
