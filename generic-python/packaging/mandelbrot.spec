# -*- mode: python ; coding: utf-8 -*-
# Ricetta PyInstaller per l'app Mandelbrot self-contained (one-dir), multipiattaforma:
#   - macOS   -> dist/Mandelbrot.app             (GPU Metal/pyobjc + Vulkan/wgpu, firma ad-hoc)
#   - Windows -> dist/Mandelbrot/Mandelbrot.exe  (CPU sempre; GPU NVIDIA se l'utente
#           installa driver + runtime CUDA; GPU AMD/NVIDIA/Intel via Vulkan/wgpu)
# Dipendenze comuni di mandel.py: numpy, Pillow(ImageTk), tkinter; fast path CPU
# numba+llvmlite (l'app degrada su numpy se assente). I backend GPU sono rilevati
# a runtime (CUDA su NVIDIA, Metal su Apple Silicon, Vulkan/wgpu cross-platform)
# e sono gia' guardati nel sorgente.
# Il runtime CUDA NON e' bundled: cupy e' incluso e trova le DLL CUDA (toolkit o pip
# nvidia/*) installate dall'utente via cuda-pathfinder (CUDA_PATH / PATH / Program Files).
# wgpu (wgpu-native) E' bundled (lib nativa dentro la wheel): la GPU Vulkan funziona
# out-of-the-box senza runtime aggiuntivi, su AMD/NVIDIA/Intel.

import os
import re
import sys

from PyInstaller.utils.hooks import collect_all, collect_submodules

HERE = os.path.dirname(os.path.abspath(SPEC))

# Versione unica di verita': letta dall'header di mandel.py (# VERSIONE: X.Y.Z).
with open(os.path.join(HERE, "mandel.py"), encoding="utf-8") as _f:
    _head = _f.read(4096)
VERSION = re.search(r"VERSIONE:\s*([\d.]+)", _head).group(1)

IS_DARWIN = (sys.platform == "darwin")
IS_WIN32 = (sys.platform == "win32")

datas = []
binaries = []
hiddenimports = []

# --- Numba + LLVM (dylib/dll di llvmlite ~116 MB, dati JIT) ---
for mod in ("numba", "llvmlite"):
    _d, _b, _h = collect_all(mod)
    datas += _d
    binaries += _b
    hiddenimports += _h

# --- Pillow + Tkinter (ImageTk importa _tkinter_finder a runtime) ---
hiddenimports += ["PIL._tkinter_finder"]

runtime_hooks = []
icon_exe = None
exe_version = None

if IS_DARWIN:
    # --- pyobjc / Metal (GPU Apple): moduli C (objc/_objc, Foundation/_nsobject,
    #     Metal/_metal) + submoduli dinamici ---
    for mod in ("objc", "Foundation", "Metal"):
        try:
            _d, _b, _h = collect_all(mod)
            datas += _d
            binaries += _b
            hiddenimports += _h
        except Exception:
            hiddenimports += [mod]
    hiddenimports += collect_submodules("objc")
    hiddenimports += ["Metal", "objc", "Foundation"]

    # --- wgpu / wgpu-native (GPU Vulkan cross-platform, lib nativa nella wheel) ---
    for mod in ("wgpu", "cffi"):
        try:
            _d, _b, _h = collect_all(mod)
            datas += _d
            binaries += _b
            hiddenimports += _h
        except Exception:
            hiddenimports += [mod]
    hiddenimports += ["wgpu"]

    # --- Numba OpenMP: omppool.so richiede @rpath/libomp.dylib (il suo rpath e'
    #     hardcoded a una dir CI inesistente). Unica copia disponibile:
    #     torch/lib/libomp.dylib. Nel bundle con basename 'libomp.dylib' ->
    #     PyInstaller fa match sul basename e riscrive il riferimento. ---
    try:
        import torch
        _libomp = os.path.join(os.path.dirname(torch.__file__), "lib", "libomp.dylib")
        if os.path.exists(_libomp):
            binaries.append((_libomp, "libomp.dylib"))
    except Exception:
        pass

    excludes = ["cupy", "torch", "matplotlib", "IPython", "pytest"]

elif IS_WIN32:
    # --- CUDA / CuPy (GPU NVIDIA) ---
    _d, _b, _h = collect_all("cupy")
    datas += _d
    binaries += _b
    hiddenimports += _h

    # --- GPU NVIDIA: cupy e' bundled (solo i moduli .py/.pyd, NO runtime CUDA).
    #     Le DLL del runtime CUDA (cublas/nvrtc/cudart/...) NON sono incluse nel
    #     pacchetto: se l'utente vuole la GPU installa lui driver NVIDIA + runtime
    #     CUDA (toolkit o pacchetti pip nvidia/*); cupy (via cuda-pathfinder) le
    #     trova da solo tramite CUDA_PATH / PATH / Program Files. Senza CUDA,
    #     l'app degrada automaticamente sulla CPU (Numba/numpy) - guardato in mandel.py. ---

    # --- wgpu / wgpu-native (GPU Vulkan cross-platform, AMD/NVIDIA/Intel).
    #     La lib nativa e' dentro la wheel -> bundled e funziona out-of-the-box,
    #     senza runtime esterni. cffi (dipendenza di wgpu) incluso per le sue DLL. ---
    for mod in ("wgpu", "cffi"):
        try:
            _d, _b, _h = collect_all(mod)
            datas += _d
            binaries += _b
            hiddenimports += _h
        except Exception:
            hiddenimports += [mod]
    hiddenimports += ["wgpu", "wgpu.backends.wgpu_native"]

    # --- Runtime hook: cartella EXE + _internal sul percorso DLL (difensivo) ---
    runtime_hooks = [os.path.join(HERE, "hook_dlldir.py")]

    # --- Icona EXE (mandelbrot.ico generata da make_icon.py) ---
    icon_exe = "mandelbrot.ico"

    # --- Versione EXE (proprietà file) ---
    try:
        from PyInstaller.utils.win32 import versioninfo
        _parts = tuple(int(x) for x in VERSION.split("."))
        _v = (_parts[0], _parts[1], _parts[2], 0)
        exe_version = versioninfo.VersionInfo(
            fixedinfo=versioninfo.FixedFileInfo(
                filevers=_v, prodvers=_v, mask=0x3f, flags=0x0,
                OS=0x40004, fileType=0x1, subtype=0x0, date=0),
            varinfo=[versioninfo.StringTable(
                "040904B0",
                strings=[versioninfo.StringStruct("FileDescription", "Mandelbrot"),
                         versioninfo.StringStruct("ProductName", "Mandelbrot"),
                         versioninfo.StringStruct("FileVersion", VERSION),
                         versioninfo.StringStruct("ProductVersion", VERSION)])])
    except Exception:
        exe_version = None

    excludes = ["objc", "Foundation", "Metal", "pyobjc", "torch",
                "matplotlib", "IPython", "pytest"]

else:
    # Altre piattaforme: solo CPU (numba/numpy) + PIL/tkinter, GPU esclusa.
    excludes = ["objc", "Foundation", "Metal", "pyobjc", "cupy", "torch",
                "matplotlib", "IPython", "pytest"]

block_cipher = None

a = Analysis(
    ["mandel.py"],
    pathex=[],
    binaries=binaries,
    datas=datas,
    hiddenimports=hiddenimports,
    hookspath=[],
    runtime_hooks=runtime_hooks,
    excludes=excludes,
    win_no_prefer_redirects=False,
    win_private_assemblies=False,
    cipher=block_cipher,
    noarchive=False,
)

pyz = PYZ(a.pure, a.zipped_data, cipher=block_cipher)

exe = EXE(
    pyz,
    a.scripts,
    [],
    exclude_binaries=True,
    name="Mandelbrot",
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=False,
    console=False,
    icon=icon_exe,
    version=exe_version,
)

if IS_DARWIN:
    app = BUNDLE(
        exe, a.binaries, a.datas,
        name="Mandelbrot.app",
        icon="mandelbrot.icns",
        bundle_id="it.mandelbrot.app",
        info_plist={
            "CFBundleName": "Mandelbrot",
            "CFBundleDisplayName": "Mandelbrot",
            "CFBundleShortVersionString": VERSION,
            "CFBundleVersion": VERSION,
            "NSHighResolutionCapable": True,
            "NSPrincipalClass": "NSApplication",
            "LSMinimumSystemVersion": "11.0",
        },
    )
else:
    coll = COLLECT(
        exe, a.binaries, a.datas,
        strip=False,
        upx=False,
        name="Mandelbrot",
    )
