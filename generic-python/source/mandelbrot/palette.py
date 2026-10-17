"""Palette: registro PALETTES + make_lut (puri, senza stato)."""
import numpy as np


# ---------------- Palette (LUT 256x3 condivisa CPU/GPU) ----------------
# Tabelle = SPEC §2.4 normativa (root AGENTS.md): 7 palette x 5 stop a
# t = 0, .25, .5, .75, 1 (stessi colori di tutti gli altri env). Il mapping
# t = (smooth/mi)^0.35 resta nel coloring (cpu.py/Metal/CUDA/Vulkan).
_TSPEC = (0.0, 0.25, 0.5, 0.75, 1.0)
def _spec(r, g, b):
    return (_TSPEC, tuple(v / 255 for v in r), tuple(v / 255 for v in g),
            tuple(v / 255 for v in b))

_FIRE = _spec((0, 170, 255, 255, 255), (0, 20, 120, 220, 255),
              (0, 0, 0, 90, 225))
_ICE = _spec((0, 0, 0, 130, 240), (0, 35, 130, 215, 255),
             (0, 110, 220, 255, 255))
_TERMAL = _spec((0, 75, 195, 250, 252), (0, 10, 25, 135, 250),
                (0, 105, 85, 30, 180))
_OCEAN = _spec((0, 0, 0, 85, 235), (0, 40, 120, 200, 250),
               (0, 95, 175, 225, 255))
_VIOLET = _spec((60, 210, 120, 0, 210), (0, 0, 40, 200, 255),
                (80, 190, 220, 230, 255))
_DESERT = _spec((0, 95, 185, 235, 255), (0, 50, 110, 190, 245),
                (0, 10, 40, 110, 220))
_FOREST = _spec((55, 115, 95, 60, 205), (32, 78, 125, 170, 235),
                (12, 32, 45, 70, 175))

# Registro palette (fonte unica): l'ordine definisce l'indice passato al kernel
# (SPEC §2.4: 0 Fire, 1 Ice, 2 Thermal, 3 Ocean, 4 Violet, 5 Desert, 6 Forest).
# UI, config e __constant__ del kernel sono tutti generati da questo dict.
PALETTES = {
    "fuoco": _FIRE,
    "ghiaccio": _ICE,
    "termal": _TERMAL,
    "ocean": _OCEAN,
    "violet": _VIOLET,
    "desert": _DESERT,
    "forest": _FOREST,
}

def make_lut(pal):
    st, r, g, b = pal
    st = np.asarray(st, dtype=np.float64)
    t2 = np.linspace(0.0, 1.0, 256)
    rgb = np.stack([np.interp(t2, st, np.asarray(r)),
                    np.interp(t2, st, np.asarray(g)),
                    np.interp(t2, st, np.asarray(b))], axis=1)
    return (rgb * 255).clip(0, 255).astype(np.uint8)

