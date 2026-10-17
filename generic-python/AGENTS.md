# Convenzioni del progetto

## 1. Struttura e file
Git traccia solo i sorgenti; tutto il resto è generato o dato utente.

| file | ruolo |
|---|---|
| `mandelbrot/` | il programma (pacchetto: `config`/`palette`/`state`/`mem`/`cpu`/`cuda`/`metal`/`vulkan`/`engine`/`app`/`expert`; Tkinter + 4 backend con fallback CPU). Fonte di verità per versione (`__init__.py`: `VERSION` + `HISTORY` ultime 10) |
| `mandel.py` | shim entry-point (re-export + `main()`); tiene l'header `# VERSIONE` letto da `mandelbrot.spec`/`build_app.py` e la compat `import mandel` di `make_icon.py` |
| `spec.md` | specifica; resta in sync col sorgente (regola §2) |
| `README.md` | pagina GitHub (titolo con versione, Download da Release) + `screenshot-*.png` |
| `LICENSE` | MIT |
| `build_app.py` | unico script di build (icona → PyInstaller → post); dettaglio in `spec.md` §11 |
| `mandelbrot.spec` | ricetta PyInstaller multipiattaforma |
| `hook_dlldir.py` | runtime hook Windows (`os.add_dll_directory` su EXE + `_internal`) |
| `make_icon.py` | genera `icon_src.png` + `mandelbrot.ico` (+ `.icns` su macOS) |

Mai in git (gitignorati): `dist/` (anche gli zip/`.dmg` distributivi), `build/`,
`icon_src.png`, `mandelbrot.ico/.icns`, `mandelbrot_*.json` (zone utente),
config utente.

## 2. Versionamento sorgente (OBBLIGATORIO)
Solo per modifiche a sorgenti Python (`mandelbrot/`, `mandel.py` e altri `.py`).
Le modifiche alla sola `spec.md`/doc non richiedono bump.
Ogni modifica bumper DEVE:
1. Incrementare la versione in entrambi i punti (devono coincidere):
   costante `VERSION = "X.Y.Z"` in `mandelbrot/__init__.py` (titolo + JSON
   zona) e header `# VERSIONE: X.Y.Z` dello shim `mandel.py` (letto da
   `mandelbrot.spec`/`build_app.py`).
   Livelli: patch `x.y.Z` (bugfix o modifica minima: un dialog, un testo,
   un'opzione); minor `x.Y.0` (funzione nuova/cambiata); major `X.0.0`
   (riscrittura/architettura).
2. Aggiungere voce allo STORICO (`VERSIONE - AAAA-MM-GG - descrizione`).
3. Aggiornare `spec.md` al nuovo stato (sempre in sincronia).

## 3. Build dell'app (multipiattaforma)
Mai di iniziativa, nemmeno prima di un commit (l'artefatto indietro è
normale): solo con `python build_app.py` su richiesta esplicita, fuori
dall'ordine automatico. Ordine: modifica → versione+STORICO+spec (§2) →
verifica. Commit e push su GitHub SOLO su richiesta esplicita, mai in
automatico (istruzione permanente: niente commit/push di iniziativa).

| piattaforma | app (disco di sistema, temp) | distributivo (progetto/NAS, `dist/`) |
|---|---|---|
| Windows | `mandelbrot_dist/Mandelbrot/Mandelbrot.exe` + `_internal/` | `dist/Mandelbrot-v<ver>-win64.zip` |
| macOS | `mandelbrot_dist/Mandelbrot.app` (firma ad-hoc) | `dist/Mandelbrot-v<ver>-macos.dmg` |

Note: intermedie (`mandelbrot_build`) e app stanno in temp utente (il progetto
può stare su share lenta); Vulkan bundled out-of-the-box, CUDA richiede
driver + runtime utente (cuda-pathfinder); ritenzione `KEEP_N = 3` versioni
per piattaforma in `dist/` (rimozione automatica delle più vecchie).
README per le release (istruzione permanente): a ogni bump di versione
aggiornare in `README.md` il numero nel titolo e, se si pubblica una
Release, i link della sezione Download
(`.../releases/download/<tag>/<file>` per piattaforma).
Su "builda" (istruzione permanente): prima commit dei sorgenti pendenti
(così la build corrisponde sempre a un commit esistente), poi
`python build_app.py` + smoke EXE. E basta.
Su "pusha" (istruzione permanente): commit di eventuali pendenti, poi
controllo quali dmg/zip in `dist/` mancano su GitHub (Release per tag),
aggiornamento Release (crea o sostituisci asset) + allineamento `README.md`
(numero titolo + link Download), poi push.
Dettaglio tecnico: `spec.md` §11.

## 4. Note operative
- Gotchas (FMA, Numba, CuPy, Metal, `__constant__`, pinned memory): casa unica
  `spec.md` §12.
- GPU condivisa col server LLM locale (llama.cpp): non fermarlo mai.
  Benchmark rumorosi → workload bounded (n launch fissi, non loop a tempo),
  confronto a parità di condizioni, meglio A/B alternato nello stesso processo.
- Clock GPU: dopo pause oltre 10 s (es. render CPU) torna idle e la prima
  misura è molto più lenta (ordine 10x); scaldare con un burst (es. qualche
  render 64x64) prima di misurare.
