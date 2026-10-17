# Genera l'icona dell'app: icon_src.png (1024x1024) + mandelbrot.ico (multi-size)
# + mandelbrot.icns (solo macOS, da icon_src.png via sips).
# Usa il motore CPU di mandel.py (Numba o fallback numpy, bit-identico) - NO GPU,
# NO display. Palette 'termal' (convenzione icona dell'app, vedi mandel.py/STORICO).
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import mandel  # noqa: E402

SIZE = 1024
MI = 800
PAL = "termal"

mandel.apply_palette(PAL)
# Vista intera dell'insieme (CX0/CY0/HALF0 definiti in mandel.py).
img = mandel.compute_cpu(mandel.CX0, mandel.CY0, mandel.HALF0,
                         SIZE, SIZE, MI, prec="f64")
png = os.path.join(HERE, "icon_src.png")
ico = os.path.join(HERE, "mandelbrot.ico")
img.save(png)
img.save(ico, sizes=[(16, 16), (24, 24), (32, 32), (48, 48),
                     (64, 64), (128, 128), (256, 256)])
print("icona generata:", png)
print("icona generata:", ico)

# Icona macOS (solo darwin): mandelbrot.icns da icon_src.png via Pillow (sips inaffidabile)
if sys.platform == "darwin":
    icns = os.path.join(HERE, "mandelbrot.icns")
    img.save(icns, format="ICNS", sizes=[(s, s) for s in (16, 32, 64, 128, 256, 512, 1024)])
    print("icona generata:", icns)
