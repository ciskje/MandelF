"""Backend CUDA (CuPy RawKernel) + split multi-GPU + parity."""
import threading
import time
import ctypes
import numpy as np
from PIL import Image
from . import state as S
from .config import BENCH, auto_mi
from .palette import PALETTES, make_lut

def _cuda_free_bytes(dev=None):
    """(free, total) VRAM sul device dato (default: selezionato);
    (0, 0) se illeggibile."""
    try:
        import cupy as cp
        if dev is None:
            dev = S._CUDA_DEVICES[S._CUDA_DEV][0] if S._CUDA_DEVICES else 0
        with cp.cuda.Device(dev):
            free, total = cp.cuda.runtime.memGetInfo()
            return int(free), int(total)
    except Exception:
        return 0, 0
def _fmt_lut(lut):
    return ", ".join(str(int(v)) for v in lut.ravel())
_KERNEL_TPL = r'''
@@CONSTS@@

__device__ __forceinline__ void pal_lut(@@T@@ t, const unsigned char* lut, unsigned char* rgb) {
    int idx = (int)(@@FMIN@@(1.0@@S@@, @@FMAX@@(0.0@@S@@, t)) * 255.0@@S@@);
    rgb[0] = lut[idx * 3 + 0];
    rgb[1] = lut[idx * 3 + 1];
    rgb[2] = lut[idx * 3 + 2];
}

__device__ __forceinline__ void process_pixel(
    int col, int row, int out_row, int w, int bh, int h,
    @@T@@ cx, @@T@@ cy, @@T@@ half, int mi,
    const unsigned char* lut,
    unsigned char* __restrict__ out)
{
    if (col >= w || out_row >= bh) return;
    @@T@@ x0 = cx + half * ((@@T@@)(2 * col - w) / (@@T@@)w);
    @@T@@ y0 = cy + half * ((@@T@@)h / (@@T@@)w) * ((@@T@@)(2 * row - h) / (@@T@@)h);
    // Interior analitico: bulbo periodica-2 + cardioide principale
    // (c interno alla cardioide principale <=> |1 - sqrt(1 - 4c)| < 1,
    // riscritto senza complessi come R < 2*sqrt(0.5*(R + A)), con
    // A = 1 - 4*Re(c), R = |1 - 4c|). Se interno, il punto non diverge mai
    // -> pixel nero senza eseguire le mi iterazioni.
    // Prefiltro bounding-box dell'insieme: evita il costo della sqrt sui
    // pixel chiaramente esterni (fuggono in 1-2 iterazioni).
    if (x0 >= -2.0@@S@@ && x0 <= 0.4@@S@@ && y0 >= -1.3@@S@@ && y0 <= 1.3@@S@@) {
        unsigned char* p = out + (size_t)(out_row * w + col) * 3;
        @@T@@ d2 = (x0 + 1.0@@S@@) * (x0 + 1.0@@S@@) + y0 * y0;
        if (d2 <= 0.0625@@S@@) { p[0] = 0; p[1] = 0; p[2] = 0; return; }
        @@T@@ A = 1.0@@S@@ - 4.0@@S@@ * x0;
        @@T@@ B = -4.0@@S@@ * y0;
        @@T@@ R = @@SQRT@@(A * A + B * B);
        if (R < 2.0@@S@@ * @@SQRT@@(0.5@@S@@ * (R + A))) { p[0] = 0; p[1] = 0; p[2] = 0; return; }
    }
    @@T@@ a = cx * cx + (x0 - cx);
    @@T@@ two_cx = 2.0@@S@@ * cx;
    @@T@@ wr = -cx, wi = 0.0@@S@@;
    int it = 0;
    bool esc = false;
    @@T@@ mag2 = 0.0@@S@@;
    for (int i = 0; i < mi; ++i) {
        if (esc) break;
        @@T@@ nr = wr * wr - wi * wi + two_cx * wr + a;
        @@T@@ ni = two_cx * wi + 2.0@@S@@ * wr * wi + y0;
        wr = nr; wi = ni;
        @@T@@ zr = wr + cx;
        mag2 = zr * zr + wi * wi;
        if (mag2 > 4.0@@S@@) { esc = true; it = i; }
    }
    unsigned char* p = out + (size_t)(out_row * w + col) * 3;
    if (!esc) { p[0] = 0; p[1] = 0; p[2] = 0; return; }
    @@T@@ nu = (@@T@@)it + 1.0@@S@@ - @@LOG2@@(0.5@@S@@ * @@LOG@@(mag2));
    @@T@@ t = @@POW@@(@@FMIN@@(1.0@@S@@, @@FMAX@@(0.0@@S@@, nu / (@@T@@)mi)), 0.35@@S@@);
    unsigned char rgb[3];
    pal_lut(t, lut, rgb);
    p[0] = rgb[0]; p[1] = rgb[1]; p[2] = rgb[2];
}

extern "C" __global__ void __launch_bounds__(256) @@KNAME@@(
    unsigned char* __restrict__ out,
    int pal,
    @@T@@ cx, @@T@@ cy, @@T@@ half,
    int w, int bh, int h, int mi,
    int row0)
{
    @@LUTSELECT@@
    int tx = blockIdx.x * blockDim.x + threadIdx.x;
    int ty = blockIdx.y * blockDim.y + threadIdx.y;
    // v5.0.0 (Fase 3): 1 px/thread (era 2): micro-benchmark A/B in-process
    // su 8 varianti (px 1/2/4 x block 16x16/32x8/8x32/32x16/8x16/16x8)
    // -> 1px 16x16 vincente su entrambe le zone (z1 1.8x, z3 1.1x); tutte le
    // varianti bit-identiche tra loro (ogni pixel e' calcolato in modo
    // indipendente, la config non cambia il risultato).
    // v6.2: row0 = prima riga della banda (split multi-GPU): la griglia e'
    // dimensionata sulla banda (bh), la riga assoluta e' row0+ty per il
    // calcolo, l'output usa out_row=ty (buffer locale alla banda).
    // v6.2.3: bh per la guardia (buffer locale, non h assoluto).
    int col0 = tx;
    process_pixel(col0, row0 + ty, ty, w, bh, h, cx, cy, half, mi, lut, out);
}
'''
_PRECS = {
    "f32": dict(T="float",  S="f", FMIN="fminf", FMAX="fmaxf",
                LOG2="log2f", LOG="logf", POW="powf", SQRT="sqrtf", KNAME="mandel_kernel_f32"),
    "f64": dict(T="double", S="",  FMIN="fmin",  FMAX="fmax",
                LOG2="log2",  LOG="log",  POW="pow",  SQRT="sqrt",  KNAME="mandel_kernel_f64"),
}
def _build_kernel(prec):
    d = _PRECS[prec]
    # Dichiarazioni __constant__ generate dal registro (una per palette).
    consts = "\n".join(
        "__constant__ unsigned char LUT_%s[768] = { %s };"
        % (name.upper(), _fmt_lut(make_lut(pal)))
        for name, pal in PALETTES.items())
    # Selezione LUT per indice pal (ordine PALETTES = indice).
    names = list(PALETTES)
    select = "    const unsigned char* lut = LUT_%s;" % names[0].upper()
    for i, name in enumerate(names[1:], start=1):
        select += "\n    if (pal == %d) lut = LUT_%s;" % (i, name.upper())
    src = (_KERNEL_TPL
           .replace("@@CONSTS@@", consts)
           .replace("@@LUTSELECT@@", select))
    for k, v in d.items():
        src = src.replace("@@" + k + "@@", v)
    return src, d["KNAME"]
S._CUDA_OK = False
S._KERNEL_F32 = None
S._KERNEL_F64 = None
S._BUF = None
try:
    import cupy as cp
    if cp.cuda.is_available():
        _src, _name = _build_kernel("f32")
        S._KERNEL_F32 = cp.RawKernel(_src, _name, options=("--use_fast_math",))
        S._CUDA_OK = True
        try:
            _src, _name = _build_kernel("f64")
            S._KERNEL_F64 = cp.RawKernel(_src, _name, options=("--use_fast_math",))
        except Exception:
            S._KERNEL_F64 = None
except Exception:
    S._CUDA_OK = False

# v5.9.0: GPU CUDA multiple. S._CUDA_DEVICES = [(id, nome)] rilevate all'avvio;
# S._CUDA_DEV = indice selezionato (default 0). Il dropdown "GPU:" in toolbar
# compare solo se > 1. Il device CuPy e' PER-THREAD: i percorsi di render,
# benchmark e warmup entrano in 'with cp.cuda.Device(S._CUDA_DEV)' (il .use()
# qui sotto vale solo per il thread chiamante).
S._CUDA_DEVICES = []
S._CUDA_DEV = 0
if S._CUDA_OK:
    try:
        for _i in range(cp.cuda.runtime.getDeviceCount()):
            _nm = cp.cuda.runtime.getDeviceProperties(_i)["name"]
            if isinstance(_nm, bytes):
                _nm = _nm.decode("utf-8", "replace")
            S._CUDA_DEVICES.append((_i, str(_nm).strip()))
    except Exception:
        S._CUDA_DEVICES = []
def _cuda_short_name(name):
    # "NVIDIA GeForce RTX 5070 Ti" -> "GeForce RTX 5070 Ti" (dropdown compatto)
    if name.startswith("NVIDIA "):
        return name[len("NVIDIA "):]
    return name

def _cuda_label(i):
    _id, _nm = S._CUDA_DEVICES[i]
    return "%d: %s" % (_id, _cuda_short_name(_nm))

def set_cuda_device(i):
    """Seleziona la GPU CUDA (indice in S._CUDA_DEVICES); True se ok.
    Invalida S._BUF (realloc sul nuovo device) e la cache hw_name."""
    if not S._CUDA_DEVICES:
        return False
    try:
        i = int(i)
    except (TypeError, ValueError):
        return False
    S._CUDA_DEV = max(0, min(i, len(S._CUDA_DEVICES) - 1))
    S._BUF = None
    S._HW_CACHE.pop("cuda", None)
    # v6.2: la selezione singola esce dallo split (dropdown gpu1/gpu2/both).
    S._CUDA_SPLIT_ON = False
    try:
        cp.cuda.Device(S._CUDA_DEVICES[S._CUDA_DEV][0]).use()
    except Exception:
        pass
    return True

# v6.2: split CUDA su 2 GPU (bande orizzontali, rapporto variabile).
# Coppia = prime 2 CUDA; quota righe del primo = S._CUDA_SPLIT_RATIO.
# S._BENCH_ACTIVE segnala che il benchmark e' in corso (v6.2.3: il bench usa
# lo split quando attivo, come il render interattivo).
S._BENCH_ACTIVE = False
S._CUDA_SPLIT_ON = False
S._CUDA_SPLIT_RATIO = 0.5
S._CUDA_SPLIT_MIN_H = 32  # sotto: single (soglia tecnica, non policy)
S._CUDA_SPLIT_BUFS = {}  # {dev_id: array} buffer device per banda (mai condivisi)
S._CUDA_SPLIT_CALIBRATING = False
S._CUDA_SPLIT_SWAP = False  # v6.2.3: bande invertite (solo parity diagnostica)
def _cuda_split_devs():
    """(id0, id1) della coppia split, o None se < 2 device."""
    if len(S._CUDA_DEVICES) >= 2:
        return (S._CUDA_DEVICES[0][0], S._CUDA_DEVICES[1][0])
    return None

def _cuda_split_ready(h):
    return (S._CUDA_SPLIT_ON and _cuda_split_devs() is not None
            and h >= S._CUDA_SPLIT_MIN_H)

def set_cuda_split(on):
    """Attiva/disattiva lo split (dropdown 'Entrambe'); True se ok."""
    if on and _cuda_split_devs() is None:
        return False
    S._CUDA_SPLIT_ON = bool(on)
    S._HW_CACHE.pop("cuda", None)
    return True

# v6.2.3: handle kernel SEPARATI per device (niente RawKernel condiviso tra
# i thread delle bande: la banda 2 riceveva row0=0, cioe' gli scalari del
# lancio della banda 1). Compilati lazy una volta per (dev, prec) sotto lock;
# i lanci restano paralleli.
S._KERN_DEV = {}
S._KERN_DEV_LOCK = threading.Lock()

def _kern_for(dev, use64):
    """Handle kernel DEDICATO per (device, prec), compilato fresh una volta
    sotto lock (mai condiviso tra device/thread)."""
    key = (dev, use64)
    k = S._KERN_DEV.get(key)
    if k is not None:
        return k
    with S._KERN_DEV_LOCK:
        k = S._KERN_DEV.get(key)
        if k is None:
            src, name = _build_kernel("f64" if use64 else "f32")
            k = cp.RawKernel(src, name, options=("--use_fast_math",))
            S._KERN_DEV[key] = k
    return k

def _cuda_launch_band(dev, out, row0, bh, w, h, mi, pal, use64, fdt,
                      cx, cy, half):
    """Un launch sulla banda [row0, row0+bh) del device dato (single e split
    passano di qui: unico code-path di lancio)."""
    with cp.cuda.Device(dev):
        bx, by = 16, 16
        grid = ((w + bx - 1) // bx, (bh + by - 1) // by)
        kernel = _kern_for(dev, use64)
        args = (out,
                S._PAL_IDX[pal],
                np.asarray(cx, dtype=fdt),
                np.asarray(cy, dtype=fdt),
                np.asarray(half, dtype=fdt),
                np.asarray(w, dtype=np.int32),
                np.asarray(bh, dtype=np.int32),
                np.asarray(h, dtype=np.int32),
                np.asarray(mi, dtype=np.int32),
                np.asarray(row0, dtype=np.int32))
        kernel(grid, (bx, by), args)
def _cuda_probe(dev, cx, cy, half, w, h, mi):
    """Render single-device scartato su dev (warmup / calibrazione timing)."""
    pal = S._PAL_INDEX_CACHE
    use64 = (S._PREC == "f64") and (S._KERNEL_F64 is not None)
    fdt = np.float64 if use64 else np.float32
    with cp.cuda.Device(dev):
        out = cp.empty((w * h * 3,), dtype=cp.uint8)
        _cuda_launch_band(dev, out, 0, h, w, h, mi, pal, use64, fdt,
                          cx, cy, half)
        cp.cuda.runtime.deviceSynchronize()
def _cuda_calibrate_split():
    """Fissa S._CUDA_SPLIT_RATIO misurando le due GPU (vista bench, probe
    480x270, 3 run dopo burst di warmup per svegliare il clock). Background,
    una sola istanza alla volta; in errore resta il rapporto precedente."""
    devs = _cuda_split_devs()
    if devs is None or S._CUDA_SPLIT_CALIBRATING:
        return
    S._CUDA_SPLIT_CALIBRATING = True
    try:
        b = BENCH
        mi = auto_mi(b["half"])
        w, h = 480, 270
        # v6.2.3: pre-compila gli handle dedicati (le probe devono misurare,
        # non compilare).
        for _dev in devs:
            try:
                _kern_for(_dev, False)
                if S._KERNEL_F64 is not None:
                    _kern_for(_dev, True)
            except Exception:
                pass
        ts = []
        for dev in devs:
            for _ in range(3):
                _cuda_probe(dev, b["cx"], b["cy"], b["half"], w, h, mi)
            t0 = time.perf_counter()
            for _ in range(3):
                _cuda_probe(dev, b["cx"], b["cy"], b["half"], w, h, mi)
            ts.append(max((time.perf_counter() - t0) / 3, 1e-9))
        r = (1.0 / ts[0]) / (1.0 / ts[0] + 1.0 / ts[1])
        S._CUDA_SPLIT_RATIO = min(0.9, max(0.1, r))
        # v6.2.1: subito dopo, parity split-vs-single (fail-safe automatico).
        _cuda_split_parity()
    except Exception:
        pass
    finally:
        S._CUDA_SPLIT_CALIBRATING = False
def _compute_gpu_cuda_split_arr(cx, cy, half, w, h, mi, prec=None):
    """Nucleo dello split: ritorna la vista ndarray (h,w,3) sul pinned host
    (il chiamante la copia se la trattiene)."""
    devs = _cuda_split_devs()
    r = min(0.9, max(0.1, S._CUDA_SPLIT_RATIO))
    h0 = (int(round(h * r)) // 16) * 16
    h0 = min(max(h0, 16), h - 16)
    bands = [(devs[0], 0, h0), (devs[1], h0, h - h0)]
    if S._CUDA_SPLIT_SWAP:
        # v6.2.3: solo per la parity invertita (diagnostica).
        bands = [(devs[1], 0, h0), (devs[0], h0, h - h0)]
    pal = S._PAL_INDEX_CACHE
    p = prec if prec in ("f32", "f64") else S._PREC
    use64 = (p == "f64") and (S._KERNEL_F64 is not None)
    fdt = np.float64 if use64 else np.float32
    bufs = []
    for (dev, _r0, _bh) in bands:
        need = w * _bh * 3
        b = S._CUDA_SPLIT_BUFS.get(dev)
        if b is None or b.size < need:
            with cp.cuda.Device(dev):
                b = cp.empty((need,), dtype=cp.uint8)
            S._CUDA_SPLIT_BUFS[dev] = b
        bufs.append(b[:need])
    pm, host = _pinned_view(w, h)
    errs = []

    def run_band(i):
        try:
            dev, r0, bh = bands[i]
            _cuda_launch_band(dev, bufs[i], r0, bh, w, h, mi, pal, use64,
                              fdt, cx, cy, half)
            with cp.cuda.Device(dev):
                cp.cuda.runtime.memcpy(pm.ptr + r0 * w * 3,
                                       bufs[i].data.ptr, w * bh * 3,
                                       cp.cuda.runtime.memcpyDeviceToHost)
        except Exception as ex:
            errs.append(ex)

    ts = [threading.Thread(target=run_band, args=(i,), daemon=True)
          for i in (0, 1)]
    for t in ts:
        t.start()
    for t in ts:
        t.join()
    if errs:
        raise errs[0]
    return np.asarray(host.reshape((h, w, 3)))
def _compute_gpu_cuda_split(cx, cy, half, w, h, mi, prec=None):
    """Frame spartito sulle 2 GPU (bande orizzontali per rapporto calibrato),
    cucito in un unico host array pinned. Errore su una banda = eccezione
    (il chiamante ricade sul single)."""
    return Image.fromarray(
        _compute_gpu_cuda_split_arr(cx, cy, half, w, h, mi, prec=prec))
def _cuda_render_array(dev, cx, cy, half, w, h, mi):
    """Render single-device su dev, ritorna ndarray uint8 (h,w,3) fresco."""
    pal = S._PAL_INDEX_CACHE
    use64 = (S._PREC == "f64") and (S._KERNEL_F64 is not None)
    fdt = np.float64 if use64 else np.float32
    with cp.cuda.Device(dev):
        out = cp.empty((w * h * 3,), dtype=cp.uint8)
        _cuda_launch_band(dev, out, 0, h, w, h, mi, pal, use64, fdt,
                          cx, cy, half)
        return out.get().reshape((h, w, 3)).copy()

# v6.2.1: esito parity split-vs-single (None = non ancora misurata).
S._CUDA_SPLIT_PARITY_OK = None
S._CUDA_SPLIT_PARITY_DIFF = 0.0
S._CUDA_SPLIT_PARITY_INFO = ""
def _cuda_split_diag():
    """Riga diagnostica per Help > Informazioni (rapporto + parity)."""
    try:
        r = min(0.9, max(0.1, S._CUDA_SPLIT_RATIO))
        n0 = _cuda_short_name(S._CUDA_DEVICES[0][1])
        n1 = _cuda_short_name(S._CUDA_DEVICES[1][1])
        base = "%s %d%% / %s %d%%" % (n0, round(r * 100),
                                      n1, 100 - round(r * 100))
    except Exception:
        return "non disponibile"
    if S._CUDA_SPLIT_PARITY_OK is None:
        if S._CUDA_SPLIT_CALIBRATING:
            return base + " (parity in corso...)"
        return base + " (parity mai eseguita: premi \u21bb per calibrare)"
    if S._CUDA_SPLIT_PARITY_OK:
        return base + " (parity OK: split bit-identico al single)"
    return base + (" (parity FALLITA: diff %.1f%%%s, split auto-disattivato)"
                   % (S._CUDA_SPLIT_PARITY_DIFF * 100.0, S._CUDA_SPLIT_PARITY_INFO))

def _pixdiff(x, y):
    """Frazione di pixel con almeno un canale diverso (stessa shape)."""
    return float((x != y).any(axis=-1).mean())
def _cuda_split_parity():
    """Split e single sulla stessa vista devono essere bit-identici (ogni
    pixel e' indipendente). Test normale + invertito. In caso di diff lo
    split si auto-disattiva (fail-safe). Background."""
    devs = _cuda_split_devs()
    if devs is None:
        return
    try:
        b = BENCH
        mi = auto_mi(b["half"])
        w, h = 480, 270
        r = min(0.9, max(0.1, S._CUDA_SPLIT_RATIO))
        h0 = (int(round(h * r)) // 16) * 16
        h0 = min(max(h0, 16), h - 16)
        c = _cuda_render_array(devs[0], b["cx"], b["cy"], b["half"],
                               w, h, mi)
        # --- test 1: ordine normale ---
        S._CUDA_SPLIT_SWAP = False
        a = _compute_gpu_cuda_split_arr(b["cx"], b["cy"], b["half"],
                                        w, h, mi).copy()
        if a.shape != c.shape:
            raise ValueError("shape %s vs %s" % (a.shape, c.shape))
        diff = _pixdiff(a, c)
        if diff == 0.0:
            S._CUDA_SPLIT_PARITY_OK = True
            S._CUDA_SPLIT_PARITY_DIFF = 0.0
            S._CUDA_SPLIT_PARITY_INFO = ""
            return
        d0 = _pixdiff(a[:h0], c[:h0])
        d1 = _pixdiff(a[h0:], c[h0:])
        d1_top = _pixdiff(a[h0:], c[:h - h0])
        # --- test 2: ordine invertito ---
        S._CUDA_SPLIT_SWAP = True
        a2 = _compute_gpu_cuda_split_arr(b["cx"], b["cy"], b["half"],
                                         w, h, mi).copy()
        S._CUDA_SPLIT_SWAP = False
        diff2 = _pixdiff(a2, c)
        if d1_top == 0.0 and d1 > 0.0:
            pat = ("norm: banda2=copia alte (row0 ignorato); "
                   "invert: diff %.1f%%" % (diff2 * 100.0))
        else:
            pat = ("norm: d0=%.1f%% d1=%.1f%%; invert: diff %.1f%%"
                   % (d0 * 100.0, d1 * 100.0, diff2 * 100.0))
        S._CUDA_SPLIT_PARITY_DIFF = diff
        S._CUDA_SPLIT_PARITY_INFO = pat
        S._CUDA_SPLIT_PARITY_OK = False
        S._CUDA_SPLIT_ON = False
        S._HW_CACHE.pop("cuda", None)
    except Exception:
        pass

# ---------------- GPU (Metal, Apple Silicon) ----------------
# Backend Metal per Apple GPU (M1...). Metal su Apple Silicon NON supporta
# 'double' -> il backend Metal e' f32-ONLY (f64 resta solo sulla CPU, Numba).
# Stesso criterio di escape, interior analitico e coloring (smooth iteration)
# del kernel CUDA e del percorso CPU (v5.2.0): le tre vie producono lo stesso
# colore, a parte 1-qualche ULP di FMA/contrazione sul bordo caotico (limitazione
# intrinseca di f32, NON un difetto: Metal f32 e' all'altezza di CUDA f32 e CPU f32).
# Vincoli pyobjc (scoperti a caro prezzo):
#   - gli argomenti scalari vanno in uno struct passato via buffer [[buffer(1)]]
#     (ne' scalari nudi ne' [[buffer]] su singoli float sono accettati);
#   - MSL usa fmin/fmax/pow/sqrt/log2/log (senza suffisso 'f') e 'half' e'
#     riservata (quindi il campo si chiama 'hs');
#   - i puntatori richiedono l'address space esplicito (device/constant);
#   - H2D/D2H: buf.contents().as_buffer(N) restituisce una memoryview scrivibile
#     (C-level, ~0.01 ms) -> e' il ponte rapido (la varlist NON e' bytes-like);
# v4.16.0: buffer host pinned per il D2H (DMA, ~1,7x piu' veloce di .get()
# pageable su frame grandi). Cache per dimensione (max 3 slot).
# NB CuPy 14: MemoryHost rimosso -> cp.cuda.PinnedMemory (+ ptr).
S._PINNED = {}

def _pinned_view(w, h):
    """(PinnedMemory, vista numpy uint8 h*w*3) su memoria host pinned."""
    key = (w, h)
    ent = S._PINNED.get(key)
    if ent is None:
        size = w * h * 3
        pm = cp.cuda.PinnedMemory(size)
        host = np.frombuffer((ctypes.c_ubyte * size).from_address(pm.ptr),
                             dtype=np.uint8)
        ent = (pm, host)
        if len(S._PINNED) >= 3:
            S._PINNED.pop(next(iter(S._PINNED)))
        S._PINNED[key] = ent
    return ent
def _compute_gpu_cuda(cx, cy, half, w, h, mi, buf=None, prec=None):
    # v5.9.0: tutto sul device selezionato (il current-device CuPy e'
    # per-thread: worker, benchmark e warmup girano su thread diversi).
    # v6.2: buf dedicato (benchmark) = sempre single; con buf=None e split
    # attivo si spartisce sulle 2 GPU (fallback single in errore).
    if buf is None and _cuda_split_ready(h):
        try:
            return _compute_gpu_cuda_split(cx, cy, half, w, h, mi, prec=prec)
        except Exception:
            pass
    _dev = S._CUDA_DEVICES[S._CUDA_DEV][0] if S._CUDA_DEVICES else 0
    with cp.cuda.Device(_dev):
        need = w * h * 3
        if buf is None:
            if S._BUF is None or S._BUF.size < need:
                S._BUF = cp.empty((need,), dtype=cp.uint8)
            buf = S._BUF
        out = buf[:need]
        pal = S._PAL_INDEX_CACHE
        p = prec if prec in ("f32", "f64") else S._PREC
        use64 = (p == "f64") and (S._KERNEL_F64 is not None)
        fdt = np.float64 if use64 else np.float32
        _cuda_launch_band(_dev, out, 0, h, w, h, mi, pal, use64, fdt,
                          cx, cy, half)
        # v4.16.0: D2H pinned (DMA, memcpyDeviceToHost) con fallback .get().
        try:
            pm, host = _pinned_view(w, h)
            cp.cuda.runtime.memcpy(pm.ptr, out.data.ptr, w * h * 3,
                                   cp.cuda.runtime.memcpyDeviceToHost)
            return Image.fromarray(host.reshape((h, w, 3)))
        except Exception:
            return Image.fromarray(out.get().reshape((h, w, 3)))

