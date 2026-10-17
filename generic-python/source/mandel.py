# ============================================================================
# Insieme di Mandelbrot - visualizzatore interattivo
# VERSIONE: 7.3.0
# ----------------------------------------------------------------------------
# Shim di compatibilita': il programma vive nel pacchetto mandelbrot/
# (config/palette/state/mem/cuda/metal/vulkan/cpu/engine/app).
# STORICO completo: git log --oneline. Ultime 10 voci in mandelbrot/__init__.py
# (HISTORY, letta da Help -> Novita` recenti...).
#
# 7.2.0 - 2026-09-04
#   - Benchmark GPU a 4x area (2x per lato) con rps normalizzato x4
#     (diluisce l'overhead fisso per-iterazione) + pre-warmup boundato
#     prima della finestra di misura; CPU resta 1x.
#   - Storici benchmark GPU rimesurati col metodo 4x (415 / 306.5 /
#     188.5 / 134).
#   - 'Codice di sicurezza' rinominato 'Codice di autenticita'.
#   - Modalita' benchmark (standard/esperta) memorizzata in config.
# 7.1.6 - 2026-09-04
#   - Diagnostica split onesta: 'parity mai eseguita' se mai calibrato,
#     'in corso' solo durante la calibrazione.
# 7.1.5 - 2026-09-04
#   - Cursore wait su root+canvas (su macOS il solo root non si vedeva
#     sul canvas e restava appeso fino al movimento del mouse).
# 7.1.4 - 2026-09-04
#   - Cursore wait sempre ripristinato (anche su frame scartati);
#     cambio motore riporta l'antialias a 1x1; nota README macOS.
# 7.1.3 - 2026-09-04
#   - Storici benchmark GPU aggiornati ai valori verificati in Esperta.
# 7.1.2 - 2026-09-04
#   - Fix dropdown GPU: con split attivo riselezionare la gpu di partenza
#     veniva ignorato (il menu tornava su Entrambe).
# 7.1.1 - 2026-09-04
#   - Codice sicurezza copiabile (Entry + Copia); clamp valori bench in
#     config (secs 1-120, w/h 64-7680, half>0: mai piu' bench accorciati).
# 7.1.0 - 2026-09-04
#   - Benchmark Esperto (3 prove da 8 s, vale la migliore) + codice di
#     sicurezza a 64 bit (tamper-evident) + Help Verifica benchmark.
# 7.0.1 - 2026-09-04
#   - Fix split: import mancanti (struct in vulkan/metal, time+BENCH/auto_mi
#     in cuda, _fmt_lut in metal); Vulkan rende di nuovo.
# 7.0.0 - 2026-09-04
#   - Split in pacchetto mandelbrot/ (ex single-file da 3774 righe);
#     matematica e kernel invariati (CPU/CUDA bit-identici, verificato).
#     Indice palette cacheato (hot-path), import lazy anti-ciclo in mem.py.
# ============================================================================

from mandelbrot import VERSION, HISTORY, CX0, CY0, HALF0, MI0, auto_mi  # noqa: F401
from mandelbrot import apply_palette, compute_cpu  # noqa: F401 (make_icon.py)
from mandelbrot.app import MandelbrotApp, main  # noqa: F401


if __name__ == "__main__":
    main()
