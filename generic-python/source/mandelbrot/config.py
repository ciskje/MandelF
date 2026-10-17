"""Costanti immutabili: vista, rendering, benchmark, percorsi, colori UI."""
import math
import os

INIT_W, INIT_H = 1280, 720
CX0, CY0, HALF0 = -0.5, 0.0, 1.5
MI0 = 200
# Iterazioni "auto": mi = MI_AUTO_BASE * (1 + log10(HALF0/half)), clamp [50, 50000]
# (coefficiente alzato da 400 a 2000 e clamp max da 10000 a 50000: le zone
# quasi paraboliche, es. vicino al becco della cardioide, fuggono lentamente
# e con il vecchio coefficiente il coloring non convergeva).
# La STESSA formula e' usata dal benchmark (vedi v4.15.0).
MI_AUTO_BASE = 2000
MI_AUTO_MIN, MI_AUTO_MAX = 50, 50000

MIN_HALF = 1e-12  # clamp minimo per half (evita zoom infinito -> half=0, stato degenere)
MIN_DIM = 50  # larghezza/altezza canvas minimi per considerare il canvas valido

# v6.1: niente tetto statico per il ricalcolo NxN: si stimano i byte
# necessari e si confrontano con la memoria davvero disponibile (VRAM via
# CUDA/Metal, RAM host via OS). Margine + riserva sotto.
PHOTO_HEADROOM = 1.25  # margine 25% sulle stime
PHOTO_HOST_RESERVE = 1 << 30  # 1 GiB di RAM mai intaccato
PHOTO_BACKSTOP_MPX = 128.0  # rete di sicurezza SOLO se la memoria libera
# non e' determinabile (query OS/driver fallita): oltre si rifiuta comunque.

# --- Percorsi e parametri ---
# Config salvata/caricata a ogni esecuzione (vista + tutti i settaggi).
# v7.3.0: cartella nascosta .mandelbrot (prima mandelbrot/).
CONFIG_PATH = os.path.join(os.path.expanduser("~"), ".mandelbrot", "config.json")

def _migrate_config():
    # v7.3.0: migrazione one-shot della vecchia posizione: se la nuova
    # config non esiste e la vecchia si, la porto nella cartella nuova
    # (niente settaggi persi all'upgrade). La vecchia cartella resta
    # intatta (mai dati utente cancellati). Silenziosa.
    old = os.path.join(os.path.expanduser("~"), "mandelbrot", "config.json")
    if not os.path.exists(CONFIG_PATH) and os.path.exists(old):
        try:
            os.makedirs(os.path.dirname(CONFIG_PATH), exist_ok=True)
            import shutil
            shutil.copy2(old, CONFIG_PATH)
        except OSError:
            pass

_migrate_config()

# Benchmark: regione + parametri. Default = regione fornita dall'utente;
# i valori sono persistiti in config.json (overridibili).
# NB: 'mi' non fa piu' parte di BENCH: e' sempre derivato dalla formula auto
# (auto_mi(bench['half'])) per mantenere il benchmark comparabile tra versioni.
BENCH = dict(cx=-0.7499302568795561, cy=-0.015139113925433963, half=5.226737155905588e-05,
             w=960, h=540, secs=8.0)

# v7.2.0: diluizione benchmark GPU: 2x per lato (4x area) per ammortizzare
# l'overhead fisso per-iterazione (Python/launch/sync); rps riportato =
# misurato x4 (kernel e copie restano nella misura: scalano col'area).
# La CPU resta 1x (a 4x area sarebbe troppo lenta).
BENCH_GPU_SCALE = 2

# v7.2.1: timeout di sicurezza (niente hang infinito del benchmark).
# Metal: un render GPU sano dura <1 s; 30 s copre anche GPU molto lente.
# CPU: single-core numpy su 960x540 @ ~11k iterazioni puo' richiedere
# decine di secondi; 180 s distingue 'lento' da 'bloccato'.
METAL_TIMEOUT_S = 30
BENCH_RENDER_LIMIT_S = 180

# Riferimenti storici per il grafico a barre del benchmark (rendering/s,
# stessa regione/parametri): evidenziano il salto CPU->GPU su macchine note.
BENCH_REF = (
    ("AMD 9900X (storico)", 6.62),
    ("4070 Super Vulkan (storico)", 134.0),
    ("5070 Ti Vulkan (storico)", 188.5),
    ("4070 Super CUDA (storico)", 306.5),
    ("5070 Ti CUDA (storico)", 415.0),
)

BACKEND_FG = {"cpu": "#1f6feb", "cuda": "#2ea44f",
              "metal": "#8957e5", "vulkan": "#d97706"}
ERR_FG = "#e5534b"

def auto_mi(half):
    """Iterazioni 'auto' per una data half: formula unica condivisa da eff_mi
    e benchmark (vedi costanti MI_AUTO_*)."""
    z = HALF0 / max(half, 1e-12)
    return int(max(MI_AUTO_MIN, min(MI_AUTO_MAX, MI_AUTO_BASE * (1.0 + math.log10(z)))))

