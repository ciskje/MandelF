# AGENTS.md — macos-python (thin env over generic-python)

Mac side of the shared Python viewer. No code duplication: everything runs
from `../generic-python/source/` (package `mandelbrot`, entry `mandel.py`).

## Run (dev, macOS)

```sh
python3 ../generic-python/source/mandel.py
```

Deps: tkinter (stdlib), numpy, numba, Pillow; Metal via pyobjc;
optional CUDA (NVIDIA + CuPy) / Vulkan (wgpu) when present, else CPU.
`platform.system()` picks backend paths; see `../generic-python/source/mandelbrot/engine.py`.

## Versioning

`7.3.0` (lockstep, see root AGENTS.md). Single source:
`../generic-python/source/mandelbrot/__init__.py` (`VERSION`) +
`../generic-python/source/mandel.py` header (must match).

## Publish

Via `../generic-python/packaging/build_app.py` (PyInstaller one-dir,
macOS .dmg with /Applications symlink). Copy the deliverable to `dist/`
as `MandelF-python-X.Y.Z-arm64.dmg`; keep the last 2. Tag
`macos-python-vX.Y.Z` (this env) on release.
