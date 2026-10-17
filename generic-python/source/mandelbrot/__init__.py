"""Mandelbrot v7.3.0 - pacchetto (ex mandel.py single-file).
STORICO completo: `git log --oneline`. Qui solo le ultime 10 voci
(lette da Help -> Novita` recenti...).
"""
VERSION = "7.3.0"

HISTORY = (
    ("7.3.0", "2026-09-04", "Config in cartella nascosta ~/.mandelbrot/ (prima ~/mandelbrot/), con migrazione automatica one-shot della vecchia config.json (vecchia cartella non toccata)."),
    ("7.2.1", "2026-09-04", "Benchmark senza hang infinito: timeout Metal 30 s (completed-handler con deadline invece di waitUntilCompleted bloccante; i valori status() grezzi non sono stabili tra versioni macOS) + deadline per-rendering (Metal 35 s, CPU 180 s); esiti/ambiente del benchmark in bench.log (nella cartella config); Help Informazioni mostra l'OS."),
    ("7.2.0", "2026-09-04", "Benchmark GPU a 4x area (2x per lato) con rps normalizzato x4 + pre-warmup pre-finestra; CPU 1x invariato; storici GPU rimesurati col metodo 4x; 'codice di sicurezza' ora 'codice di autenticità'; modalità standard/esperta memorizzata in config."),
    ("7.1.6", "2026-09-04", "Diagnostica split onesta: 'parity mai eseguita' se mai calibrato, 'in corso' solo durante la calibrazione."),
    ("7.1.5", "2026-09-04", "Cursore wait su root+canvas (su macOS il solo root non si vedeva sul canvas e restava appeso fino al movimento del mouse)."),
    ("7.1.4", "2026-09-04", "Cursore wait sempre ripristinato (anche su frame scartati); cambio motore riporta l'antialias a 1x1; nota README per il primo avvio su macOS."),
    ("7.1.3", "2026-09-04", "Storici benchmark GPU aggiornati ai valori verificati in modalita' Esperta."),
    ("7.1.1", "2026-09-04", "Codice sicurezza copiabile (Entry + Copia); clamp valori bench in config (secs/w/h/half sempre sani)."),
    ("7.1.0", "2026-09-04", "Benchmark Esperto (3x8 s, vale la migliore) + codice di sicurezza a 64 bit + Help Verifica benchmark."),
    ("7.0.1", "2026-09-04", "Fix split: import mancanti (struct in vulkan/metal, time+BENCH/auto_mi in cuda, _fmt_lut in metal); Vulkan rende di nuovo."),
)

# Compat: make_icon.py e vecchi `import mandel` usano questi nomi.
from .config import CX0, CY0, HALF0, MI0, auto_mi  # noqa: F401
from .state import apply_palette  # noqa: F401
from .cpu import compute_cpu  # noqa: F401
