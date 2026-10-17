# AGENTS.md — windows-python (thin env over generic-python)

Windows side of the shared Python viewer. No code duplication: everything
runs from `..\generic-python\source\` (package `mandelbrot`, entry `mandel.py`).

## Run (dev, Windows)

```powershell
py ..\generic-python\source\mandel.py
```

Deps: tkinter (stdlib), numpy, numba, Pillow; CUDA via CuPy (NVIDIA driver +
CUDA runtime found by cuda-pathfinder); Vulkan via wgpu; Metal n/a;
fallback CPU. See `..\generic-python\source\mandelbrot\engine.py`.

## Versioning

`7.3.2` (lockstep, see root AGENTS.md). Single source:
`..\generic-python\source\mandelbrot\__init__.py` (`VERSION`) +
`..\generic-python\source\mandel.py` header (must match).

## Publish

Via `..\generic-python\packaging\build_app.py` (PyInstaller one-dir,
win64 zip). Copy the deliverable to `dist\` as
`MandelF-python-X.Y.Z-win64.zip`; keep the last 2. Tag
`windows-python-vX.Y.Z` (this env) on release.
