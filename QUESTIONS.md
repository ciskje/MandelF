# QUESTIONS — MandelC#

Generic questions and answers that do not concern the program source.
Moved out of `TODO.md` so the todo stays strictly about the program.
Program-related analysis (AA dithering, ILGPU vs DirectX, Radeon bench,
zone constants) stays in `TODO.md` with its verdicts in `SPECS.md`.

- **What SMs and warps are** — answered. SM (Streaming Multiprocessor) is the
  GPU's compute unit (the 5070 Ti has 70, each with 128 CUDA cores, scheduler,
  registers and L1); a warp is the indivisible crew of 32 threads that always
  executes the same instruction in lock-step. Slowest thread of each warp sets
  the warp cost (divergence); each SM hosts a limited number of resident warps,
  so a frame runs in successive waves.

- **How the benchmark splits across the 5070 Ti compute units** — answered.
  One frame is 960x540 = 518,400 independent samples (one thread / pixel-shader
  invocation each), i.e. 16,200 warps of 32 over 70 SMs (~5 waves per SM).
  Tensor cores, RT cores and ROPs stay idle; FP64 units stay idle in 32-bit
  mode, which is why the 64-bit kernel measures ~41x less. The test is pure
  ALU and compute-bound; memory traffic is negligible.

- **Theoretical performance comparison RTX 4070 SUPER vs RTX 5070 Ti** —
  answered (tensor, memory, FP32/FP64). Note only; the full text was removed
  from `SPECS.md` in v2.5.23.

- **ffmpeg 7.1 installation in %USERPROFILE%\ffmpeg + user PATH** —
  done (via imageio-ffmpeg/PyPI, gyan.dev was at 100 KB/s). Environment setup
  for the zoom-video MP4 export, no source change.

- **Safe updates audit (SDK/runtime/NuGet)** — done, all already at latest
  stable, no changes.

- **Explain how to launch the app** — answered (published exe in `published/`
  or `dotnet run`; see README).

- **Clarify if AGENTS.md is read on empty session** — answered.

- **Generic questions in .md files not about the program source** — answered;
  this move itself (generic entries collected here).

- **What PTX is** — answered. PTX (Parallel Thread Execution) is NVIDIA's
  virtual GPU assembly: an intermediate, forward-compatible instruction set
  between CUDA C++ (`.cu`) and the real per-architecture machine code (SASS).
  The chain is `.cu` → PTX (via `nvcc` or NVRTC, sharing the same backend)
  → SASS (via the driver's JIT compiler at module load, cached on disk).
  One PTX file runs on every GPU because the driver translates it for the
  actual chip (sm_89, sm_120, ...); SASS would need one binary per chip.
  Think of it as the GPU equivalent of .NET IL or Java bytecode. This app
  embeds one `compute_75` PTX (`Cuda/mandelbrot.ptx`, built from
  `Cuda/mandelbrot.cu`) and loads it with `cuModuleLoadDataEx`; it is
  compiled with `--fmad=false` so the rational arithmetic matches the CPU
  bit for bit.

- **What --fmad=false means** — answered. FMA (fused multiply-add) is one
  GPU instruction computing `a*b+c` with a single rounding instead of two
  (round after mul, round after add). With fusion on (`nvcc` default, and our
  first PTX), the compiler silently merges separate `*` and `+` operations
  of the escape loop (e.g. `2*zx*zy+ccy`) into FMAs: faster (one instruction
  instead of two) and in isolation more accurate, but bit-different from the
  CPU, where the .NET JIT evaluates mul and add separately. `--fmad=false`
  forbids that contraction, so every GPU operation rounds exactly like its
  CPU counterpart and the render comes out pixel-identical (verified: 0 diffs
  on all scenes). Measured cost on the double-precision kernel: ~13%
  throughput (6.7 → 5.8 MPixel/s on the 5070 Ti); the float kernel still
  gained from the explicit block tuning.   Deliberate trade: identity with the
  CPU over raw speed, same guarantee as the CPU SIMD path (v2.19.4).
  Measured per-precision cost on the 5070 Ti (same PTX, flag on/off):
  32-bit 295 → 291.3 MPixel/s (−1.3%), 64-bit 6.7 → 5.8 (−13%).
  So the flag does affect 32-bit, but barely; the double loop pays most.
  Decision v2.22.2: FMA enabled on both precisions (user choice, speed over
  bit-identity). Measured vs-CPU pixel difference with FMA on: ≤0.7% of
  pixels on all scenes except 32-bit deep zoom, where chaos amplification
  reaches ~12% sparse flips; official history bars remeasured accordingly
  (5070 Ti 293.7/6.7, 4070 SUPER 246.2/5.3).

- **Where the blocks/warps split is in the program** — answered: nowhere, it
  is implicit. Our code only decides the thread count (one per sample); the
  blocks/warps slicing is done by the ILGPU runtime (CUDA) and by the
  D3D11 driver + rasterizer (DirectX). CUDA: `LoadAutoGroupedStreamKernel`
  in `GpuMandelbrot.cs` ("auto grouped" = ILGPU picks the block size),
  launched with `count = W*H` threads, one `Index1D` per sample. DirectX:
  `Draw(3, 0)` of the fullscreen triangle in `DxMandelbrot.cs` generates one
  pixel-shader invocation per pixel; the hardware groups them into
  32-thread warps. The only parallelism knobs we control are the frame-level
  queue depths (CUDA adaptive batch, DirectX in-flight ring), not the
  intra-frame blocks/warps.

- **Why CUDA benchmarks slower than DirectX despite full optimization control** —
  answered. The gap is small, not a missing optimization: on the history bars
  (best of 3, Release) CUDA 32-bit scores 301.7 vs DirectX 322.4 MPixel/s on
  the 5070 Ti (~7%) and 247.1 vs 262.4 on the 4070 SUPER (~6%);
  `BenchmarkForm.cs` notes both kernels run the identical incremental-squares
  loop, so it is a fair engine-to-engine comparison. ILGPU was already ruled
  out as the cause: v2.22.0 replaced it with native PTX (`Cuda/mandelbrot.cu`
  via `nvcc`, driven through `CudaNative.cs`) and throughput was a wash
  (+4% float / −13% double from the `--fmad=false` identity trade, later
  recovered by re-enabling FMA in v2.22.2). Remaining structural causes, all
  visible in the sources: (1) per-thread index math — every CUDA bench thread
  pays an integer `idx % W` / `idx / W` division (`mandelbrot.cu`,
  `FloatBenchKernel`/`DoubleBenchKernel`), while the HLSL bench shader gets
  its coordinates free from the rasterizer (`SV_Position` in `BenchPS`,
  `DxMandelbrot.cs`); (2) submit path — DirectX sets pipeline state once and
  then issues only `Draw + End(query)` per frame with a deep (up to 256)
  in-flight ring (`RunBenchmarkFramesOffscreen`), while CUDA pays a
  managed-to-native `cuLaunchKernel` per launch plus param packing and
  `Synchronize` per batch (`GpuMandelbrot.cs`, `BenchmarkGpuCore`); (3) the
  graphics pipeline (fullscreen triangle, quad grouping, ROP render-target
  write) is the most game-tuned path in the driver, vs a 1D compute dispatch
  with global-memory writes. Practical consequence: DirectX wins interactive
  float rendering, but it is float-only (`SPECS.md`), so deep zoom has no
  DirectX equivalent and needs CUDA double (6.7 MPixel/s on the 5070 Ti).
  Follow-up (v2.22.5): the 2D-grid bench layout closed part of it
  (5070 Ti 301.7 → 310.7, 4070 SUPER 247.1 → 250.1); Nsight Systems then
  showed kernel time == wall time, ptxas no spills, and identical SM clocks
  under both engines, so the remainder is backend codegen/divergence
  handling inside NVIDIA's own compilers, not host overhead. Rejected by A/B:
  4x unroll (−3.5%), half-pixel sampling (−1.2%), HLSL-style loop (+0.5%),
  `--use_fast_math` (−0.4%).
  Render follow-up (v2.22.6): end-to-end float rendering needs no catch-up —
  CUDA is at or above DirectX on every measured scene (fullset, AA4, deep,
  Julia, 1080p, 1080p AA4); render kernels moved to the same 2D grid,
  bit-identical. Double render stays FP64-hardware-bound on GeForce
  (deep 960x540: CUDA ~79 ms vs CPU ~57 ms).

- **What would change in SPEC_FOR_LLM_PLATFORM_AGNOSTIC.md if the replica
  also targeted Linux?** — answered. Nothing in the core contracts: §2
  (math), §3 (CPU) and §4 (GPU engine as API-agnostic interface) are
  technology-free by design, so the same contract applies on Linux
  (implement the GPU side with the host API, e.g. Vulkan or NVIDIA CUDA).
  The document would need these small edits: (1) §1.1/intro and the §8
  checklist: "Windows and macOS" → add Linux; (2) §1.2 tech stack: add the
  Linux .NET 8 desktop UI option and `linux-x64` packaging (§1.4 too);
  (3) §7.1 heading and the §5.9/§7.2/checklist references:
  `%APPDATA%\MandelbrotViewer\` is Windows-specific — restate as "the
  per-app user-data directory" (`~/.config/MandelbrotViewer` on Linux/XDG,
  `~/Library/Application Support/MandelbrotViewer` on macOS); (4) add a note
  that `System.Drawing.Common` (LockBits/`Format32bppArgb`) is Windows-only
  on .NET 8, so the "Bitmap" in the contracts must be read as an abstract
  32-bit ARGB pixel buffer the UI toolkit draws — the largest code-level
  consequence; (5) optionally note that the §4.7 "swapchain or equivalent"
  maps to X11/Wayland presentation (or frame copy into the toolkit) on
  Linux. No math, constants, or GPU contract changes.

- **[pi-web-access] "Dynamic tool activation requires Pi 0.86.1 or newer"** —
  answered. Cosmetic warning from the `pi-web-access` extension (v0.31.0),
  not a real version problem: Pi here is v0.87.1, which already supports
  dynamic tools (`getAllTools`/`setActiveTools` present in the bundle).
  Root cause is the extension's `supportsDynamicTools()` check in
  `tool-activation.ts`: after verifying the API functions it re-reads the
  Pi version via `import.meta.resolve("@earendil-works/pi-coding-agent")`,
  but that package is not installed under the extensions sandbox
  (`~/.pi/agent/npm/node_modules/@earendil-works` is empty — Pi injects the
  API instead of installing it), so the resolve throws, the `catch`
  returns `false`, and the warning is printed even on new Pi versions.
  Harmless: as the message says, web tools "remain eagerly available",
  i.e. search/fetch/source-check still work, just loaded eagerly instead
  of on-demand via `web_enable`. Upstream fix belongs in `pi-web-access`
  (version check should not depend on `import.meta.resolve`, or should
  treat "API functions present" as sufficient). Workaround if the noise
  bothers: `pi update` when a fixed release exists, or ignore it.
