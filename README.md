# Mandelbrot Viewer (MandelC)

> This project is an LLM test: `SPECS.md` was written by humans as a complete,
> self-contained specification, so it can be used to recreate the application
> from scratch; the test itself is how faithfully another LLM rebuilds it.

Interactive Mandelbrot set viewer built with C# / WinForms (.NET 8, Windows only).

![Screenshot](screen1.png?raw=true)

![Screenshot](screen2.jpg)

## Features

- **Three rendering engines** with automatic fallback:
  - **CPU** — always available, parallel SIMD with AVX512 wide path on Zen5 (double bit-identical; float fast path on wide Mandelbrot views)
  - **CUDA** (NVIDIA) — float 32-bit or double 64-bit, via native PTX kernels
  - **DirectX 11** — realtime ~60 fps pixel shader, via Vortice

- **Smooth coloring** — consistent across all engines, 7 palettes (Fire, Ice, Thermal, Ocean, Violet, Desert, Forest), gamma curve `t^0.35` (float fast paths may differ on rare boundary pixels)

- **Zoom & pan** — mouse wheel, click-to-zoom, drag or arrow keys; half-resolution preview during interaction, full resolution on release

- **Anti-aliasing** — 1x / 2x / 4x / 8x supersampling with RGB averaging

- **Auto iterations** — `2000 * (1 + log10(1.5/half))`, clamped 50–50000

- **Julia mode** — `z = z² + c` with fixed c (click to set, persisted)

- **History & favorites** — back/forward navigation (200 views), named saved zones

- **Export** — high-resolution PNG (up to 8K+), zoom-out MP4 video (ffmpeg H.264)

- **RealTime zoom-out** — animated return to the full set (120 frames @ 30 fps, AA 1x)

- **Benchmark** — standardized 960×540 AA1x test, 8 s budget, MPixel/s chart with per-card history; CLI: `--bench-cpu [float]`, `--bench-cuda`, `--bench-dx`

- **Multi-GPU** — dropdown to select the adapter (CUDA + DirectX unified), CUDA precision selector (32/64-bit)

- **Settings persistence** — iterations, palette, AA, engine, GPU, window size/position

## Requirements

- Windows 10/11 (x64)
- .NET 8 Desktop Runtime (or use the self-contained publish)
- Optional: NVIDIA GPU + driver for CUDA

## Download

Download the latest self-contained Windows executable (v2.24.1:
`MandelbrotViewer.exe`, ~155 MB, no .NET installation required) from the
[GitHub Releases page](https://github.com/ciskje/MandelC/releases/latest).
Alternatively use the Windows
installer (`MandelbrotViewer-Setup-2.24.1.exe`)
from the [v2.24.1 release](https://github.com/ciskje/MandelC/releases/tag/v2.24.1):
it installs to Program Files with Start-menu shortcuts and an uninstaller.

## Build & Run

```powershell
# Build
dotnet build MandelbrotViewer\MandelbrotViewer.csproj

# Run (debug)
dotnet run --project MandelbrotViewer

# Publish + installer (self-contained single file, then ISCC)
.\setup.bat   # -> MandelbrotViewer-Setup-<version>.exe
```

## Keyboard Shortcuts

| Key | Action |
|-----|--------|
| `R` | Reset view |
| `S` | Save PNG |
| `+` / `-` | Iterations ±50 |
| `Ctrl+O` / `Ctrl+S` | Load / Save zone |
| `Ctrl+J` | Toggle Julia mode |
| `Ctrl+D` | Save favorite |
| `Ctrl+B` | Benchmark |
| `Ctrl+Shift+R` | Back to set (RealTime) |
| `Ctrl+Shift+E` | Export PNG |
| `Ctrl+Shift+V` | Export zoom video |
| `Alt+←` / `Alt+→` | History back / forward |
| `Esc` | Stop animation |

## License

MIT — see [LICENSE](LICENSE).
