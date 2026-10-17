"""Palette: registro PALETTES + make_lut (puri, senza stato)."""
import numpy as np


# ---------------- Palette (LUT 256x3 condivisa CPU/GPU) ----------------
_FIRE = (
    (0.0, 0.2, 0.45, 0.7, 0.9, 1.0),
    (0.05, 0.35, 0.85, 1.0, 1.0, 1.0),
    (0.0, 0.02, 0.2, 0.65, 0.95, 1.0),
    (0.0, 0.0, 0.02, 0.15, 0.55, 1.0),
)
_ICE = (
    (0.0, 0.25, 0.5, 0.75, 1.0),
    (0.02, 0.05, 0.30, 0.70, 1.0),
    (0.02, 0.15, 0.55, 0.85, 1.0),
    (0.10, 0.45, 0.90, 1.0, 1.0),
)
# Termal: parte col ghiaccio (blu/ciano chiari), passa dal bianco gelido e
# finisce col fuoco (oro/arancio/rosso). t=0 ghiaccio, t=1 fuoco.
_TERMAL = (
    (0.0,  0.2,  0.4,  0.55, 0.7,  0.85, 1.0),
    (0.02, 0.10, 0.55, 0.95, 1.00, 1.00, 1.00),
    (0.08, 0.45, 0.80, 0.96, 0.85, 0.55, 0.30),
    (0.28, 0.85, 0.95, 0.98, 0.45, 0.20, 0.10),
)

# Registro palette (fonte unica): l'ordine definisce l'indice passato al kernel
# (0=fuoco, 1=ghiaccio, 2=termal). UI, config e __constant__ del kernel sono
# tutti generati da questo dict.
PALETTES = {
    "fuoco": _FIRE,
    "ghiaccio": _ICE,
    "termal": _TERMAL,
}

def make_lut(pal):
    st, r, g, b = pal
    st = np.asarray(st, dtype=np.float64)
    t2 = np.linspace(0.0, 1.0, 256)
    rgb = np.stack([np.interp(t2, st, np.asarray(r)),
                    np.interp(t2, st, np.asarray(g)),
                    np.interp(t2, st, np.asarray(b))], axis=1)
    return (rgb * 255).clip(0, 255).astype(np.uint8)

