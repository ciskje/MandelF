# AGENTS.md — MandelF (windows-c + macos-avalonia + macos-swift)

## Standing decisions (how the owner wants this repo — never regress)

- One repo, one folder per dev environment, named `<platform>-<stack>`
  lowercase (`windows-c`, `macos-avalonia`, `macos-swift`).
- Same folder scheme everywhere: docs at env root, `source/` for code
  (`source.tests/` for C# tests), `packaging/`, `dist/`, `screenshots/`.
  Toolchain defaults never override this (SPM paths mapped explicitly).
- No `SPEC*.md` files: the normative spec lives in §1–§8 below.
- No Rust env (`MandelR` abandoned, stays outside the repo).
- One release channel per env, tags carry the env prefix; docs in English.
- Build only for the local platform; repo lives on NAS → redirect all
  heavy I/O (build trees, restores, test temp) to local SSD scratch.

One repo, native builds of the same Mandelbrot viewer:

- `windows-c/` — C# / WinForms / .NET 8, Windows only (imported from MandelC,
  history preserved).
- `macos-avalonia/` — C# / Avalonia 11 / .NET 8, macOS arm64 (copied from the
  unversioned MandelMET folder).
- `macos-swift/` — native SwiftUI / Swift 6, macOS 14+, Apple Silicon.

Normative contracts for all environments: §1–§8 below (formulas, constants,
behavior). Platform details (build, gotchas, paths) live in
`windows-c/AGENTS.md`, `macos-avalonia/AGENTS.md` and `macos-swift/AGENTS.md`.
This file holds the shared workflow, versioning, releases, and the full
normative specification. Build on each machine **only for its own platform(s)**.

## Repository layout (all environments)

Every environment folder follows the same scheme:

```text
<env>/
  AGENTS.md README.md CHANGELOG.md TODO.md   // docs (SPECS.md/QUESTIONS.md where present)
  source/          // code (source.tests/ for C# tests)
  packaging/      // Info.plist/.iss + make script (reproducible artifacts)
  dist/           // final artifacts only (.app+dmg / setup.exe), regenerable,
                  //   gitignored — keep the last 2 per environment locally
  screenshots/    // README images
```

Regenerable and always gitignored (root `.gitignore`): `bin/`, `obj/`,
`.build/`, `published/` (dotnet intermediate), `dist/` contents.
`windows-c/` predates the scheme: `packaging/` + `dist/` adoption happens
on the Windows machine (see TODO.md).

## Change workflow

1. A change request updates the shared spec/intents first (§1–§8 below).
2. Implement **only for the local platform**; the other platform implements
   the same intent later on its own machine.
3. Same rule for fixes: if the bug is in a shared intent, mirror the fix.

## TODO.md (classify first)

On every change request, first decide the scope — `generic`, `windows-c`,
`macos-avalonia` or `macos-swift` — and append a row to `TODO.md` with the platform note
before implementing:

- `generic`: implement locally now; the row stays open until every environment
  has it (next build on another platform picks it up from TODO.md).
- environment-only: implement and rebuild the app locally; others ignore it.
- Mark the local environment done in the row; mirror generic intents here too.
- Done rows are archived (deleted here) at each release; history lives in
  tags and release notes, not in TODO.md.

Legend per row: `win-c:[x]` / `mac-avalonia:[x]` / `mac-swift:[x]` = shipped
in that environment.

## Versioning (lockstep X.Y.Z)

Same version number on all environments: `X.Y.Z` = the same intent set.
An environment publishes `X.Y.Z` when it has implemented those intents;
if one lags, its release waits (never stamp a version whose intents the
code does not have).

- Z = bugfix/tweak, Y = new feature/behavior, X = upheaval.
- Docs/refactor/build never change the version.
- Current: `windows-c` `2.24.2`, `macos-swift` `2.24.2`,
  `macos-avalonia` `2.22.7` (catch-up pending, see TODO.md).
- Single source per environment: `<Version>` in its `.csproj`
  (`windows-c` also mirrors it into `AppVersion.cs`; the two must match).
  `macos-swift` uses `source/MandelCore/AppVersion.swift`.

## Tags & releases

One channel per environment, tags always carry the environment prefix:

- `windows-c`: tag `windows-c-vX.Y.Z` + `MandelbrotViewer-Setup-X.Y.Z-win-x64.exe`
  (Inno Setup, per-machine install). Command in `windows-c/AGENTS.md`.
- `macos-avalonia`: tag `macos-avalonia-vX.Y.Z` + `MandelbrotViewer-X.Y.Z-arm64.dmg`.
  Command in `macos-avalonia/AGENTS.md`.
- `macos-swift`: tag `macos-swift-vX.Y.Z` + `MandelSwift-X.Y.Z-arm64.dmg`
  (`.app` bundle + Applications symlink, ad-hoc signed). Command in `macos-swift/AGENTS.md`.
- Every version bump is published (tag + release with artifact), otherwise
  Latest lags behind the code. Keep the **last 2 releases per environment**.
- Release notes **always in English**.

## "Generate the application" (build for the current environment)

"Generate the application" always means: build **only for the platform(s) of the
machine the agent runs on**, never the other one(s). Commands in
`windows-c/AGENTS.md` (Build/run) and `macos-avalonia/AGENTS.md` (Build/run).
Don't touch the version (except releases), don't publish
to GitHub unless explicitly requested.

## Source language

Source everything in English: comments, docs, identifiers, commit messages.
Exception: UI strings through the platform loader where localized.

## Line endings

`* text=auto eol=lf` everywhere (LF working copies on all platforms —
the Mac builds from this repo too); only `*.sh` → LF and `*.bat` → CRLF
are enforced explicitly.

---

# Mandelbrot Viewer — Replication Specification (normative, all environments)

This specification is complete and self-contained. It is written for an LLM
agent whose task is to **replicate the application as faithfully as possible**
in a new codebase: same user-visible behavior, same algorithms, same
constants, same engine architecture. Follow the sections in order. Where a
value is given, use exactly that value. Where a contract is stated
(input/output/return), your implementation must honor it.

How to read this spec:

- Sections 1-2 define the project and the shared mathematical model.
- Section 3 specifies the CPU engine; section 4 specifies the GPU engine as a
  pure API-agnostic contract (implement it with whatever GPU API the target
  platform offers).
- Section 5 specifies the main window and all interactive behavior.
- Sections 6-7 specify the auxiliary dialogs, persistence, and CLI.
- Section 8 is an acceptance checklist: after implementing, verify each item.

This spec is **fully platform-agnostic**: it names no operating system, no UI
toolkit, and no GPU technology. Every such choice — target OS, desktop UI
toolkit, GPU API, packaging — is delegated to the implementer (§1.6). What
binds you is the behavioral contract, the mathematics, and the constants;
not the technology that realizes them.

---

## 1. Project Overview

### 1.1 What the application is

An interactive Mandelbrot/Julia fractal explorer for desktop computers. The
target platform is **not specified** by this spec — the implementing LLM
chooses it (any modern desktop operating system is acceptable). The user can
zoom/pan in real time, choose a color palette, an antialiasing level, a
precision (32/64-bit for the CPU and GPU engines), and one of two rendering
engines:

- **CPU**: always available, multicore, SIMD-vectorized, 32- or 64-bit.
- **GPU**: a real-time-capable GPU engine (float or double precision) backed
  by whichever GPU API the host platform provides; the spec defines only its
  interface and observable behavior, not the technology.

Auxiliary features: standardized cross-engine benchmark window, high-resolution
tiled PNG export, zoom-video MP4 export (via ffmpeg), "back to set" animation,
view history, favorite zones (JSON files), settings persistence, log/diagnostics
window, and command-line diagnostics/benchmarks.

### 1.2 Tech stack (replicate with equivalent choices)

| Concern | Choice |
|---|---|
| Language / runtime | C# 12, .NET 8 (the reference language core; the deployment target OS is the implementer's choice) |
| UI | A desktop UI toolkit of the implementer's choice, providing: a window, a menu bar, a toolbar (buttons, combo boxes, a numeric spinner, checkboxes, radio groups), an image view surface, a GPU-hostable panel surface, a status bar, tooltips, modal dialogs, and keyboard shortcuts — laid out exactly as §5 specifies, with the same labels, tooltips, and shortcuts |
| Threading | `System.Threading.Tasks.Parallel` (partitioner), async/await, `CancellationToken` |
| SIMD | `System.Numerics.Vector` (double and float) with scalar fallback |
| Unsafe | Unsafe pointer code for direct pixel-buffer writes (the reference implementation locks a bitmap's pixel memory and writes through a raw pointer; the contract is simply "direct write access to the 32-bit ARGB pixel buffer") |
| GPU engine | Any GPU API available on the chosen deployment platform, accessed only through the §4 contract: device enumeration/selection, kernel (or shader) execution, device buffers, a palette LUT texture, double-buffered presentation for real-time rendering, offscreen render targets for capture/preview/benchmark, and completion queries (or equivalent) for benchmark timing. FMA contraction is allowed (speed over CPU bit-identity). |
| JSON | `System.Text.Json` |
| Packaging | Self-contained single-file publish for the chosen deployment platform; app icon embedded |

Project-level settings: `OutputType=WinExe`, `Nullable=enable`,
`ImplicitUsings=enable`, `InvariantGlobalization=true`, version `2.22.7`
(`Major=2, Minor=22, Patch=7`; UI displays `X.Y` when `Patch==0`, else `X.Y.Z`).

### 1.3 Source file map

Each source file, its role, and its key public surface. A faithful replica
should keep the same module decomposition (one file per area listed below).

```
MandelbrotViewer/
├── Program.cs            Entry point. Parses CLI commands
│                         (--diag-gpu, --bench-gpu, --bench-cpu;
│                         --bench-cpu takes "float" mode, all --bench-*
│                         accept an optional device/card positional and
│                         "--csv file"). Applies the embedded app icon to any
│                         form.
├── AppVersion.cs         Version constants (2.22.7), Full and Display strings.
├── RenderEngine.cs       enum RenderEngine {Cpu, Gpu} +
│                         RenderEngineInfo.IsAvailable / DisplayName.
├── AppLog.cs             In-memory ring of the last 200 timestamped log lines
│                         ("HH:mm:ss  message"), thread-safe.
├── Settings.cs           AppSettings record (JSON, see §7.1) + Load/Save with
│                         tolerance for missing/corrupt files, MaxIter clamp.
├── Palette.cs            Palette enum (order is the dropdown index, persisted)
│                         + PaletteColors: 7 palettes × 5 stops, LUT builder
│                         (4096 entries) and LUT color lookup.
├── Mandelbrot.cs         CPU engine: Render/RenderTile (4 code paths:
│                         scalar/vector × float/double), early-out test,
│                         AutoIterForScale, CPU benchmarks (double + float).
├── GpuEngine.cs          GPU engine (API-agnostic, §4): GpuViewParams
│                         (88-byte blittable struct), init/device selection,
│                         launch-layout autotune, render + benchmark, device
│                         LUT, gating, capture/preview, disposal. Implements
│                         its host-side calls with whatever GPU API the
│                         platform provides.
├── GpuKernels.cs         GPU kernel/shader sources (4 programs: float and
│                         double render — on-chip SSAA + LUT + Julia + tile
│                         mapping — and float and double benchmark,
│                         iterations only), written in the platform's GPU
│                         shading language.
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
├── Diagnostics.cs        CLI: --diag-gpu (device enumeration + init test,
│                         writes diag_gpu.txt), --bench-gpu, --bench-cpu.
├── LogForm.cs            Read-only log/diagnostics dialog with copy button.
├── app.ico               Application icon (embedded + csproj ApplicationIcon).
└── MandelbrotViewer.csproj
```

Supporting documentation (not part of the replica, but the replica should keep
equivalents): `AGENTS.md` (workflow), `SPECS.md` (user-visible behavior),
`CHANGELOG.md`, `README.md`, `TODO.md`, `QUESTIONS.md`.

### 1.4 Build and run

- Build: `dotnet build` (SDK 8.0).
- Run (dev): `dotnet run --project MandelbrotViewer`.
- Publish: self-contained, single-file, for the chosen deployment platform,
  output to `published/`.
- GPU kernel sources are compiled by the host GPU API toolchain at build or
  first-run time (whichever the chosen API supports); if the toolchain is
  unavailable at build time the checked-in compiled artifacts are kept
  (warning only).

### 1.5 Global invariants (all engines)

1. **Identical workload contract.** Every engine renders the exact same
   per-subsample math (same escape loop, same smooth formula, same early-out,
   same LUT entries) so engines are comparable. Benchmark programs of all
   engines run the identical iterations-only escape loop (no coloring, no
   early-out in GPU benchmark programs; the CPU benchmark *does* apply the
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

### 1.6 Freedom of implementation

This spec is deliberately platform-agnostic. The implementing LLM is free
— and expected — to choose:

- the **target operating system** (any modern desktop OS),
- the **desktop UI toolkit** (whatever best serves the §5 layout, tooltips,
  menus, shortcuts, and dialogs),
- the **GPU API** (whatever the deployment platform offers; §4 binds only
  the interface and behavior),
- the **packaging and distribution** format.

What is **binding**, regardless of those choices: every formula, constant,
table, and numeric threshold in this document; the behavioral contracts of
§1.5, §2, §3, §4, §5, §6, and §7; and the acceptance checklist of §8. The
document must never be read as requiring a specific OS, toolkit, or GPU
technology: wherever a concept is stated in terms of a reference
implementation (an image surface, a pixel buffer, a presentation target, a
completion query), the reference implementation's name is illustrative, not
prescriptive.

Key combinations in this document (e.g. `Ctrl+O`, `Alt+F4`,
`Alt+Left/Right`) are the reference bindings; on the target platform they
map to that platform's standard equivalents (primary modifier, window-close
shortcut, back/forward navigation), preserving the same gesture and intent.

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
`0 <= sx, sy < k`. The GPU engine may express the same mapping with
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

  Known discrepancy (recorded, formula kept as written): the formula text
  evaluates to 1011 at scale 9.36, not ≈1944; sibling replicas implement the
  formula exactly (`AutoIterForScale(9.36) = 1011`,
  `AutoIterForScale(1.0453474311811176e-04) = 10915`) and never adjust
  constants to match prose — see `macos-avalonia/QUESTIONS.md`.

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
- **Lookup** (CPU `ColorFromLut`, GPU `LutColor`): given `smooth` and
  `invMaxIter = 1/maxIter`:

```text
pos = smooth * invMaxIter * 4096 - 0.5
if pos <= 0 → lut[0];  if pos >= 4095 → lut[4095]
i0 = (int)pos;  f = pos - i0
r = lutR(i0) + f * (lutR(i0+1) - lutR(i0))   (same for g, b; truncate)
```

  CPU and GPU run the lerp in double; the GPU may alternatively sample the
  4096-entry 1D texture with hardware linear filtering (equivalent within
  1 LSB).

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
render path and the GPU float kernel it runs in **double** on the
double-precision coordinates (host-order), only the escape loop is float.
The GPU float render path may also run the same test in float.

### 2.6 Precision model

- **CPU and GPU**: user chooses 64-bit (double, default) or 32-bit (float).
  No automatic fallback: 32-bit renders everything in float (any zoom,
  Mandelbrot and Julia) and is less precise at deep zoom by user choice.
  (A GPU implementation that only supports float may expose 64-bit as
  unavailable, but the contract above still applies to the 32-bit path.)
- `WantsDouble(scale) = scale < 1e-3`: when true and the GPU engine is in
  use, the main-window status bar appends ` [beyond float!]` — float
  precision is insufficient and CPU/GPU double is recommended.

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

The GPU engine exposes the same `Render`/`RenderTile` shape (its offscreen
`RenderPreviewToImage` returns a 32-bit ARGB image, `null` on failure).

---

## 3. CPU Engine (`Mandelbrot.cs`)

### 3.1 Rendering pipeline

1. Compute the shared view math (§2.2) and fetch the cached 4096-entry LUT
   (§2.4) for `(palette, maxIter)`; precompute `invMaxIter`.
2. Obtain direct write access to the image's 32-bit ARGB pixel buffer once
   (the reference implementation locks the bitmap, write-only, and writes
   through a raw `int*` pointer). Never use an intermediate color buffer.
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
| `RenderRowVectorDouble` | SIMD | blocks of `Vector<double>.Count` lanes + scalar tail |
| `RenderRowVectorFloat` | SIMD | blocks of `Vector<float>.Count` lanes + scalar tail |

- The vector core processes one subsample row for a full lane block:
  lane `i` sits at global grid `gridBase + i*k`. Per-lane coordinates are
  `originX + gb*pixelSize` (exact integer grid, one multiply + one add), so
  **the double vector path is bit-identical to the scalar path** regardless
  of block/tile decomposition. The float path is approximate vs double
  (like GPU 32-bit) and must agree lane-by-lane with its scalar tail.
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
  budget elapses (standard: 8 s, §6.1). No coloring, no smoothing, no SSAA
  averaging; each grid cell is one elementary sample.
- `BenchmarkCpuFloat`: identical workload through the float vector core
  (double bulb classification + float escape loop), a separate workload with
  its own history (like GPU 32-bit vs 64-bit).
- Same row partitioning as rendering (`chunk = max(8, H/(procs*8))`).
- **Applies the cardioid/bulb early-out** (known interior counts `maxIter`
  without looping) — intentionally different from the GPU benchmark programs.
- Progress reported to the UI at most every 1 s (`BenchmarkProgress.
  ReportInterval`) plus a final report, to avoid stealing time from the test.
- Returns `(TotalIters, Seconds, Frames)`; the metric is
  `frames * (W*H*AA²) / seconds` elementary samples/s (§6.1).
- Cancellation checked per frame and per row range.

---

## 4. GPU Engine — API-agnostic contract (`GpuEngine.cs`, `GpuKernels.cs`)

The GPU engine is specified **only by interface and observable behavior**.
Implement it with whichever GPU API the host platform provides (desktop
rendering compute API, shader compiler, device memory, presentation
facilities). Nothing in this section depends on a particular technology;
every requirement below is stated in terms of the interface, the math, and
the numbers, which are the contract.

### 4.1 Required host-side interface

The application code may rely on exactly this surface (names are the
reference names; the shape and semantics are the contract):

`Image` below means an abstract **32-bit ARGB image**: width × height
pixels, row-major, one little-endian ARGB word per pixel (the reference
implementation is a bitmap; any toolkit image type with the same layout
satisfies the contract).

```text
static string[] AdapterNames()
  → GPU device names available on this machine, sorted; software-only /
    virtual renderers excluded.

bool TryInitialize(string? deviceName)
  → selects and opens a device (null name = automatic selection), loads the
    compiled GPU programs, autotunes (§4.5), and leaves the engine usable.
  → false on any failure, with a human-readable LastError set.

string LastError              // reason of the last failed step, if any
string DeviceShortName        // device name with vendor marketing prefixes
                              // removed (e.g. "GeForce ", "Radeon ")
string Details                // capability summary string (§4.4 format)
bool WantsDouble(double scale)→ scale < 1e-3  (§2.6)

bool Render(Image img, cx, cy, scale, maxIter, palette, k, ct,
            julia, jcx, jcy)
  → whole-frame render into the image (32-bit ARGB pixel buffer); returns
    whether double precision was used (the form uses it for the status label).

void RenderTile(Image img, ..., offsetX, offsetY, fullWidth, fullHeight, ...)
  → tile render, pixel-identical to the whole-frame render (§1.5.2).

Image? RenderPreviewToImage(w, h, ..., fullW, fullH, offsetX, offsetY)
  → offscreen colored render for preview/export; null on failure.

Image Capture()
  → pixel copy of the current on-screen frame.

(double seconds, int frames) Benchmark(int w, int h, int maxIter,
        CancellationToken ct, Action<double> tick)
  → standardized iterations-only benchmark, 8 s budget (§4.8).

void Dispose()
  → best-effort release of all device resources; never throws.
```

Global rules:

- **Single gate.** All rendering and benchmark operations are serialized
  through one lock so they never interleave.
- **Fallback.** If initialization or a render fails, the application falls
  back to the CPU engine and shows `LastError` (or the failure reason) in
  the status bar. The user never sees a crash.
- **Buffers.** Device and host pixel buffers are allocated once and reused
  between frames; reallocation only when the pixel count changes.
- **Cancellation.** Every operation takes a cancellation token and checks it
  at natural points (before/after each submission batch, before copy-back).
- **Precision.** Both a float (32-bit) and a double (64-bit) variant of each
  program must exist; the engine selects the variant from the app's
  precision setting. FMA contraction is allowed: float and double GPU
  results may differ from the CPU double path by rare single-pixel flips on
  chaotic boundaries (documented, amplified only in 32-bit deep zoom).

### 4.2 View parameters struct (bit-exact contract)

`GpuViewParams` is a blittable struct shared with the GPU side: **same field
order, 8-byte packing, exactly 88 bytes** (4 doubles = 32, 5 ints = 20, pad
4, 2 doubles = 16, 4 ints = 16). A static size assertion in the GPU-side
code and a `Marshal.SizeOf` check in the host code enforce it at
initialization:

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

### 4.3 The four GPU programs (semantics, not syntax)

All programs run with a **2D launch grid**: element
`(x = blockIdx.x*blockDim.x + threadIdx.x, y = blockIdx.y*blockDim.y +
threadIdx.y)` maps directly to pixel/sample `(x, y)` — no index
division/modulo; 32-wide blocks keep the hardware's warp-like threads on a
single row (coalesced writes).

**Render program (float and double variants)** — one element per **output
pixel**:

```text
originX = CenterX - FullW*k*0.5*PixelSize        // host op order
for sy in 0..k-1, sx in 0..k-1:
    globalBx = (OffsetX + x)*k + sx              // global k-times grid
    globalBy = (OffsetY + y)*k + sy
    (px, py) = complex coordinate of (globalBx, globalBy)   // §2.2
    if JuliaOn: z = (px, py);  c = (Jcx, Jcy)
    else:       z = (0, 0);    c = (px, py)
    if (not JuliaOn) and InteriorBulb(px, py):  // §2.5, skip loop
        color = black
    else:
        run the §2.1 escape loop in float or double
        if escaped:
            smooth = iter + 1 - log2(0.5*ln(max(zx2+zy2, 4)))
            color  = LutColor(lut, smooth, MaxIter)   // §2.4 lookup
        else:
            color  = black
    accumulate the k*k channel sums (64-bit)
pixels[y*W+x] = average of the accumulated ARGB
```

**Benchmark program (float and double variants)** — one element per
**sample** (`W x H` = the elementary grid), `cx = CenterX + (x - W*0.5)*
PixelSize`, `cy = TopY + y*PixelSize`, runs the §2.1 loop, and stores the
iteration count. **No coloring, no smoothing, no early-out** — so GPU
benchmark programs measure the identical workload across engines.

**LutColor** mirrors `PaletteColors.ColorFromLut` (double lerp from the
precomputed `1/maxIter`; FMA may fuse the final adds — documented
difference from CPU).

### 4.4 Initialization and device selection (`TryInitialize`)

1. Verify the `GpuViewParams` size (88) and load the compiled GPU programs
   (from the embedded/checked-in artifacts; compile from source only if the
   host toolchain is present).
2. Enumerate GPU devices (name + total memory). No name given → candidates
   sorted by **memory descending** (auto = most capable); a name given →
   exact match.
3. For each candidate: open the device context, load the programs, look up
   the four program handles, then autotune (§4.5). On failure record
   `LastError` and try the next candidate.
4. `Details` (shown in diagnostics) format:
   `"SM x{sm}, CC {major}.{minor}, {mem} MB, block {fx}x{fy}/{dx}x{dy} (32/64-bit), occ {occF}/{occD} blocks/SM"`
   (with the equivalent capability terms of the chosen API: shader-unit
   count, compute level, memory, tuned block layout, occupancy).

### 4.5 Launch-layout autotune (`TuneBlocks`)

- Candidates: `(32,4), (32,8), (32,16), (32,32)` threads/element-block per
  precision (width 32 keeps warp-like threads on single rows; heights sweep
  128/256/512/1024 elements).
- Probe grid: the **real benchmark grid 960x540** at reduced iterations
  (2000), center `(-0.75, 0)`, pixel `0.01/960` — same occupancy regime as
  the measured workload (a small grid would starve the units and rank
  differently).
- After a warmup (8 launches of the float bench program at 32x8 + sync, to
  reach steady clocks and compiled programs), time each candidate with
  rounds **interleaved** (candidate order inside each round), **median of 5
  rounds**.
- Keep the default `32x8` unless the winner's median is clearly faster:
  only replace it when `(defaultMedian - bestMedian)/defaultMedian >= 0.015`
  (1.5% margin), so noise cannot flip the layout between runs.
- Failure of any timing → `+inf`; default 32x8 is the safe outcome.

### 4.6 Rendering (`Render` / `RenderTile`)

Under the single gate:

1. Ensure the device palette LUT matches `(palette, maxIter)` — the host
   4096-entry table (§2.4) is uploaded once per combination and shared by
   all frames (`EnsureLut`).
2. Reuse device + host pixel buffers between frames (reallocation only when
   the pixel count changes).
3. Launch the float or double render program on the tuned 2D grid
   (`gridX = ceil(W/bx)`, `gridY = ceil(H/by)`), synchronize, check the
   cancellation token, copy back to the host `int[]`, and write into the
   target image's 32-bit ARGB pixel buffer.
4. Return `useDouble` (the form uses this for the status label
   `GPU-{double|float} {DeviceShortName}`).

### 4.7 Real-time presentation, capture, preview

- **Real-time render**: the app hosts the engine on the view panel (a panel
  surface the engine attaches to) and drives it with a **16 ms timer**
  (~60 fps) plus a dirty flag: each tick resizes if needed and renders one
  frame only when dirty; pan/zoom set dirty, so they are immediate.
- **Presentation surface**: a double-buffered presentation target (the
  reference implementation is a swapchain), 8-bit RGBA, stretch scaling,
  flip-sequential (or equivalent) presentation, windowed, ignoring alpha.
- **Resize**: resize the presentation buffers to the current panel size
  (stretch during the drag, full quality on release) and recreate the
  render target.
- **Capture** (`Capture()`): copy the current backbuffer to a CPU-readable
  (staging) resource, map it, and copy row-by-row (honoring the row pitch)
  into a 32-bit ARGB image.
- **Preview** (`RenderPreviewToImage`): render the colored program into an
  offscreen 8-bit RGBA render target (byte layout identical to the target
  image, so channels do not come out inverted), read back as in capture,
  then restore the presentation target. Supports full tile parameters
  (fullW/fullH/offset) for pixel-identical tiled export.

### 4.8 GPU benchmark (standard workload, §6.1)

Two equivalent implementations are provided, both honoring the same rules:

- **Interactive benchmark** (adaptive batching):
  1. Allocate the iterations buffer once (`W*H*4` bytes) and reuse it.
  2. Estimate per-frame time from `batchMin = 4` submissions, then queue
     `batch = clamp(round(0.8 s / frameSeconds), 4, 256)` programs per
     synchronization (≤ ~0.8 s of queued work, keeping slow GPUs clear of
     the OS watchdog while fast GPUs run deep). This measures compute
     throughput, not submit latency.
  3. Loop `submit batch → synchronize` until the budget (8 s) elapses;
     count frames; report progress at most every 1 s (`TotalIters` = 0 —
     GPU benchmarks measure in frames).
  4. Cancellation checked around each batch; returns `(0, seconds, frames)`.
- **Offscreen benchmark** (standardized, no presentation — excludes desktop
  compositor and cross-GPU copies):
  1. Create an in-device-memory 8-bit RGBA render target (no swapchain,
     **no present**) plus a ring of **256 event queries**.
  2. VRAM pre-check: required = `w*h*4*2` bytes vs dedicated+shared (when
     known); fail fast with a clear error instead of a hang.
  3. Pipeline state (constants, program, render target, viewport) is set
     **once** before the loop; per frame only `draw + end(query)`.
  4. **In-flight depth** sized from a measured frame time: run a small batch
     (`BenchFlightMin = 4`) to estimate, then
     `inflight = clamp(round(0.8 s / frameTime), 4, 256)`
     (`BenchTdrSafeSeconds = 0.8` — never queue more than ~0.8 s of work,
     avoiding the OS watchdog / device-reset on slow cards; deep queues
     saturate fast cards).
  5. Main loop: submit frames with their event queries; drain completed
     ones (non-blocking status check); tick callback every ≥ 0.5 s.
  6. **DrainOne** (blocking wait for the oldest frame): tight spin-poll,
     with cancel check, device-lost check, and a **60 s per-frame timeout**
     every 1024 polls — a hung/reset device becomes a readable
     `InvalidOperationException("GPU hung during the benchmark...")`, and
     an unresponsive GPU a `TimeoutException`. Never wait forever.
  7. On exit: flush and drain all pending; the time stops at the
     completion of the last frame; returns `(completedSeconds, completed)`.

### 4.9 Failure and fallback

- Init failure → the GPU radio stays disabled; `LastError` shown in the
  status bar / diagnostics.
- Runtime render failure (e.g. in the 16 ms tick) → `FallbackToCpu(reason)`:
  dispose the engine, disable the radio, switch to CPU, status shows
  `"{reason} — falling back to CPU"`.

---

## 5. Main Window (`MandelbrotForm`)

### 5.1 Window frame

- Title: `"Mandelbrot Set Viewer v{AppVersion.Display}"`; app icon applied
  from the embedded resource (never blocking).
- Client size **1152x720** (logical pixels, font-based auto-scaling);
  the form intercepts shortcut keys even when a child control has focus
  (keyboard preview); position/size/maximized state restored from settings
  when the saved rectangle intersects a screen.
- Closes via the platform's standard window-close shortcut (reference:
  `Alt+F4`); on close: save settings, stop timers, cancel pending
  renders/animation, dispose both GPU engines.

### 5.2 Layout

Top-to-bottom: menu strip → toolbar (top panel, docked at the top, height
72, padding 8/4, two flow-layout rows, no wrap, controls left-clustered,
margins 3/1) → view area (fills the remaining space) → status strip.

**Row 0** (left to right): `↩ Home` button (70x23, a symbol-capable UI
font, → back-to-set animation) → `Reset` (60x23) → `Benchmark` (100x23) →
"Iterations:" label + a numeric spinner (width 70, Min 50, Max 50000,
Increment 50, value 256) + `Auto` checkbox → "Palette:" + a combo box
(width 110, drop-down list, items `Fire, Ice, Thermal, Ocean, Violet,
Desert, Forest`, index 0 default) → "AA:" + a combo box (width 60, items
`1x, 2x, 4x, 8x`, index 0 default).

**Row 1**: "Engine:" + radio group `CPU` (checked by default) / `GPU`
(disabled until initialized) → "Precision:" + radio `32` / `64` (**64
checked by default**; applies to the CPU and GPU engines) → "GPU:" label +
a combo box (width 200; initially hidden with the CPU engine, visible with
the GPU engine; item 0 = `Auto` plus the sorted device list from the GPU
engine, filled on first show).

**View area**: two overlapping surfaces that fill the remaining space — an
image view (black background, crosshair cursor, content scaled to fill) for
bitmap engines and a GPU panel hosting the GPU engine's presentation
surface; the active one is shown per engine. Both share the same mouse
handlers.

**Status strip**: a stretchable label (left, showing the live status text)
+ a static hint label (gray, the standard UI font at 8 pt):
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
- 32/64: "CPU/GPU precision 32-bit (float): faster, less precise." / "...64-bit (double): more precise, slower."
- CPU: "CPU multicore engine (always available)"
- GPU: "GPU engine: real-time GPU rendering (float/double, uses the GPU
  available on this machine)"
- File items: load zone "Reload the view saved in a JSON file (Ctrl+O)"; save
  zone "Save the current view (center, scale, iterations) to a JSON file (Ctrl+S)";
  save image "Save the current image as PNG (Ctrl+Shift+S)"; benchmark
  "Standard benchmark: 8s at high iterations (Ctrl+B)"
- Generate: "Generate output from the view: PNG screenshot or MP4 zoom video";
  screenshot "Screenshot at view resolution with optional AA (Ctrl+Shift+E)";
  video "MP4 zoom video with optional AA (Ctrl+Shift+V, requires ffmpeg)";
  back to set "Back to set with RealTime animation (Esc to stop, Ctrl+Shift+R)"
- About: "About MandelC# (F1)"

### 5.3 Menus

**&File**: Load &zone... (Ctrl+O) | Save &zone... (Ctrl+S) | — | Save &image
as... (Ctrl+Shift+S) | — | Bench&mark... (Ctrl+B) | — | E&xit (platform close).

**View** (inserted at position 1, built in code — dynamic favorites):
Back (Alt+Left) | Forward (Alt+Right) | — | &Julia mode (Ctrl+J, checkable item,
checked state persisted) | — | Save favorite zone… (Ctrl+D) | Favorite zones
(submenu, rebuilt on open: `*.json` files in the zones dir, ordered
insensitively, `(none)` disabled placeholder) | Delete favorite (submenu,
same list).

**&Generate**: &Screenshot... (Ctrl+Shift+E) | &Zoom video... (Ctrl+Shift+V) |
Back to set (&RealTime) (Ctrl+Shift+R; while running the text switches to
"Stop (Esc)").

**&Help**: &About... (F1) | — | Show &log / diagnostics....

### 5.4 Status text formats

- While computing: `"Computing [preview ]{W}x{H}, iter={maxIter}..."`.
- After a CPU render: `"Center {cx:+0.000000;-0.000000} {cy:+0.000000;
  -0.000000}i | width {scale:E2} | iter {maxIter}[ (auto)] | {Palette}[ AA{k}x]
  | engine CPU-{double|float}[ | Julia c={jcx} {jcy}i][ (preview)]"`.
- After a GPU render: same shape with `engine GPU-{double|float}
  {DeviceShortName}`, plus ` [beyond float!]` when `scale < 1e-3`.
- Engine problems, GPU switches, and errors are reported verbatim in the
  status bar (e.g. `"GPU on '{name}': {error} — switching to CPU"`).

### 5.5 Mouse interaction (shared by the bitmap view and the GPU panel)

- **MouseEnter**: the control takes focus (so the wheel arrives at it).
- **MouseDown** (Left/Right): begin a potential drag; record position; push
  the current zone as `dragStartZone`.
- **Drag vs click threshold: 5 px** (Euclidean distance from the down
  position; below = click, above = drag).
- **MouseMove while down**: once moved, pan so the point under the cursor
  stays under the cursor (`center += (from - to) * (scale / viewWidth)`),
  and re-render as **preview** (bitmap: half size, AA off, bilinear upscale;
  GPU: full realtime frame) **throttled to 80 ms** via
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

### 5.6 Keyboard (form-level keyboard preview)

- `Esc` cancels the running back-to-set animation (only).
- Ctrl/Alt combinations are ignored (they belong to menus).
- `R` reset | `S` save PNG | `+`/`-` iterations ±50 (clamped to 50...50000).
- Arrow keys pan by **1/10 of the view** (Shift: **1/100**), pushed to
  history; **ignored while the iterations control has focus** (arrows adjust
  the numeric there).

### 5.7 Reset, Save PNG

- **Reset**: push history; restore `(-0.5, 0, 9.36)`; in Julia mode also
  reset `c` to `(-0.7, 0.27015)` (the mode stays on).
- **Save PNG** (S / File menu): capture the current image (GPU engine:
  on-screen frame capture; bitmap: the last rendered bitmap) via a save
  dialog (filter PNG, default name `mandelbrot.png`); failures show a warning
  dialog.

### 5.8 History

- Two stacks of view zones; **back history capped at 200 entries** (overflow
  drops the oldest, preserving order); pushing to back always clears forward.
- Events pushed: zoom (click/wheel), committed pan (at drag start zone),
  reset, zone load, favorite load, Julia toggle, Julia c-set, arrow pan.
- Back/Forward enable/disable the menu items; `Alt+Left`/`Alt+Right`
  (platform back/forward).
- A zone record is `(CenterX, CenterY, Scale, MaxIter, Julia, Jcx, Jcy)`
  (JSON-serializable; Julia fields have defaults so old files load as
  Mandelbrot).

### 5.9 Zones and favorites

- **Save zone** (Ctrl+S): serialize the current zone to JSON (indented),
  default name `zone.json`, filter "Mandelbrot Zone (*.json)".
- **Load zone** (Ctrl+O): parse; validate finite coordinates and
  `Scale > 0`; on failure show a warning with the reason; on success push
  history and apply (explicit iterations, auto checkbox unchecked, value
  clamped to 50...50000; Julia state restored).
- **Favorites** (subfolder `zone` of the app's per-user data directory,
  i.e. the OS-standard location for per-application user data, in a folder
  named `MandelbrotViewer`): Save favorite (Ctrl+D) must stay inside that
  folder (full-path prefix check, warning otherwise);
  list = `*.json` files ordered case-insensitively; opening one loads it;
  deletion via the Delete favorite submenu. All IO errors surface as
  warning dialogs and never crash.

### 5.10 Engine and GPU switching

- Radio change: set engine; if the GPU engine is not yet initialized, try
  now (with a wait cursor — synchronous init may briefly freeze the UI;
  attached to the GPU panel handle at the current size). Failure → revert to
  CPU, disable the failed radio, status shows the reason.
- `ApplyEngineVisibility`: show the GPU panel vs the bitmap view; GPU label
  + combo visible only with the GPU engine; precision radios enabled only for
  CPU/GPU; re-render (or dirty the GPU frame).
- **GPU combo change**: with the GPU engine → re-init on that device (fallback
  to CPU on failure); with CPU → the selection is remembered and applied when
  the engine changes.
- **Busy cursor** during async bitmap renders: the "busy" cursor
  (arrow + hourglass, UI stays interactive) on the whole window; restore only
  by the **latest** render (token identity check), and the image panels go
  back to `Cross`, not `Default`. Superseded renders must not touch the cursor.
- New install default: engine radio is CPU until the GPU engine initializes
  on first show; if the saved engine is GPU and init succeeds, GPU is
  checked.

### 5.11 Back to set (realtime animation)

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
  (GPU realtime frame or bitmap preview-quality render).

### 5.12 Settings wiring (see §7.1)

- On load: iterations (clamped), auto checkbox, palette index (clamped), AA
  index (clamped), precision (32 if `Single`, else 64), Julia mode + c
  (finite check, else defaults), GPU selection, window bounds/maximized.
- **The current view is NOT persisted** — every launch starts at the full set.
- On close (best effort, never blocks): write the full settings JSON.

---

## 6. Auxiliary Features (benchmark, export, video, log, CLI)

### 6.1 Standard benchmark (shared constants, `BenchmarkStandard`)

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
workload with its own history, like GPU 32-bit vs 64-bit.

**Benchmark window** (`BenchmarkForm`):
- Auto-starts when shown. Info line: engine (+ fallback note), zone
  `960x540 1x AA = 960x540 elementary samples without averaging, 10915 max
  iterations, scale 1.0453474311811176e-04, precision {GPU|CPU}
  {64-bit|32-bit|float} — minimum duration 8 seconds (GPU offscreen, no
  presentation).`
- Before measuring, renders a **colored preview** of the zone (Fire palette,
  AA 1x) — CPU via the normal render, GPU via the offscreen colored render —
  shown in the preview box.
- Live progress: `"{pct}%  |  {rate}"` at most every 1 s; rate shown only
  after 0.2 s of elapsed time. Units: Gpixel/s ≥ 1e9, Mpixel/s ≥ 1e6,
  kpixel/s ≥ 1e3, else pixel/s.
- Result: `"{value} Mpixel/s"` + detail line
  `"{frames} frame [({fps} frame/s)] 960x540 AA1x in {s} s on {device}"`.
  Device label: GPU `DeviceShortName` or CPU name (`Diagnostics.CpuName()`).
- **Bar chart** (custom GDI+ paint, left margin 132, right 56, top 22,
  bottom 24; title "Confronto prestazioni (MPixel/s)", standard UI font 8 pt
  bold):
  horizontal bars sorted descending by value, max = max*1.15, two gridlines,
  bar height ≤ 22 px (62% of row). Historical reference bars (MPixel/s,
  best of 3 standardized runs) — one bar per (engine, device, precision),
  e.g.:
  - `GPU 5070 Ti 32-bit 310.7`, `GPU 4070 SUPER 32-bit 250.1`,
    `GPU 5070 Ti 64-bit 6.7`, `GPU 4070 SUPER 64-bit 5.3`,
    `GPU 5070 Ti 338`, `GPU 4070 SUPER 262.4`, `GPU AMD Radeon 5.2`
  - `CPU 9900X 9.7`, `CPU 9900X float 15.3`
  plus the current `Risultato` bar. Colors: current blue (36,113,163),
  GPU orange (226,126,34), CPU gray (112,128,144).
- **CSV export** (button, enabled after a run): appends one row via
  `BenchmarkCsv.AppendRow` with header on first write:
  `timestamp,engine,device,precision,run,frames,seconds,mpixel_s`
  (UTC ISO-8601 `o` timestamp; seconds `0.00`, mpixel `0.#`, invariant
  culture; fields quoted when they contain `,` `"` or newline).
- Errors: logged to `AppLog` ("Benchmark {engine}: {message}"), shown in the
  detail label, chart refreshed. Cancellation supported (Start button is the
  cancel button).
- While the benchmark runs, if the GPU engine is the active one its timer
  stops and the panel hides (gray sample frame stays on the presentation
  surface); both restore afterwards.

### 6.2 PNG export (`ExportForm`)

- Presets (dropdown, default index 3 = 4K): **Current view** (view size
  clamped to ≥ 320x240), Full HD 1920×1080, 2K 2560×1440, 4K 3840×2160,
  8K 7680×4320, Double 4K 7680×2160, **Custom…** (both sides integers
  **320 ... 16384**, strict validation with the message "Custom dimensions:
  integers 320…16384 px."). The screenshot entry (Generate menu) opens with
  preset = Current view.
- AA dropdown: `As view, 1x, 2x, 4x, 8x` (As view = the active view AA);
  **no automatic AA reduction** at high resolutions (on-chip SSAA).
- Precision radios (32/64) enabled for the GPU engine and CPU only (a GPU
  implementation without double precision exposes only the 32-bit choice).
- Render: **tiled 512×512** working tiles, row-major, on the active engine
  (CPU `RenderTile`, GPU `RenderTile`, and GPU `RenderPreviewToImage`
  with full-image tile parameters — pixel-identical to a whole render),
  copied row-by-row into the result bitmap; cancellable between tiles
  (progress label `"Rendering {w}×{h} AA{k}x…"`); then a PNG save dialog
  (default `mandelbrot.png`).
- Info line shows engine, mode (Mandelbrot / `Julia c=... i`), palette,
  iterations, and the sample budget: `"{w}×{h} ({MPixel} MPixel), AA{k}x —
  {w*k*h*k/1e6} MPixel of samples."`.

### 6.3 Zoom video (`ZoomVideoForm`)

- Renders the transition **from the current view to the full set** at view
  resolution (clamped ≥ 320x240); unavailable when `StartScale/scale < 2.0`
  (warning "You are already at the full set: frame a region first...").
- Frame count dropdown: **60, 120, 240, 480** (default 120); fps: **24, 30,
  60** (default 30); AA: As view/1x/2x/4x/8x; precision as in export.
- Frame i of N: `t = i/(N-1)`, ease-out `te = 1-(1-t)^3`,
  `scale = startScale * (9.36/startScale)^te`, center linear in scale toward
  `(-0.5, 0)`, iterations = `AutoIterForScale(scale)` per frame.
- Frames render on the active engine, saved as `f0000.png ... f{N-1:0000}.png`
  in a temp folder `MandelC#-video-{guid8}`; progress bar +
  `"Frame {i+1}/{N}…"`; cancellable; the temp folder is deleted on success
  or failure, **kept** when ffmpeg is absent (it is then the result, with an
  Open button).
- **ffmpeg** lookup: an `ffmpeg` executable found on the system.
  Encoding (worker thread, cancellable, kill tree on cancel):
  `-y -framerate {fps} -i f%04d.png -frames:v {N} -vf "pad=ceil(iw/2)*2:ceil(ih/2)*2" -c:v libx264 -pix_fmt yuv420p -crf 18 {out}`
  (pad to even dimensions for yuv420p). stderr drained asynchronously during
  the wait (reading after `WaitForExit` deadlocks once the pipe fills);
  non-zero exit → error with the last 10 stderr lines.

### 6.4 Log / diagnostics (`LogForm` + `BuildDiagnosticLog`)

- Read-only, a monospace font at 9.75 pt, 720x480 (min 440x320), centered
  on parent,
  with "Copy to clipboard" and "Close" buttons.
- Content (in order): header `"Mandelbrot Set Viewer   v{Full}"` + open time
  + OS + .NET runtime; **=== Engine ===** (selected + in use); **===
  GPU engine ===** (Ready, device in use, last error, device list, details
  incl. tuned block layout); **=== GPU selection ===** (selected device /
  Auto, precision); **=== Saved settings ===** (engine, GPU, palette, AA,
  iterations, precision); **=== Event log ===** (the AppLog ring: last 200
  lines `HH:mm:ss  message`, or `(empty)`).

### 6.5 About dialog

Version `v{Display}`, engine description, then the full command list (left
zoom 2x, right zoom out 2x, wheel, drag, R/S/±/arrows, Alt+Left/Right,
Ctrl+D favorites, Ctrl+J Julia, palette list, auto iterations, AA, engines,
GPU Auto, File menu zones).

### 6.6 Command-line diagnostics (`Program.cs`, `Diagnostics.cs`)

- `--diag-gpu [device]`: enumerate GPU devices (with the enumeration error
  if any), detailed per-device listing (name + memory or failure per
  device), then a headless initialization test (800x600) printing IsReady,
  device name, LastError, and a test render (200 iterations).
- `--bench-gpu [device] [--csv f]`: standardized benchmark, **3 runs** of
  the 8 s budget per device (all devices, or the named one), offscreen with
  event queries; prints MPixel/s per device and the best; `--csv f`
  appends one row per run (same format as §6.1).
- `--bench-cpu [float] [--csv f]`: CPU benchmark (3 runs); `float`
  switches to the float workload.
- `--diag-gpu` also supports writing `diag_gpu.txt` next to the executable
  (devices, ready, device, details, error; or the exception).
- Argument parsing: first positional = device (for `--bench-cpu` it is the
  precision mode); `--csv file` anywhere after the flag.

---

## 7. Persistence (settings, zones, favorites)

### 7.1 Settings (`settings.json` in the app's per-user data directory)

The per-user data directory is the OS-standard location for
per-application user data (a folder named `MandelbrotViewer`); its concrete
path is the implementer's choice per target OS.

JSON, indented, `System.Text.Json`. Fields (defaults in parentheses):

```text
IterAuto   bool  (false)          // automatic iterations on/off
MaxIter    int   (256)            // manual iteration limit, saved clamped 50..50000
Palette    int   (0)              // palette index (0 Fire .. 6 Forest)
AaIndex    int   (0)              // AA dropdown index (0=1x,1=2x,2=4x,3=8x)
Engine     string ("Gpu")         // enum name: Cpu | Gpu
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

### 7.2 Zones and favorites

- Zone JSON (save/load + favorites, indented): `ViewZone` record
  `{CenterX, CenterY, Scale, MaxIter, Julia, Jcx, Jcy}`; Julia fields
  optional with defaults `false, -0.7, 0.27015` (old files load as
  Mandelbrot).
- Validation on load: finite center/scale and `Scale > 0`.
- Favorites directory: subfolder `zone` of the app's per-user data
  directory (§7.1); favorite saves must stay inside it.
- App event log (`AppLog`): in-memory ring of the last **200** lines,
  thread-safe, timestamps `HH:mm:ss` + two spaces; used by the log dialog and
  benchmark error reporting. (In-memory only — not written to disk.)

---

## 8. Acceptance Checklist (verify your replica against this)

After implementing, check each item. **Math/behavior** (runnable on any
machine):

- [ ] Initial view is centered at `(-0.5, 0)` with complex width `9.36` and
      renders the full Mandelbrot set with black interior.
- [ ] `AutoIterForScale(9.36)` = 1011 (formula as written; the SPEC prose
      "≈1944" is a known discrepancy, see §2.3);
      `AutoIterForScale(1.0453474311811176e-04)` = 10915; manual limit
      clamps to 50...50000; `+`/`-` keys step by 50.
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
- [ ] Favorites save only inside the app's per-user data directory
      (`zone` subfolder); menus list, open, and delete them.
- [ ] Settings persist (engine, GPU, palette, AA, iterations + auto,
      precision, Julia, window bounds) and a corrupt settings.json falls back
      to defaults; the view itself is not persisted.
- [ ] Benchmark window: 960×540, AA 1x, 10915 iterations, 8 s budget,
      colored preview first, live progress ~1/s, MPixel/s result, CSV row
      format of §6.1; cancel stops the run.
- [ ] Export dialog: presets produce the exact resolutions of §6.2; custom
      values outside 320...16384 are rejected; result PNG matches the view.
- [ ] Zoom video: blocked at the full set; frames ease-out logarithmically to
      scale 9.36 with auto iterations per frame; ffmpeg MP4 when available,
      otherwise the PNG sequence is kept.
- [ ] Back to set: 120 frames @ 30 fps, AA forced 1x during, Esc cancels,
      blocked when already at the full set (`StartScale/scale < 2.0`).
- [ ] Status bar shows center (6 decimals), width (E2), iterations (with
      "(auto)"), palette, AA, engine label; `[beyond float!]` appears with
      the GPU engine when scale < 1e-3.
- [ ] Busy cursor is AppStarting during async renders and is restored only by
      the latest render; panels return to the crosshair.
- [ ] Log/diagnostics window shows engine, the GPU engine section (device in
      use, last error, device list, diagnostics), GPU selection, saved settings,
      and the 200-line event log; copy button works.
- [ ] CLI: `--diag-gpu [device]`, `--bench-gpu [device] [--csv f]`,
      `--bench-cpu [float] [--csv f]` behave as in §6.6 without opening the UI.

**Engines** (where hardware allows):

- [ ] CPU: 4 code paths exist (scalar/vector × float/double); double vector
      output is bit-identical to scalar double; 32-bit setting renders in
      float with no automatic fallback; rows are parallelized with the
      §3.1 chunking.
- [ ] CPU and GPU renders of the same zone are near-identical (differences
      limited to rare chaotic-boundary pixels; 32-bit may differ more at deep
      zoom — both expected).
- [ ] GPU: `GpuViewParams` is exactly 88 bytes on both sides; the 4 programs
      (float/double render + float/double bench) exist; init selects by
      device memory (Auto) or exact name; tuned block is one of
      32x4/32x8/32x16/32x32 with the 1.5% margin rule; device LUT uploaded
      once per (palette, maxIter); render and benchmark share one gate;
      unavailability falls back to CPU with a readable status reason.
- [ ] GPU real-time: the §4.7 presentation path exists (2-buffer present
      surface, 16 ms timer renders only when dirty, resize stretch-then-full);
      software/virtual renderers excluded from the device list; device memory
      read without 32-bit overflow; capture and preview go through
      CPU-readable staging copies; offscreen benchmark uses event queries
      with in-flight 4..256, ≤ 0.8 s queued, 60 s frame timeout, and readable
      hang/device-lost errors.
- [ ] GPU combo lists the device names from `AdapterNames()` (sorted,
      distinct) with `Auto` first, remembers the choice, and re-initializes on
      change with the §5.10 fallbacks.

**Portability and freedom of implementation**:

- [ ] This spec names **no** operating system, UI toolkit, or GPU
      technology: every such choice (target OS, UI toolkit, GPU API,
      packaging) is made by the implementer, exactly as §1.6 states.
- [ ] The replica builds and runs on the implementer's chosen target OS
      with the §5 UI layout, controls, tooltips, menus, and (platform-
      mapped) shortcuts.
- [ ] The GPU engine is implemented with whatever GPU API the chosen
      platform offers and exposes exactly the §4.1 interface; the
      application code never references a specific GPU technology.
- [ ] Settings, zones, favorites, and the event log live in the target OS's
      standard per-application user-data directory with the same JSON
      schemas (§7.1-7.2).
- [ ] The CPU engine, math, palettes, LUT, tiling, history, and all dialogs
      behave identically regardless of the chosen OS, toolkit, or GPU API
      (engine-independent features are technology-agnostic).
- [ ] Keyboard shortcuts use the reference bindings of §5, mapped to the
      target platform's standard equivalents (primary modifier, window-close
      shortcut, back/forward navigation) as §1.6 states.

**Fidelity spot values** (grep your implementation for these exact
constants): `9.36`, `-0.5`, `-0.7`, `0.27015`, `0.35`, `4096`, `50`,
`50000`, `256`, `2000`, `1.5`, `0.0625`, `0.25`, `1e-3`, `88`, `32x8`,
`0.015`, `960`, `540`, `10915`, `1.0453474311811176e-04`, `8` (s),
`512` (tile), `320`, `16384`, `120`, `30`, `60`, `480`, `24`, `0.7`,
`1.43`, `5` (px), `80` (ms), `16` (ms), `200` (log lines), `256`, `4`,
`0.8`, `60` (s timeout), `0.5` (s tick), `1.15` (chart scale).
