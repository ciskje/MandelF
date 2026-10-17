"""Dispatch backend, precisione, compute() e warmup."""
import threading
from . import state as S
from .config import BACKEND_FG, CX0, CY0, HALF0
from .cuda import _compute_gpu_cuda, _cuda_short_name
from .metal import _compute_gpu_metal
from .vulkan import _compute_gpu_vulkan
from .cpu import compute_cpu, _numba_warmup

if S._CUDA_OK:
    S._BACKENDS_OK.append("cuda")
if S._METAL_OK:
    S._BACKENDS_OK.append("metal")
if S._VULKAN_OK:
    S._BACKENDS_OK.append("vulkan")
S._BACKENDS_OK.append("cpu")

def _backend_ok(name):
    return name in S._BACKENDS_OK

def _default_backend():
    return S._BACKENDS_OK[0]

S._ACTIVE = _default_backend()
S._GPU = S._ACTIVE != "cpu"

def _backend_fg():
    return BACKEND_FG.get(S._ACTIVE, "#1f6feb")

def _gpu_supports_f64():
    return S._ACTIVE == "cuda" and S._KERNEL_F64 is not None

def set_prec(p):
    if p == "f64" and S._ACTIVE != "cpu" and not _gpu_supports_f64():
        return False
    if p in ("f32", "f64"):
        S._PREC = p
    return True

def compute_gpu(cx, cy, half, w, h, mi, buf=None, prec=None):
    if S._ACTIVE == "metal":
        return _compute_gpu_metal(cx, cy, half, w, h, mi, prec=prec)
    if S._ACTIVE == "vulkan":
        return _compute_gpu_vulkan(cx, cy, half, w, h, mi, prec=prec)
    return _compute_gpu_cuda(cx, cy, half, w, h, mi, buf=buf, prec=prec)
def backend():
    if S._ACTIVE != "cpu":
        return S._ACTIVE.upper() + " " + S._PREC
    if S._NUMBA_OK.get(S._PREC):
        return "CPU " + S._PREC + " multi-core"
    return "CPU " + S._PREC + " single-core (numpy)"
def hw_name():
    """Nome dell'hardware attivo (GPU o CPU), in cache per backend (v5.8.0).
    Rilevamento senza dipendenze nuove: CPU dal registro Windows (winreg) o
    'sysctl' su macOS; GPU da CuPy (CUDA), pyobjc (Metal) o wgpu (Vulkan,
    gia' rilevato in VulkanBackend.name). In caso di errore -> generico."""
    if S._ACTIVE in S._HW_CACHE:
        return S._HW_CACHE[S._ACTIVE]
    is_cpu = (S._ACTIVE == "cpu")
    name = "CPU" if is_cpu else "GPU"
    try:
        import platform
        if is_cpu:
            s = platform.system()
            if s == "Windows":
                import winreg
                k = winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE,
                                   r"HARDWARE\DESCRIPTION\System\CentralProcessor\0")
                try:
                    name = winreg.QueryValueEx(k, "ProcessorNameString")[0].strip()
                finally:
                    winreg.CloseKey(k)
            elif s == "Darwin":
                import subprocess
                name = subprocess.check_output(
                    ["sysctl", "-n", "machdep.cpu.brand_string"],
                    text=True).strip()
            else:
                name = platform.processor() or name
        elif S._ACTIVE == "cuda":
            import cupy as cp
            # v5.9.0: device selezionato (dropdown GPU); con piu' GPU
            # l'ordine CUDA puo' differire da quello nvidia-smi.
            # v6.2: in split i nomi di entrambe le GPU (anche durante il bench,
            # v6.2.3: il bench ora usa lo split quando attivo).
            if S._CUDA_SPLIT_ON and len(S._CUDA_DEVICES) >= 2:
                _dev = S._CUDA_DEVICES[S._CUDA_DEV][0] if S._CUDA_DEVICES else 0
                name = "%s+%s" % (_cuda_short_name(S._CUDA_DEVICES[0][1]),
                                  _cuda_short_name(S._CUDA_DEVICES[1][1]))
                S._HW_CACHE[S._ACTIVE] = name
                return name
            _dev = S._CUDA_DEVICES[S._CUDA_DEV][0] if S._CUDA_DEVICES else 0
            name = cp.cuda.runtime.getDeviceProperties(_dev)["name"]
            if isinstance(name, bytes):
                name = name.decode("utf-8", "replace")
        elif S._ACTIVE == "metal":
            _n = S._METAL_BE.dev.name
            name = str(_n() if callable(_n) else _n)
        elif S._ACTIVE == "vulkan":
            name = str(S._VULKAN_BE.name)
    except Exception:
        pass
    if not name:
        name = "CPU" if is_cpu else "GPU"
    S._HW_CACHE[S._ACTIVE] = name
    return name
def compute(cx, cy, half, w, h, mi, buf=None, prec=None, my_gen=0):
    if S._ACTIVE != "cpu":
        return compute_gpu(cx, cy, half, w, h, mi, buf=buf, prec=prec)
    # v5.3.0: la precisione selezionata (f32/f64) vale anche per la CPU.
    return compute_cpu(cx, cy, half, w, h, mi, my_gen=my_gen, prec=prec or S._PREC)
def _gpu_warmup():
    try:
        compute_gpu(CX0, CY0, HALF0, 64, 64, 64, prec="f32")
        if _gpu_supports_f64():
            compute_gpu(CX0, CY0, HALF0, 64, 64, 64, prec="f64")
    except Exception:
        pass

def _warmup_active_gpu():
    if S._ACTIVE in ("cuda", "vulkan"):
        try:
            compute_gpu(CX0, CY0, HALF0, 64, 64, 64, prec="f32")
        except Exception:
            pass
if S._GPU:
    threading.Thread(target=_gpu_warmup, daemon=True).start()
threading.Thread(target=_numba_warmup, daemon=True).start()

