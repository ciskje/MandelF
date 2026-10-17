"""Stato mutabile condiviso (ex-globali di mandel.py). Fonte unica."""
import threading
import numpy as np
from .palette import PALETTES, make_lut

_PALETTE = "fuoco"
_LUT = make_lut(PALETTES["fuoco"])
_PAL_IDX = [np.asarray(i, dtype=np.int32) for i in range(len(PALETTES))]
_PAL_INDEX_CACHE = 0

def _refresh_pal_index():
    global _PAL_INDEX_CACHE
    try:
        _PAL_INDEX_CACHE = list(PALETTES).index(_PALETTE)
    except ValueError:
        _PAL_INDEX_CACHE = 0

_refresh_pal_index()

def apply_palette(name):
    """Seleziona la palette; aggiorna LUT + cache indice (hot-path)."""
    global _PALETTE, _LUT
    if name not in PALETTES:
        name = "fuoco"
    _PALETTE = name
    _LUT = make_lut(PALETTES[name])
    _refresh_pal_index()

_CUDA_OK = False
_KERNEL_F32 = None
_KERNEL_F64 = None
_BUF = None
_CUDA_DEVICES = []
_CUDA_DEV = 0
_BENCH_ACTIVE = False
_CUDA_SPLIT_ON = False
_CUDA_SPLIT_RATIO = 0.5
_CUDA_SPLIT_MIN_H = 32
_CUDA_SPLIT_BUFS = {}
_CUDA_SPLIT_CALIBRATING = False
_CUDA_SPLIT_SWAP = False
_KERN_DEV = {}
_KERN_DEV_LOCK = threading.Lock()
_CUDA_SPLIT_PARITY_OK = None
_CUDA_SPLIT_PARITY_DIFF = 0.0
_CUDA_SPLIT_PARITY_INFO = ""
_METAL_OK = False
_METAL_BE = None
_VULKAN_OK = False
_VULKAN_BE = None
_VULKAN_ADAPTERS = []
_VULKAN_DEV = 0
_BACKENDS_OK = []
_ACTIVE = "cpu"
_GPU = False
_PREC = "f32"
_PINNED = {}
_NUMBA_OK = {"f64": False, "f32": False}
_NUMBA_STATUS = {"import": "ok", "f64": "warmup in corso...", "f32": "warmup in corso...", "tempo": None}
_GEN = np.zeros(1, dtype=np.int32)
_NUMBA_AVAILABLE = False
_CPU_WS = {}
_HW_CACHE = {}
