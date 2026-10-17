# SPECS (user-visible behavior) — stato replica

Mappa file SPEC §1.3 → questa replica (Avalonia):

| SPEC | Replica |
|---|---|
| Program.cs | `MandelbrotViewer/Program.cs` (CLI + Avvio Avalonia) |
| AppVersion.cs | idem |
| RenderEngine.cs | idem |
| AppLog.cs | idem (ring 200) |
| Settings.cs | idem (`~/Library/Application Support/MandelbrotViewer/`) |
| Palette.cs | idem (7×5 stop, LUT 4096) |
| Mandelbrot.cs | idem (math + 4 path scalar/vector×float/double + bench) + `RenderInto` headless bit-identico |
| GpuEngine.cs | stub contratto §4.1 (Metal pendente) |
| GpuViewParams | `GpuViewParams.cs` (88 byte assert) |
| GpuKernels.cs | contratto + nomi 4 programmi (MSL pendente) |
| MandelbrotForm(.Designer).cs | `Views/MainWindow.axaml(.cs)` |
| BenchmarkForm(.Designer).cs | `Views/BenchmarkForm.cs` (chart custom, CSV) |
| BenchmarkStandard/Progress/Csv.cs | `BenchmarkStandard.cs` (Standard+Progress), `BenchmarkCsv.cs` |
| ExportForm(.Designer).cs | `Views/ExportForm.cs` (tile 512) |
| ZoomVideoForm(.Designer).cs | `Views/ZoomVideoForm.cs` (ffmpeg o PNG seq) |
| Diagnostics.cs | idem (CpuName sysctl, diag log, CLI) |
| LogForm.cs | `Views/LogForm.cs` (+ AboutForm) |

Comportamento: zoom 2x/0.7/1.43, drag threshold 5px, preview half-size throttle
80ms, history back≤200, auto-iter per formula, status §5.4, back-to-set 120f@30fps,
export 320…16384, video ease-out + auto-iter/frame, CSV `timestamp,engine,device,
precision,run,frames,seconds,mpixel_s`.

Note di porting (stesso intento, convenzione Avalonia):
- Mnemonici menu: SPEC usa `&` (WinForms), Avalonia usa `_` (`_File`, `_Generate`…).
- GPU: backend OpenGL 4.1 (GLSL 410, float32) via Silk.NET + GLFW hidden window.
  Double esposto come non disponibile (§4 lo consente); radio 64 disabilitata con GPU.
- Presentazione GPU: offscreen + blit su timer 16ms dirty-flag (no swapchain);
  resize: preview subito + full 300ms dopo l'ultimo SizeChanged (debounce).
- Main-thread GLFW: init/probe/dispose solo su main thread (check pthread_main_np);
  render/bench su worker con MakeContextCurrent sotto gate singola.
- Init: `Image` Avalonia con Source null sta a 0x0 e blocca il render →
  placeholder 1x1 iniziale (test regressione ViewHasSize).
- Diagnostica frame: `MV_DUMP=1` scrive /tmp/mv-frame.png + /tmp/mv-frame.txt.

Misure M1 (8s, 960x540 AA1x, iter 10915): CPU double 0.3, CPU float 0.6,
GPU float ~234 Mpixel/s. CPU-float vs GPU-float a zoom normale: 0.08% pixel
differenti (solo bordi caotici, max 1 LSB + rari flip iterazione da FMA).

Proof-of-work (`--bench-check [W H [SCALE [MAXITER]]]`): rilegge i conteggi
grezzi dal kernel bench e li confronta col loop float CPU sulla stessa griglia:
zona fair (scale 0.5): 99.57% esatti, media identica (2865/2865), max-diff solo
su pixel caotici (FMA, documentato). A zoom profondo oltre il limite float
(scale<1e-3): 76% — coordinate float marginali, atteso (§2.6).
Il benchmark misura samples/s (media ~2000-3000 iter/campione in zona bench):
234 Mpx/s ≈ throughput di picco M1 su workload puramente aritmetico e coerente;
il rapporto ~390x col nostro CPU deriva dal loop vettoriale che paga maxIter
per lane divergenti + SIMD M1 a 2 lane double.
