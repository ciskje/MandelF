"""Query memoria + guardia ricalcolo NxN."""
import os
import platform
import ctypes
from . import state as S
from .config import PHOTO_HEADROOM, PHOTO_HOST_RESERVE, PHOTO_BACKSTOP_MPX

def _host_free_bytes():
    """RAM libera (byte), senza nuove dipendenze: GlobalMemoryStatusEx su
    Windows, sysconf AVPHYS_PAGES altrove. 0 se non determinabile."""
    try:
        import platform
        if platform.system() == "Windows":
            import ctypes
            class _MS(ctypes.Structure):
                _fields_ = [("dwLength", ctypes.c_ulong),
                            ("dwMemoryLoad", ctypes.c_ulong),
                            ("ullTotalPhys", ctypes.c_ulonglong),
                            ("ullAvailPhys", ctypes.c_ulonglong),
                            ("ullTotalPageFile", ctypes.c_ulonglong),
                            ("ullAvailPageFile", ctypes.c_ulonglong),
                            ("ullTotalVirtual", ctypes.c_ulonglong),
                            ("ullAvailVirtual", ctypes.c_ulonglong),
                            ("ullAvailExtendedVirtual", ctypes.c_ulonglong)]
            st = _MS()
            st.dwLength = ctypes.sizeof(_MS)
            if ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(st)):
                return int(st.ullAvailPhys)
        else:
            import os
            return int(os.sysconf("SC_PAGE_SIZE")) * int(os.sysconf("SC_AVPHYS_PAGES"))
    except Exception:
        pass
    return 0
def _photo_mem_ok(n, w, h):
    from .cuda import (_cuda_split_devs as _csd,  # lazy: evita ciclo import
                        _cuda_free_bytes as _cfb)
    """(ok, msg): il ricalcolo NxN ci sta nella memoria disponibile?
    Stima per pixel del frame grande P=(n*w)*(n*h): CPU = workspace
    (~50 B/px f32, ~90 f64) + RGB; CUDA = out device P*3 + pinned/host;
    Metal (unified) = tutto in RAM condivisa; Vulkan = output u32 P*4
    (heap VRAM non interrogabile via wgpu -> vale il check RAM host)."""
    P = (n * w) * (n * h)
    if S._ACTIVE == "cpu":
        need_h = P * ((50 if S._PREC == "f32" else 90) + 6)
        need_d = 0
    elif S._ACTIVE == "cuda":
        if S._CUDA_SPLIT_ON and _csd() is not None:
            # v6.2: fabbisogno per banda su ciascuna VRAM (quota rapporto).
            r = min(0.9, max(0.1, S._CUDA_SPLIT_RATIO))
            for _shr, _dev in ((r, _csd()[0]),
                               (1.0 - r, _csd()[1])):
                _nd = P * _shr * 3
                _free_d, _t = _cfb(_dev)
                if _free_d > 0:
                    if _nd * PHOTO_HEADROOM > _free_d:
                        return False, (f"ricalcolo {n}x{n} rifiutato: servono "
                                       f"~{_nd / 2**30:.1f} GiB di VRAM su GPU "
                                       f"{_dev} (liberi {_free_d / 2**30:.1f})")
                elif P / 1e6 > PHOTO_BACKSTOP_MPX:
                    return False, (f"ricalcolo {n}x{n} rifiutato: VRAM non "
                                   f"determinabile e {P / 1e6:.1f} Mpx oltre la "
                                   f"rete di sicurezza")
            need_h, need_d = P * 8, 0  # host una volta sola, device gia' visti
        else:
            need_h, need_d = P * 8, P * 3
    elif S._ACTIVE == "metal":
        need_h, need_d = P * 8, 0
    else:  # vulkan
        need_h, need_d = P * 10, P * 4
    free_h = _host_free_bytes()
    if free_h > 0:
        if need_h * PHOTO_HEADROOM > free_h - PHOTO_HOST_RESERVE:
            return False, (f"ricalcolo {n}x{n} rifiutato: servono "
                           f"~{need_h / 2**30:.1f} GiB di RAM "
                           f"(liberi {free_h / 2**30:.1f})")
    elif P / 1e6 > PHOTO_BACKSTOP_MPX:
        return False, (f"ricalcolo {n}x{n} rifiutato: RAM non determinabile "
                       f"e {P / 1e6:.1f} Mpx oltre la rete di sicurezza")
    if S._ACTIVE == "cuda" and need_d > 0:  # split: device gia' visti sopra
        free_d, _total_d = _cfb()
        if free_d > 0:
            if need_d * PHOTO_HEADROOM > free_d:
                return False, (f"ricalcolo {n}x{n} rifiutato: servono "
                               f"~{need_d / 2**30:.1f} GiB di VRAM "
                               f"(liberi {free_d / 2**30:.1f})")
        elif P / 1e6 > PHOTO_BACKSTOP_MPX:
            return False, (f"ricalcolo {n}x{n} rifiutato: VRAM non "
                           f"determinabile e {P / 1e6:.1f} Mpx oltre la rete "
                           f"di sicurezza")
    return True, ""

