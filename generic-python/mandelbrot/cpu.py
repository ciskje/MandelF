"""Backend CPU (Numba prange + fallback numpy bit-identico)."""
import math
import time
import threading
import numpy as np
from PIL import Image
from . import state as S
from .config import HALF0

# ---------------- CPU (fallback) ----------------

# v4.16.0: array di lavoro CPU cacheati per (w,h) (nessuna allocazione per
# render: offset di griglia + real/imag/c/w4/z/diverged/it riutilizzati).
# v5.0.0: escape loop Numba (opzionale, con fallback numpy bit-identico).
# Il kernel fa SOLO il loop di escape (stessa semantica del loop numpy:
# it = indice 0-based dell'iterazione in cui |z|^2 > 4, altrimenti 0);
# geometria, prefiltro interior e coloring restano in numpy (invariati).
# Cancellazione cooperativa (my_gen = generazione del job; 0 = nessuna
# cancellazione, es. benchmark/CLI): le righe sono divise in 16 bande e il
# contatore S._GEN[0] e' controllato a livello PYTHON tra le bande (affidabile;
# un check in-kernel NON lo e': Numba/LLVM fa hoisting dei letture di
# memoria cross-thread dentro prange, verificato sperimentalmente).
# Se il job diventa obsoleto, le bande rimanenti vengono saltate e il frame
# e' comunque scartato dal worker (doppia garanzia).
# v5.3.0: flag di ok PER PRECISIONE (f32 e f64 si auto-specializzano in kernel
# Numba distinti; se il self-test di bit-identita' NON passa per una
# precisione, il fallback numpy resta attivo solo per quella).
S._NUMBA_OK = {"f64": False, "f32": False}
# v5.9.5: diagnostica del fallback single-core (visibile in Help >
# Informazioni e come avviso in status bar). "import": "ok" o motivo del
# fallimento; per precisione: "warmup in corso..." | "ok (multi-core)" |
# motivo ("self-test..." / "compilazione fallita: ..."); "tempo": secondi
# del warmup (None se non concluso).
S._NUMBA_STATUS = {"import": "ok", "f64": "warmup in corso...",
                 "f32": "warmup in corso...", "tempo": None}
S._GEN = np.zeros(1, dtype=np.int32)
try:
    from numba import njit, prange

    # NB: NO cache=True: il cache di numba e' legato al nome del modulo e
    # mandel.py e' eseguito/importato con nomi diversi (__main__ vs mandel vs
    # loader dinamico) -> il load della cache falliva. La compilazione (~1-2 s)
    # e' pagata dal thread di warmup all'avvio, prima del primo render CPU.
    @njit(parallel=True)
    def _mandel_escape(c, diverged, it, mag, mi, row0, row1):
        # Solo il loop di fuga sulle righe [row0, row1). La cancellazione
        # cooperativa e' gestita dal chiamante (compute_cpu) a livello
        # PYTHON, tra bande di righe: un check in-kernel di un contatore
        # scritto da un altro thread NON e' affidabile con Numba/LLVM
        # (verificato sperimentalmente: il load di gen_arr[0] viene fatto
        # una sola volta / hoistato fuori dal loop prange, quindi il bump
        # cross-thread non e' visto e le righe vengono comunque elaborate).
        w = c.shape[1]
        for row in prange(row0, row1):
            for col in range(w):
                if diverged[row, col]:
                    continue
                vre = c[row, col].real
                vim = c[row, col].imag
                z_re = 0.0
                z_im = 0.0
                for i in range(mi):
                    # z = z^2 + c (stesso ordine di operazioni del numpy)
                    z_re, z_im = z_re * z_re - z_im * z_im, 2.0 * z_re * z_im
                    z_re += vre
                    z_im += vim
                    if z_re * z_re + z_im * z_im > 4.0:
                        it[row, col] = i
                        mag[row, col] = z_re * z_re + z_im * z_im
                        break
except Exception as ex:
    njit = None
    prange = None
    # v5.9.5: motivo registrato (prima muto) per la diagnostica in UI.
    S._NUMBA_STATUS["import"] = "numba non importabile: " + str(ex)[:160]
    S._NUMBA_STATUS["f64"] = S._NUMBA_STATUS["f32"] = "numba non disponibile"

# Numba assente (import fallito) -> il motore CPU gira sul fallback numpy
# single-core; lo segnalo nell'etichetta del backend (vedi backend()).
S._NUMBA_AVAILABLE = (njit is not None)
def _numba_selftest(cdt):
    """Self-test del kernel Numba contro il loop numpy di riferimento (4x4 con
    casi limite: interiori, fuga rapida/lenta, pre-diverged, overflow), STESSA
    formula. Criterio per precisione:
      f64: it[] e mag[] BIT-IDENTICI (Numba f64 e numpy f64 usano la stessa
           aritmetica a 3 arrotondamenti — verificato).
      f32: it[] (decisioni di fuga) ESATTAMENTE uguali; mag[] entro una
           toller stretta. In f32 Numba/LLVM applica contrazioni FMA e
           riassociazioni che DIPENDONO DAL CONTESTO del loop (verificato:
           z_re^2-z_im^2 resta a 3 arrotondamenti, mentre 2*z_re*z_im+ci e
           z_re^2+z_im^2 vengono contratti in FMA) -> la bit-identita' con
           numpy non e' riproducibile in modo affidabile. La differenza e' di
           1-qualche ULP su una piccolissima frazione di pixel di bordo
           caotico (irrelevante: al piu' 1 entry di LUT nel coloring).
    """
    fdt = np.float32 if cdt is np.complex64 else np.float64
    c = np.array([
        -0.5 + 0.5j, 0.7 + 0.1j, -1.7 + 0j, 0.3 + 0.6j,
        -0.1 - 0.9j, 2.5j, 0.0, 1.0,
        -0.749 - 0.001j, 0.1 - 0.7j, -1.2 - 0.5j, 0.9 + 0.9j,
        -0.28 + 0.01j, -2.0j, 0.5 - 1.9j, -0.75 + 0.12j,
    ], dtype=cdt).reshape(4, 4)
    d = np.zeros((4, 4), dtype=bool)
    d[0, 0] = True  # pre-diverged (come il prefiltro interior)
    it_numba = np.zeros((4, 4), dtype=np.int32)
    mag_numba = np.zeros((4, 4), dtype=fdt)
    _mandel_escape(c, d, it_numba, mag_numba, 64, 0, 4)
    # riferimento numpy (stessa formula del fallback in compute_cpu)
    zr = np.zeros((4, 4), dtype=fdt)
    zi = np.zeros((4, 4), dtype=fdt)
    cr = c.real
    ci = c.imag
    div = d.copy()
    it_ref = np.zeros((4, 4), dtype=np.int32)
    mag_ref = np.zeros((4, 4), dtype=fdt)
    with np.errstate(over="ignore", invalid="ignore"):
        for i in range(64):
            if not (~div).any():
                break
            zr2 = zr * zr - zi * zi
            zi2 = 2.0 * zr * zi
            zr = zr2 + cr
            zi = zi2 + ci
            m = ((zr * zr + zi * zi) > 4.0) & ~div
            if not m.any():
                continue
            div |= m
            it_ref[m] = i
            mag_ref[m] = zr[m] * zr[m] + zi[m] * zi[m]
    # it[] (decisioni di fuga) DEVE coincidere SEMPRE, per entrambe le precisioni
    if not np.array_equal(it_numba, it_ref):
        return False
    if cdt is np.complex64:
        # f32: mag[] entro tolleranza stretta (differenze FMA/riassociazione
        # di Numba, ~1-qualche ULP). Cattura errori grossolani (formula errata)
        # tollerando il rumore di arrotondamento.
        esc = (it_numba > 0) & (it_ref > 0)
        if not esc.any():
            return True
        a = mag_numba[esc].astype(np.float64)
        b = mag_ref[esc].astype(np.float64)
        rel = np.abs(a - b) / np.maximum(b, 1.0)
        return bool((rel <= 1e-2).all())
    # f64: bit-identita' stretta
    return bool(np.array_equal(mag_numba, mag_ref))
def _numba_diag():
    """Riga diagnostica CPU/Numba per Help > Informazioni (v5.9.5): se non
    si va in multi-core, spiega il perche' (da S._NUMBA_STATUS)."""
    if S._NUMBA_OK.get("f32") and S._NUMBA_OK.get("f64"):
        t = S._NUMBA_STATUS.get("tempo")
        return "Numba multi-core attivo (f32+f64%s)." % (
            ", warmup %ss" % t if t is not None else "")
    parts = []
    if S._NUMBA_STATUS.get("import") != "ok":
        parts.append(str(S._NUMBA_STATUS["import"]))
    for p in ("f64", "f32"):
        if not S._NUMBA_OK.get(p):
            parts.append("%s: %s" % (p, S._NUMBA_STATUS.get(p, "?")))
    return "Single-core (numpy). Motivo: " + "; ".join(parts)
def _numba_warmup():
    """Compila in background all'avvio i kernel di ENTRAMBE le precisioni
    (f64 e f32; il kernel Numba si auto-specializza sul dtype in ingresso) e
    fa il self-test contro il loop numpy di riferimento, una precisione per
    volta (f64: bit-identita'; f32: it[] esatto + mag[] entro tolleranza —
    vedi _numba_selftest); se il test non passa (o la compilazione fallisce)
    resta il fallback numpy per quella precisione. Esito/motivo/tempo in
    S._NUMBA_STATUS (v5.9.5: niente piu' except muti per la diagnostica UI).
    """
    t0 = time.perf_counter()
    if njit is None:
        return
    for prec, cdt in (("f64", np.complex128), ("f32", np.complex64)):
        fdt = np.float32 if cdt is np.complex64 else np.float64
        try:
            if not _numba_selftest(cdt):
                # self-test violato: fallback numpy per questa precisione
                S._NUMBA_STATUS[prec] = "self-test di correttezza fallito"
                continue
            # warmup: compila il percorso parallelo a dimensioni realistiche
            c2 = np.zeros((64, 64), dtype=cdt)
            d2 = np.zeros((64, 64), dtype=bool)
            it2 = np.zeros((64, 64), dtype=np.int32)
            mag2 = np.zeros((64, 64), dtype=fdt)
            _mandel_escape(c2, d2, it2, mag2, 32, 0, 64)
            S._NUMBA_OK[prec] = True
            S._NUMBA_STATUS[prec] = "ok (multi-core)"
        except Exception as ex:
            S._NUMBA_STATUS[prec] = "compilazione fallita: " + str(ex)[:160]
    S._NUMBA_STATUS["tempo"] = round(time.perf_counter() - t0, 1)

S._CPU_WS = {}

def _cpu_ws(w, h, prec):
    # v5.3.0: workspace per PRECISIONE (dtype f32/f64). X/Y restano float64 in
    # entrambi i casi: la griglia e' esatta e il prodotto con half viene
    # calcolato in f64 poi arrotondato al dtype di destinazione (no double
    # rounding).
    fdt = np.float32 if prec == "f32" else np.float64
    cdt = np.complex64 if prec == "f32" else np.complex128
    key = (w, h, prec)
    ws = S._CPU_WS.get(key)
    if ws is None:
        ws = {
            "X": np.arange(w) - w / 2,
            "Y": np.arange(h) - h / 2,
            "tx": np.empty(w, dtype=fdt),
            "ty": np.empty(h, dtype=fdt),
            "real": np.empty((h, w), dtype=fdt),
            "imag": np.empty((h, w), dtype=fdt),
            "c": np.empty((h, w), dtype=cdt),
            "w4": np.empty((h, w), dtype=cdt),
            # v5.0.0: z in parti reali/immaginarie (NO np.square: quel ufunc e'
            # compilato da numpy con FMA -> bit diversi dal percorso Numba).
            "zr": np.empty((h, w), dtype=fdt),
            "zi": np.empty((h, w), dtype=fdt),
            "tr": np.empty((h, w), dtype=fdt),
            "ti": np.empty((h, w), dtype=fdt),
            "diverged": np.empty((h, w), dtype=bool),
            "it": np.empty((h, w), dtype=np.int32),
            # v5.2.0: |z|^2 al momento della fuga (coloring smooth, come la GPU);
            # 0 = mai fuggito (interiore).
            "mag": np.empty((h, w), dtype=fdt),
        }
        if len(S._CPU_WS) >= 6:
            S._CPU_WS.pop(next(iter(S._CPU_WS)))
        S._CPU_WS[key] = ws
    return ws
def compute_cpu(cx, cy, half, w, h, mi, my_gen=0, prec="f64"):
    # v5.3.0: prec = "f32"/"f64" (default f64). Tutti gli array di lavoro sono
    # del dtype corrispondente; il percorso f64 resta bit-identico al
    # precedente (stesse operazioni, stesso ordine, stesso dtype).
    ws = _cpu_ws(w, h, prec)
    fdt = np.float32 if prec == "f32" else np.float64
    real, imag = ws["real"], ws["imag"]
    # Ordine delle operazioni IDENTICO al codice originale (bit-identita'
    # garantita): xs = cx + (half*X)/(w/2); ys = cy + ((half*h/w)*Y)/(h/2).
    np.multiply(ws["X"], half, out=ws["tx"])
    ws["tx"] /= (w / 2)
    ws["tx"] += cx
    np.multiply(ws["Y"], half * (h / w), out=ws["ty"])
    ws["ty"] /= (h / 2)
    ws["ty"] += cy
    np.copyto(real, ws["tx"][None, :])
    np.copyto(imag, ws["ty"][:, None])
    c = ws["c"]
    np.multiply(imag, 1j, out=c)
    c += real
    zr = ws["zr"]
    zi = ws["zi"]
    zr.fill(0)
    zi.fill(0)
    diverged = ws["diverged"]
    diverged.fill(False)
    it = ws["it"]
    it.fill(0)
    mag = ws["mag"]
    mag.fill(0.0)
    # Interior analitico (stesso criterio del kernel GPU): bulbo periodica-2 +
    # cardioide principale (|1 - sqrt(1 - 4c)| < 1). Questi pixel non divergono
    # mai -> restano it=0 (neri) ed esclusi dal loop: ~2.5x sulla CPU.
    w4 = ws["w4"]
    np.multiply(c, -4.0, out=w4)
    w4 += 1.0
    diverged |= (np.abs(w4) < 2.0 * np.sqrt(0.5 * (np.abs(w4) + np.real(w4))))
    diverged |= (np.abs(c + 1.0) <= 0.25)
    if S._NUMBA_OK.get(prec):
        # v5.0.0: escape loop parallelo Numba (bit-identico, verificato a
        # runtime dal self-test di _numba_warmup; v5.3.0: il kernel si
        # auto-specializza sul dtype in ingresso, f64 e f32).
        # Cancellazione cooperativa a livello PYTHON (affidabile): le righe
        # sono divise in 16 bande e il contatore di generazione viene
        # controllato tra una banda e l'altra. In-kernel non e' affidabile:
        # Numba/LLVM fa hoisting dei letture di memoria cross-thread dentro
        # prange, quindi un check in-kernel non vedrebbe il bump.
        BANDS = 16
        band = (h + BANDS - 1) // BANDS
        for b0 in range(0, h, band):
            if my_gen != 0 and S._GEN[0] != my_gen:
                break  # job obsoleto: stop (il worker scarta il frame)
            _mandel_escape(c, diverged, it, mag, mi, b0, min(b0 + band, h))
    else:
        # Fallback numpy (e riferimento del gate di correttezza).
        # v5.0.0: quadrato complesso in parti esplicite (a*a-b*b, 2*a*b),
        # MAI np.square (quel ufunc e' compilato da numpy con FMA -> bit
        # diversi).
        # v5.3.0: il fallback e' la stessa formula in f32 e f64 (ufunc
        # dtype-agnostic). In f32 NON e' bit-identico al kernel Numba
        # (contrazioni FMA/riassociazioni di Numba/LLVM dipendenti dal
        # contesto del loop) -> differenza di 1-qualche ULP su una piccolissima
        # frazione di pixel di bordo caotico; il self-test lo tollera (it[]
        # esatto, mag[] entro 1e-2 relativo). E' usato solo se Numba e' assente.
        cr = c.real
        ci = c.imag
        tr = ws["tr"]
        ti = ws["ti"]
        with np.errstate(over="ignore", invalid="ignore"):
            for i in range(mi):
                if my_gen != 0 and S._GEN[0] != my_gen:
                    break  # vista cambiata: il frame verra' scartato
                if not (~diverged).any():
                    break
                # z = z^2 + c, in parti (stesse operazioni e ordine del Numba)
                np.multiply(zr, zr, out=tr)   # tr = zr^2
                np.multiply(zi, zi, out=ti)   # ti = zi^2
                tr -= ti                      # tr = zr^2 - zi^2
                np.multiply(zr, zi, out=ti)   # ti = zr*zi (zr,zi ancora vecchi)
                ti *= 2.0                     # ti = 2*zr*zi
                np.add(tr, cr, out=zr)        # zr = tr + cr
                np.add(ti, ci, out=zi)        # zi = ti + ci
                m = ((zr * zr + zi * zi) > 4.0) & ~diverged
                if not m.any():
                    continue
                diverged |= m
                it[m] = i
                mag[m] = zr[m] * zr[m] + zi[m] * zi[m]
    # v5.2.0: coloring IDENTICO al kernel GPU (smooth iteration):
    # nu = it + 1 - log2(0.5*ln|z|^2), t = (nu/mi)^0.35, dove |z|^2 e' il
    # modulo al momento della fuga (mag, > 4). I pixel mai fuggiti
    # (mag == 0) restano neri. (v<=5.1.x: t=(it/mi)^0.35 + rgb[it==0]=0,
    # che confondeva "interiore" con "fuggito alla 1a iterazione" e
    # anneriva la regione lontana, dove la GPU dava il rosso scuro LUT[0..].)
    # NB: log2/log CPU (libm) vs device (CUDA) possono differire di 1-2 ULP
    # -> al massimo 1 entry di LUT di differenza vs GPU f64 (documentato
    # nel gate: check CPU-vs-GPUf64 con bound piccolo).
    esc = mag > 0.0
    nu = np.zeros((h, w), dtype=fdt)
    nu[esc] = it[esc].astype(fdt) + 1.0 - np.log2(0.5 * np.log(mag[esc]))
    t = np.power(np.clip(nu / mi, 0.0, 1.0), 0.35).ravel()
    idx = (t * 255).astype(np.uint8)
    rgb = S._LUT[idx].reshape((h, w, 3)).copy()
    rgb[~esc] = 0
    return Image.fromarray(rgb)

# ---------------- Dispatch backend ----------------
# v5.6.0: S._ACTIVE (stringa) e' l'unico stato del motore (cpu/cuda/metal/vulkan).

