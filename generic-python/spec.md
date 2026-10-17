# Visualizzatore Mandelbrot — spec di ricreazione

Descrizione concisa ma sufficiente perché un altro LLM (o sviluppatore) ricrei il
programma da zero. Riferimento: pacchetto `mandelbrot/` + shim `mandel.py`.
Ogni modifica al sorgente DEVE aggiornare anche questa spec (vedi `AGENTS.md`).

Convenzioni: `cpu|cuda|metal|vulkan` = valori di `_ACTIVE`; `Cpu/Cuda/...`
capitalizzato = label toolbar; `CUDA/METAL/VULKAN` upper-case = prefisso di
`backend()` (CPU resta `CPU`); `f32/f64` sempre minuscoli; `FMA`/`ULP` sempre
maiuscoli; decimali col punto; unità `ms/s/px/pt`.

Struttura in ordine di costruzione (§1→§12): stack → matematica → palette → MI →
backend → implementazione (§6.1–§6.4) → pipeline → UI → file → benchmark →
build → gotchas (§12.1–§12.6).

## 1. Stack e architettura
- Pacchetto Python 3.14 `mandelbrot/` + shim `mandel.py`: GUI `tkinter`,
  rendering su 1 di 4 backend selezionabili — CPU (numpy/Numba), CUDA (CuPy
  `RawKernel`), Metal (pyobjc), Vulkan (wgpu/wgpu-native) — con fallback CPU;
  Pillow per PNG. `mandel.py` resta l'entry point (re-export + `main()`) così
  `mandelbrot.spec`, `build_app.py` e `make_icon.py` sono invariati.
  Moduli: `config` (costanti), `palette` (PALETTES/make_lut puri), `state`
  (tutti i mutabili ex-globali + `apply_palette`), `mem` (query memoria),
  `cpu`/`cuda`/`metal`/`vulkan` (backend), `engine` (dispatch `compute()` +
  warmup), `app` (`MandelbrotApp` + `main`). I moduli leggono/scrivono lo
  stato come `S.NOME` (`from . import state as S`); `mem` importa `cuda` lazy
  dentro la funzione (niente cicli import); `metal` riusa `_fmt_lut` da `cuda`.
  (v7.0.1: audit post-split degli import — ogni `NameError` uccideva il worker
  al primo frame del backend corrispondente, con UI apparentemente bloccata.)
  Backend non disponibili restano visibili in toolbar ma disabilitati (grigi).
- Render su thread worker, UI mai bloccata (§7).
- Metodi di `MandelbrotApp` (in `mandelbrot/app.py`) raggruppati per funzione:
  UI, vista, controlli, pipeline, file, foto, benchmark.
- Versione in due punti che devono coincidere: costante `VERSION = "X.Y.Z"`
  in `mandelbrot/__init__.py` (titolo + JSON zona + Help) e header
  `# VERSIONE: X.Y.Z` dello shim `mandel.py` (letto da `mandelbrot.spec`/
  `build_app.py` per EXE/.app/zip). `HISTORY` (ultime 10) vive in
  `mandelbrot/__init__.py`. Incremento/STORICO: vedi `AGENTS.md`.
- Dipendenze: numpy (sempre); Numba (opzionale, altrimenti fallback numpy
  single-core); CuPy (CUDA); pyobjc (Metal); wgpu (Vulkan); Pillow (PNG, icona).

## 2. Matematica del rendering
Comune alle 4 vie (differenze solo di 1–2 ULP, vedi §12.2–§12.3).
- Escape `z = z*z + c`, `|z|^2 > 4`; punti mai fuggiti = nero.
- Interior analitico prima del loop (stesso su CPU/GPU, salta le `mi`
  iterazioni): bulbo periodo-2 `|c+1| <= 0.25` (`d2 = (x0+1)^2 + y0^2 <= 0.0625`)
  e cardioide `|1 - sqrt(1-4c)| < 1` (forma senza complessi
  `R < 2*sqrt(0.5*(R+A))`, `A = 1-4*Re(c)`, `R = |1-4c|`, con prefiltro
  bounding-box).
- GPU: iterazione in coordinata spostata `w = z - cx` (solo parte reale).
- Coloring smooth identico CPU/GPU: `nu = it + 1 - log2(0.5*ln(mag))`
  (`mag = |z|^2` alla fuga, `> 4`), `t = clip(nu/mi,0,1)^0.35`.
  Solo `mag == 0` resta nero (distingue interiore da fuga a `it == 0`).
- LUT 256x3 condivisa: `make_lut` = `linspace(0,1,256)` float64,
  `np.interp` per canale, `*255`, clip, `uint8`. Indice =
  `(t*255).astype(uint8)` = troncamento (non `round`).
  `log2/log` libm-vs-device differiscono di 1–2 ULP → al massimo 1 entry LUT.
- LUT nel kernel: CUDA/Metal una `constant` per palette incorporata nel
  sorgente; Vulkan un unico buffer `storage` con tutte le palette concatenate
  (`lut[pal*256+idx]`, colori packed `0x00RRGGBB` perché WGSL non ha `u8`).

## 3. Palette
Registro `PALETTES` (fonte unica): ordine = indice kernel (0 fuoco, 1 ghiaccio,
2 termal). UI, config e LUT kernel sono generati da qui. Default `fuoco`.

| palette  | n.stop | t | R | G | B |
|---|---|---|---|---|---|
| fuoco | 6 | 0,.2,.45,.7,.9,1 | .05,.35,.85,1,1,1 | 0,.02,.2,.65,.95,1 | 0,0,.02,.15,.55,1 |
| ghiaccio | 5 | 0,.25,.5,.75,1 | .02,.05,.30,.70,1 | .02,.15,.55,.85,1 | .10,.45,.90,1,1 |
| termal | 7 | 0,.2,.4,.55,.7,.85,1 | .02,.10,.55,.95,1,1,1 | .08,.45,.80,.96,.85,.55,.30 | .28,.85,.95,.98,.45,.20,.10 |

## 4. Iterazioni (MI)
- Auto (default, formula unica condivisa col benchmark):
  `auto_mi(half) = int(max(50, min(50000, 2000*(1 + log10(1.5/max(half,1e-12))))))`.
  Vista iniziale (`half = 1.5`) → 2000; default benchmark
  (`half = 5.226737155905588e-05`) → 10915; `half = 5.2e-05` → 10920.
- Manuale: entry (commit su Invio/FocusOut; intero 50–100000, altrimenti
  messaggio in status bar + ripristino) e pulsanti `±1000`
  (`change_mi`: `max(50, mi+d)`, senza clamp superiore — può superare 100000).
  I pulsanti sono disabilitati in auto; in auto il campo è disabilitato ma
  mostra il valore corrente; uscendo dall'auto, `mi` si congela sul valore
  auto corrente. Reset ripristina `mi = MI0 = 200` + auto.
- Canvas sotto `MIN_DIM = 50` px è invalido → si usa 1280x720.

## 5. Backend e precisione
- Rilevamento all'avvio in ordine di preferenza: CUDA > Metal > Vulkan > CPU
  (`_BACKENDS_OK`, `_ACTIVE` stringa `cpu|cuda|metal|vulkan`); default = primo
  disponibile (`_default_backend()`); `compute` dispatcha su `_ACTIVE`.
- Toolbar "Motore": 4 `Radiobutton` nello stesso `Frame`, senza `variable`
  (Tk non li raggruppa da solo; la mutua esclusione è manuale via
  `.select()`/`.deselect()`); non disponibili = `state="disabled"`.
- `backend()`: GPU → `_ACTIVE.upper() + " " + _PREC` (es. `CUDA f32`);
  CPU → `"CPU " + _PREC + " multi-core"` col kernel Numba parallelo,
  `"CPU " + _PREC + " single-core (numpy)"` col fallback. La scelta segue
  `_NUMBA_OK[_PREC]` (per precisione, come `compute_cpu`), non il solo import;
  prima del warmup i render usano davvero il fallback → `single-core`.

| backend | f32 | f64 (condizione) |
|---|---|---|
| cpu | sì | sì, sempre (anche fallback numpy, senza check Numba) |
| cuda | sì | sì, solo se il kernel f64 compila (`_KERNEL_F64 is not None`) |
| metal | sì | mai (f32-only) |
| vulkan | sì | mai (f32-only) |

- GPU multipla: dropdown `GPU:` nella 2a riga toolbar, contenuto per motore
  attivo (nascosto se il motore ha <= 1 GPU). CUDA: device da `_CUDA_DEVICES`
  (`gpu1`/`gpu2`/...) + voce `Entrambe (split)` (v6.2): spartisce ogni
  render in bande orizzontali sulle prime 2 CUDA con rapporto fisso
  (v6.2.5: dropdown 33% 66% / 50% 50% / 66% 33% + label corrente;
  calibrazione automatica solo su richiesta col pulsante ricampiona);
    kernel con offset `row0` (bit-identico al single), buffer device separati
    per banda (`_CUDA_SPLIT_BUFS`), cucitura su host pinned, fallback single
    in errore; sotto 32 px di altezza resta single (soglia tecnica). Render
    interattivi, full, foto NxN e benchmark (v6.2.4) usano lo split;
    warmup resta sul device scelto. Selezione singola esce dallo split
    (v7.1.2: anche il ritorno sulla gpu di partenza, che col vecchio check
    `_pos == _CUDA_DEV` veniva ignorato col menu che tornava su Entrambe);
    reset torna a gpu1. Diagnostica in Informazioni: rapporto + `mai
    eseguita` (calibrazione on-demand mai lanciata) / `in corso` / esito
    parity. Titolo/stato con entrambi i nomi (`hw_name`).
   Selezioni persistite (`cuda_device`, `cuda_split`, `cuda_split_ratio`,
   `vulkan_adapter`). Vulkan (v5.9.2): adapter fisici backend Vulkan da
   `_VULKAN_ADAPTERS` (deduplicati per vendor/device, default = ex
  high-performance); cambio via `VulkanBackend.select_adapter()` (ricrea
  risorse device-bound sotto lock); warmup 64x64 in background sul nuovo
  device/adapter se il motore è attivo.
- Precisione = settaggio globale unico (default f32). Bottone f64 dinamico
  (`_sync_precision_buttons`, chiamato in `_build_toolbar`/`set_backend`/`reset`;
  `load_config` passa da `_select_backend`/`_select_precision` + sync):
  abilitato solo se il backend corrente supporta f64; passando a backend
  f32-only con f64 attiva si torna a f32; `set_prec` rifiuta f64 non
  supportata. La precisione è applicata dopo il motore al load (vincolo
  d'ordine).
- Nota misurata: f32 non accelera il loop di escape (latency-bound); guadagno
  solo sui passi memory-bound (geometria/prefiltro/coloring): 1.6–1.7x
  esterno, 1.07x deep-zoom.

## 6. Implementazione per backend
Comune: matematica §2, 1 px/thread, griglia 16x16 (256 thread), LUT come §2.
Sotto solo scarti + gotcha API.

### 6.1 CPU (numpy/Numba)
- Array di lavoro cacheati per `(w,h,prec)` (offset griglia + real/imag/c/w4 +
  work array, max 6) → nessuna allocazione per render.
- Escape in Numba `@njit(parallel=True)`, `prange` sulle righe, early-exit;
  geometria/interior/coloring in numpy; produce `it[]` + `mag[]`.
- Senza Numba (o self-test fallito per quella precisione): fallback numpy
  single-core, `backend()` = `CPU f32/f64 single-core (numpy)`,
  flag `_NUMBA_AVAILABLE = (njit is not None)`, stato per precisione
  `_NUMBA_OK = {"f32": bool, "f64": bool}`.
- f32/f64: array nel dtype target (X/Y restano f64, prodotto con `half` in f64
  poi arrotondato); kernel Numba auto-specializzato `complex64/128`;
  fallback = stessa sequenza di ufunc in parti esplicite (no FMA) anche in f32.
- Self-test all'avvio in thread (4x4 con casi limite vs loop numpy), una
  precisione per volta → fallback numpy automatico per la precisione che
  fallisce. Criterio: f64 bit-identità di `it[]` e `mag[]`; f32 `it[]` esatto +
  `mag[]` entro 1e-2 relativo (FMA Numba-f32 contesto-dipendente, §12.2).
- Warmup Numba di entrambe le precisioni in thread all'avvio.
- Cancellazione cooperativa: vedi §7.

### 6.2 CUDA (CuPy `RawKernel`)
- 1 px/thread, `__launch_bounds__(256)`, block 16x16, grid
  `(ceil(w/16), ceil(bh/16))`, `options=("--use_fast_math",)`; 2 varianti
  f32/f64 dalla stessa sorgente; NVRTC lazy (no `-O3`). Guardia bounds su
  entrambi gli assi (`col >= w || out_row >= bh`). Parametro `row0` (v6.2):
  prima riga della banda per lo split multi-GPU (griglia sulla banda, `row0+ty`
  per il calcolo della coordinata, `out_row=ty` per l'indice nel buffer locale
  alla banda → v6.2.3: fix OOB, prima scriveva a indici assoluti in buffer
  locali). Lancio unico `_cuda_launch_band` per single e split. Handle kernel
   per-device (`_kern_for(dev, use64)`, v6.2.3: no RawKernel condiviso tra
   thread). Parity (v6.2.1): split e single sulla vista bench devono essere
   bit-identici (`_cuda_split_parity`, v6.2.2: pattern-match + ordine invertito);
   in caso di diff lo split si auto-disattiva (rapporto + esito in Help >
   Informazioni, `_cuda_split_diag`). v6.2.5: calibrazione + parity solo su
   richiesta (pulsante ricampiona); rapporto fisso via dropdown (1/3, 50/50, 2/3).
- Scalari = array numpy size-1; indice palette preallocato (`_PAL_IDX`);
  `np.asarray` su array CuPy vietato → `.get()`.
- D2H pinned: `PinnedMemory(size)` + `runtime.memcpy(..., memcpyDeviceToHost)`
  cacheato per dimensione (DMA 1.7x di `.get()` pageable), fallback `.get()`;
  vista numpy via `np.frombuffer` su `ctypes` all'indirizzo `pm.ptr`.
- CuPy 14: niente `MemoryHost` / `Event.elapsed_time` → `PinnedMemory` + `ptr`,
  timing `perf_counter` + sync.
- Warmup all'avvio in daemon: render 64x64 f32 (+ f64 se supportata) → context,
  module load e prime allocazioni fuori dal primo render.

### 6.3 Metal (pyobjc, f32-only)
- Kernel MSL `mandel`, threadgroup 16x16 (256, sotto max 1024 di M1),
  grid `(ceil(w/16), ceil(h/16))`.
- Parametri `(cx,cy,half,w,h,mi,pal)` in uno `struct` via `[[buffer(1)]]`
  (né scalari nudi né singoli float); output RGB888 in `[[buffer(0)]]`.
- H2D/D2H: `buf.contents().as_buffer(N)` (memoryview C-level, circa 0.01 ms;
  la varlist non è bytes-like).
- Deterministico (2 run bit-identici). Misurato 12–30x la CPU su M1.
- Gotcha: `fmin/fmax/pow/sqrt/log2/log` (senza `f`); `half` è tipo riservato →
  campo `hs`; puntatori con address space esplicito (`device`/`constant`);
   `setBuffer:offset:atIndex:` = `(buffer, offset, index)` → `(pbuf, 0, 1)`;
   compute sincrono (`commit` + attesa + read) + `Lock`; attesa = completed
   handler + `threading.Event` con deadline `METAL_TIMEOUT_S = 30` s
   (v7.2.1, prima `waitUntilCompleted()` bloccante: una GPU impallassi dava
   hang infinito); fallimento → `cmd.error()` nel dettaglio →
   `RuntimeError`. Gotcha (v7.2.1): i valori grezzi di `status()` NON sono
   confrontabili — l'enum cambia tra versioni macOS (su macOS 26
   `Completed = 4`, su versioni precedenti `5`): mai `if st == 5`.

### 6.4 Vulkan (wgpu 0.32, f32-only)
- Shader WGSL `main`, workgroup 16x16 (256), dispatch
  `(ceil(w/16), ceil(h/16))`. Lib nativa nella wheel, nessuna runtime esterna;
  funziona su AMD/NVIDIA/Intel.
- Vincoli WGSL: output e LUT = `array<u32>` packed `0x00RRGGBB` (unpack numpy
  `(h,w,3)` via shift al readback); parametri in 2 uniform
  (`vec4<f32>` cx,cy,half + `vec4<i32>` w,h,mi,pal); LUT storage concatenata;
  built-in senza `f`; `var`/`let`; `break` ultimo del blocco.
- Deterministico (2 run bit-identici, verificato su AMD 780M).
- H2D `queue.write_buffer` (buffer persistenti `UNIFORM|COPY_DST`); D2H
  `queue.read_buffer` (buffer `STORAGE|COPY_SRC`); `queue.submit([enc.finish()])`
  + `Lock` (sincrono).
- Selezione adapter (v5.9.2): enumera gli adapter, tiene uno per GPU fisica
  con `backend_type == "Vulkan"` (scarta dupe D3D12/OpenGL/WARP); default =
  ex high-performance; `select_adapter()` ricrea device/pipeline/buffer
  sotto il lock esistente (mai in gara con `compute`).
- API "WebGPU-style": `gpu.enumerate_adapters_sync()`,
  `gpu.request_adapter_sync(power_preference="high-performance")` (solo per
  individuare il default),
  `adapter.request_device_sync()`, `create_shader_module(code=...)`,
  `create_compute_pipeline(layout="auto", compute=ProgrammableStage(...))`,
  `create_buffer(size=, usage=)` / `create_buffer_with_data(data=, usage=)`
  (keyword, non descrittore), `create_bind_group(layout=, entries=
  [BindGroupEntry(binding=N, resource=buf)])`, layout da
  `pipe.get_bind_group_layout(0)`; bundle via `collect_all(wgpu/cffi)` +
  `hiddenimports wgpu.backends.wgpu_native`.

## 7. Pipeline (asincrona, latest-wins)
- Worker daemon + `Condition` + slot job singolo; request in coda collassano
  sull'ultimo. Durante il benchmark il worker scarta i job E il ricalcolo NxN
  e' sospeso (`take_photo` rifiuta, `_maybe_full` non lancia, pendenti
  cancellati: niente contesa col thread bench sullo stesso device, su GPU
  display evita TDR/reset e conteggi crollati). Bench chiesto durante una
  foto parte accodato a fine ricalcolo (`_bench_after_photo`); a fine bench
  la vista si rinfresca con la scala persistente (`recalc_scaled`).
- `_submit`: `self._gen += 1; _GEN[0] = self._gen` (`_GEN` =
  `np.zeros(1, int32)`); worker scarta il frame se `_GEN[0] != gen` prima di
  `frames.put` (mostrato poi da `_poll`).
- Cursore `watch` durante il render interattivo (`_worker_loop`), tolto in
  `_show()`; se il frame e' scartato come obsoleto il watch si toglie solo
  se non c'e' lavoro piu' nuovo in coda (v7.1.4: prima restava appeso).
  Il watch va su root + canvas (`_busy_on`/`_busy_off`, v7.1.5): su macOS
  il cursore del solo toplevel non si propaga al canvas sotto il puntatore
  (invisibile durante il render, poi appeso fino al primo movimento).
- Cancellazione cooperativa solo CPU: Numba ogni banda (16 bande totali,
  `band = ceil(h/16)`; check Python tra bande — check in-kernel inaffidabile
  per hoisting in `prange`, §12.4); fallback numpy ogni iterazione.
  `my_gen = 0` disattiva il check (uso interno benchmark).
  GPU non cancellabile (render tipico sotto 160 ms), solo scarto post-render.
- `request_render`: preview immediata `max(w//div,16)` con `div = 8` se CPU
  (draft leggero durante l'interazione), `div = 4` su GPU; poi full dopo
  500 ms solo se vista invariata. Poll UI ogni 30 ms (`after`), resize `NEAREST`.

## 8. UI e interazione
### 8.1 Vista e navigazione
- Avvio: centro `(-0.5, 0)`, `half = 1.5`, canvas 1280x720 (16:9); asse y scalato `h/w`;
  clamp `half >= MIN_HALF = 1e-12`.
- Rotella x1.25/x0.8 al cursore; click x2 al cursore; click destro x0.5 al
  cursore (due dita sul trackpad; bind su `ButtonPress-2/3`); `+`/`-` x2/x0.5
  al centro e `r` = reset legati alla finestra (`root.bind` con guardia che
  li ignora col focus su una `Entry`; il click sul canvas prende il focus).
- `r` = reset; `Ctrl+S` (bind su root) = salva PNG.

### 8.2 Layout, titolo, status
- Toolbar: `Motore:` (4 radio + dropdown `GPU:` con device/adapter del
  motore attivo, se > 1) / `Palette:` (check dal registro) / `Precisione:`
  (`f32`/`f64`) / `Iter:` + `Auto`. Seconda riga: label
  `Iterazioni:` / `Iterazioni (auto):`, entry (largh. 7, allineata a destra),
  `-1000`/`+1000`, `Ricalcola` + dropdown scala `1x1`/`2x2`/`4x4`/`8x8`
  (default `1x1`), `Benchmark`, `Reset`. Poi canvas, poi status bar.
  Il cambio motore riporta la scala antialias a `1x1` (v7.1.4).
  Font `TkDefaultFont/TkTextFont/TkMenuFont` 13 pt.
   Menu File: `Salva immagine... (Ctrl+S)`, `Carica zona...`, `Salva zona`,
   `Salva zona con nome...`, separatore, `Esci`.
   Menu Help: `Istruzioni...` (guida rapida: navigazione, motore/precisione,
   iterazioni, palette, file, foto, benchmark), `Novità recenti...` (ultime 10
   modifiche da tupla embedded `HISTORY`, righe versione/data/descrizione),
   separatore, `Informazioni...` (versione, backend/hardware attivi, autore
   Francesco Ferrara `<occhiobello@gmail.com>`, diagnostica CPU/Numba da
   `_numba_diag()`). Dialog `Toplevel` modali centrati via `_modal`. `HISTORY` è embedded (non STORICO commentato, che
   PyInstaller scarta): va aggiornata a ogni bump (voce in testa, max 10).
- Titolo: `Insieme di Mandelbrot v<VER> - <backend()> (<hw>)`
  + ` - <basename zona>` se presente; ricalcolato a ogni frame in `_show`
  così segue la transizione warmup `single-core` → `multi-core`.
- Status: `<msg> | <backend> · <hw> | <palette> | render: <N> ms`
  (+ ` · single-core: motivo in Help > Informazioni` in rosso se la CPU è
  sul fallback numpy; si auto-cancella al multi-core). Esito warmup in
  `_NUMBA_STATUS` (import/self-test/compilazione/tempo per precisione).
- `Entry` disabilitata: `delete`/`insert` sono no-op → riabilitare prima (§12.6).

### 8.3 Hardware, cursore, tema
- `hw_name()` in cache per backend, senza nuove dipendenze; fallback
  `CPU`/`GPU` su errore o nome vuoto: CPU da registro Windows
  (`ProcessorNameString`) / `sysctl machdep.cpu.brand_string` su macOS /
   `platform.processor()` altrove; CUDA da `getDeviceProperties()` sul device
   selezionato (dropdown GPU, default 0; con più GPU l'ordine può differire
   da `nvidia-smi`); Metal da
   `dev.name` (se callable va invocato); Vulkan dall'adapter selezionato
   (`VulkanBackend.name`, default ex high-performance).
- Benchmark: cursore `watch` in `run_benchmark()`, ripristino in
  `_bench_done()` (unico punto di uscita, anche su errore).
- Ricalcola con scala (pulsante `Ricalcola` + dropdown esplicito
  `1x1`/`2x2`/`4x4`/`8x8`, default `1x1`, dispatcher `recalc_scaled()`):
  la selezione ricalcola subito (trace su `recalc_var`) e resta persistente
  (`_recalc_n()`): `1x1` rifa il rendering della vista corrente
  (`request_render`); NxN ricalcola la vista a NxN per lato. Con scala NxN
  anche il full dopo pan/zoom/MI va in antialiasing (`_maybe_full` chiama
  `take_photo(n)`; la preview draft resta leggera). Richieste durante un
  ricalcolo in corso accodate (`_photo_pending`, latest-wins: `_photo_done`
  rilancia sulla vista corrente).
  box-filter sull'output, unico code-path per tutti i backend dato che la
  GPU colora in-kernel); cursore `watch` durante il calcolo, ripristino in
  `_photo_done()` (unico punto di uscita, anche su errore); all'avvio
  invalida i render interattivi in volo/pendenti (bump `_GEN` + cancella
  il full-timer ritardato) e il risultato e' scartato se vista, dimensione
  canvas, palette, motore o precisione cambiano nel frattempo (snapshot
  esteso con `w,h`, non generazione); `self.pil` aggiornata così `Ctrl+S`
  salva la versione   antialiased. Un ricalcolo alla volta con accodamento latest-wins
  (`_photo_pending`: richieste durante un ricalcolo rilanciano sulla vista
  corrente a fine run). Guardia memoria dinamica (`_photo_mem_ok`, niente
  tetto statico): stima B/px del frame grande per backend (CPU 56/96 f32/f64,
  CUDA 8 host + 3 device, Metal 8 unified, Vulkan 10 host + 4 device) vs RAM
  libera da OS (`GlobalMemoryStatusEx`/`AVPHYS_PAGES`) e VRAM libera da CUDA
  (`memGetInfo`), con margine 25% + 1 GiB mai intaccato; rete di sicurezza a
  128 Mpx solo se la query fallisce; `MemoryError` -> errore pulito;
  workspace CPU gigante rimosso dalla cache `_CPU_WS` dopo l'uso (non inquina
  gli entry interattivi).
- Tema: Button/Checkbutton/Radiobutton nativi (mai colorati: su macOS
  degradano a flat illeggibili, lezione 5.4.2); colore solo su Label/Frame:
  status bar e barra accento (3px sopra il canvas) nel colore del motore
  (`BACKEND_FG`: CPU `#1f6feb`, CUDA `#2ea44f`, Metal `#8957e5`, Vulkan
  `#d97706`), titoli dialog Help colorati, errori in rosso `#e5534b`.
  Dialog benchmark: testo di sistema; accenti OK/KO `#2ea44f`/`#e5534b`,
  divisore `#8a8a8a`; grafico: griglia `#a0a0a0`, assi/divisore `#8a8a8a`.
  Solo UI, nessun effetto sul rendering.
- Reset: vista + `mi = 200` + auto + palette `fuoco` + backend default +
  device CUDA 0 + adapter Vulkan 0 + f32 + scala `1x1`. Non tocca benchmark, `view_file`,
  cache hardware.

## 9. File I/O
| file | path | chiavi | note |
|---|---|---|---|
| zona (JSON indentato) | a scelta, default `mandelbrot_AAAAMMGG_HHMMSS.json` | `app, versione, cx, cy, half, mi, mi_auto` | `Carica` ripristina vista+MI (clamp `half`), rende file corrente (titolo). `Salva zona` voce disabilitata se nessun file corrente; `save_zone()` senza file ricade su `save_as`. PNG usa stesso pattern nome |
| config | `~/.mandelbrot/config.json` (v7.3.0; prima `~/mandelbrot/`) | `precision, palette, backend, cuda_device, cuda_split, cuda_split_ratio, vulkan_adapter, bench` | vista e `view_file` mai persistiti (vecchi valori ignorati); backend legacy `"gpu"` → default, ignoto → default; `cuda_device`/`vulkan_adapter` clampati al range (default 0), `cuda_split_ratio` clampato 0.1–0.9 (default 0.5); salvataggio all'uscita + throttle 1 s (`after(1000)`; `request_render` marca dirty anche per sola vista); v7.3.0: migrazione one-shot — se la nuova config non esiste e la vecchia sì, `config.json` viene copiata in `~/.mandelbrot/` (vecchia cartella non toccata, silenziosa) |

- Avvio sempre da default (insieme intero + MI auto); vista precedente solo via
  `Carica zona...`.

## 10. Benchmark
- Dialog conferma custom (`Toplevel` modale centrato, radio Standard/Esperta,
  Avvia/Annulla, `Return`/`Esc`); poi thread dedicato.
  Standard = 1 prova da `secs`; Esperta (v7.1.0) = 3 prove da `secs` di cui
  vale la migliore tra quelle completate (status `prova i/3...`); errore ->
  stop, il risultato resta il migliore ottenuto (dialog FALLITO solo se tutto
  fallito). GPU in esclusiva per tutta la durata (24 s in esperta).
- Default (in `config.json` chiave `bench`, overridibile; vecchia `bench.mi`
  ignorata):

| par | cx | cy | half | w x h | secs | mi |
|---|---|---|---|---|---|---|
| val | -0.7499302568795561 | -0.015139113925433963 | 5.226737155905588e-05 | 960x540 | 8.0 | sempre `auto_mi(half)` = 10915 |

- Metodo di misura (v7.2.0): su GPU (CUDA/Vulkan/Metal) il bench renderizza
  a 2x per lato (4x area: 960x540 -> 1920x1080; clamp 7680, fattore reale =
  `(bw*bh)/(w*h)`) e riporta rps = misurato x4: l'overhead fisso
  per-iterazione (Python, launch, sync) si ammortizza x4; kernel e copie
  restano nella misura (scalano col'area, come lavoro reale dell'app).
  `mi` invariato. La CPU resta 1x (a 4x area sarebbe troppo lenta).
  Pre-warmup boundato (<=50 rendering e <=0.7 s) PRIMA della prima finestra
  di misura: clock GPU, buffer pinned, kernel esatto — tutti fuori dalla
  misura. Le barre "storico" GPU (BENCH_REF) sono rimesurate col metodo 4x
   (v7.2.0: 415.0 / 306.5 / 188.5 / 134.0) e confrontabili con le nuove run;
   la barra CPU resta una misura diretta (la CPU non usa la diluizione).
- Timeout (v7.2.1): niente hang infinito del benchmark.
  1. Metal: `waitUntilCompleted()` bloccante sostituito da completed
     handler + `threading.Event` con deadline `METAL_TIMEOUT_S = 30` s per
     rendering (un render GPU sano dura <1 s); scaduto -> `RuntimeError`
     chiaro ("GPU bloccata?"); buffer fallita -> dettaglio `cmd.error()`.
     (I valori grezzi di `status()` non sono usati: l'enum varia tra versioni
     macOS, veda §6.3.)
  2. App: deadline per-rendering `render_dl()` nel `_bench_worker`
     (pre-warmup E finestra di misura): Metal `METAL_TIMEOUT_S + 5 = 35` s,
     CPU `BENCH_RENDER_LIMIT_S = 180` s (single-core numpy puo' essere
     davvero lento; 180 s distingue 'lento' da 'bloccato'). Sforato ->
     errore, bench FALLITO col dettaglio.
  3. Diagnosi: ogni fallimento (pre-warmup, misura, 0 rendering) appende
     nella cartella config (`~/.mandelbrot/bench.log` da v7.3.0; prima
     `~/mandelbrot/`), troncato a ~200 righe, con timestamp,
     versione, motore+precisione, hw, OS, Python, stato Numba, parametri
     bench e traceback. Silenzioso: un errore nel log non rompe il bench.
  4. `Help > Informazioni` mostra la riga `OS: <platform.platform()>`
     (facilita i report su hardware non collaudato, es. M5).
- Esegue nel modo corrente (motore + f32/f64 di toolbar, GPU/adapter
  selezionato) ma SEMPRE 1x1 fisso (960x540 da config, mai NxN: la scala
  persistente non tocca il bench; su GPU 2x per lato, vedi Metodo sopra);
  CUDA usa buffer proprio (allocato sul
   device scelto), Metal/Vulkan output proprio, CPU memoria numpy. Per gli
   8 s ha la GPU in esclusiva (worker in pausa E ricalcolo NxN sospeso, §7);
   bench sempre 1x1, usa lo split se attivo (v6.2.4, storici confrontabili
   a parita' di selezione); se il device e' occupato/in reset il dialog
   FALLITO lo segnala con hint (riprovare o riavviare).
- Report: dialog con `rendering/s` grande (42 pt, `#2ea44f`; su GPU i
  valori equivalenti a 1x dopo diluizione x4), statistiche (n. rendering,
  ms/render — su GPU anche n. equivalenti + ms equivalente; in esperta
  `migliore di 3 prove da 8 s`), codice
  di autenticità a 64 bit (16 hex in gruppi da 4, Entry copiabile + pulsante
  `Copia`), grafico +
  griglia parametri (riga `Metodo` 1x/4x + riga `Hardware` da `hw_name()` +
  riga `Modalità`);
  errore → `BENCHMARK FALLITO` + dettaglio (`#e5534b`).
- Valori bench in `config.json` clampati al load (`_load_bench`: secs 1–120,
  w/h 64–7680, half 1e-12–2, cx/cy ±2; malformato → default): niente piu'
  benchmark accorciati da valori spuri.
- Modalità (v7.2.0): la scelta standard/esperta del dialog è memorizzata in
  `config.json` (`bench_mode`, validata a load: solo standard/esperto,
  altrimenti standard) e ripristinata all'avvio come pre-selezione.
- Codice di autenticità (v7.1.0, `mandelbrot/expert.py`, tamper-evident NON
  anti-forgery: app open-source/offline, chi ricalcola il codice col
  programma modificato non e' rilevabile; il ritocco dello screenshot si'):
  24 bit rps (centesimi) + 16 bit impronta hw (SHA256 del nome normalizzato:
  minuscole, spazi compattati) + 24 bit checksum SHA256 su
  `rps|hw|backend|prec|versione|mi|secs`. Nessun segreto incorporato.
- Verifica (Help > `Verifica benchmark...`): dialog con codice + nome hw
  digitato (letto dallo screenshot) + resto dei campi (precompilati coi
  default); ricalcolo e verdetto `OK` / `HW DIVERSO` (nome errato o altra
  macchina) / `MANOMESSO` (checksum fallito) / `FORMATO INVALIDO`, con rps
  decodificato e dettaglio.

| barra | valore (rendering/s) | stile |
|---|---|---|
| `AMD 9900X (storico)` | 6.62 | grigia |
| `4070 Super Vulkan (storico)` | 134.0 | grigia |
| `5070 Ti Vulkan (storico)` | 188.5 | grigia |
| `4070 Super CUDA (storico)` | 306.5 | grigia |
| `5070 Ti CUDA (storico)` | 415.0 | grigia |
| `<backend()> — questa run` | misurato | verde (solo metodo, no hw) |

  Grafico `tk.Canvas` 590xN (margini 230,60,8,26; barre 20 px, gap 10 px;
  altezza N = 8 + n*20 + (n-1)*10 + 26 dal numero di barre):
  scala lineare 0→max, step nice 1/2/5×10^n, valore a fine barra.

## 11. Build dell'app (multipiattaforma)
One-dir self-contained, nessuna dipendenza per l'utente tranne driver/runtime
CUDA (solo per CUDA; Vulkan bundled).

| piattaforma | app (disco di sistema, temp) | distributivo (progetto `dist/`) | GPU |
|---|---|---|---|
| Windows | `mandelbrot_dist/Mandelbrot/Mandelbrot.exe` + `_internal/` | `dist/Mandelbrot-v<ver>-win64.zip` | Vulkan bundled; CUDA solo con driver+runtime utente (cuda-pathfinder: `CUDA_PATH`/PATH/Program Files) |
| macOS | `mandelbrot_dist/Mandelbrot.app` (firma ad-hoc) | `dist/Mandelbrot-v<ver>-macos.dmg` (staging `mandelbrot_dmg` + link `/Applications`, `hdiutil UDZO`) | Metal + Vulkan bundled |
- Primo avvio su macOS: firma ad-hoc non notarizzata → Gatekeeper blocca;
  tasto destro (Ctrl-clic) su `Mandelbrot.app` → Apri → Apri, oppure
  `xattr -cr /Applications/Mandelbrot.app` (nota anche nel README).

- Script unico `build_app.py`: icona → PyInstaller (`mandelbrot.spec`,
  `--workpath mandelbrot_build` + `--distpath mandelbrot_dist` in temp) →
  post (verifica + zip/dmg) → cleanup. Intermedie `build/` mai nel progetto
  (lento su share di rete).
- Ricetta `mandelbrot.spec`: entry `mandel.py` (shim) + pacchetto `mandelbrot/`
  seguito automaticamente da PyInstaller; comune `collect_all(numba/llvmlite)`,
  `PIL._tkinter_finder`, escluse `torch/matplotlib/IPython/pytest`;
  win32 `collect_all(cupy)` (solo moduli, no DLL CUDA) +
  `collect_all(wgpu/cffi)` + `hiddenimports wgpu.backends.wgpu_native`, icona
  `.ico`, versioninfo, hook `hook_dlldir.py` (`os.add_dll_directory` su EXE +
  `_internal`); darwin `collect_all(objc/Foundation/Metal)` +
  `collect_all(wgpu/cffi)`, `libomp.dylib` da `torch/lib`, `BUNDLE` (icona
  `.icns`, bundle_id, plist).
- Ritenzione: `KEEP_N = 3` versioni per piattaforma in `dist/` (zip e dmg
  separatamente; rimozione automatica delle più vecchie per numero di
  versione, anche a 2 parti es. `v6.0`).
- Icona `make_icon.py`: render 1024x1024 (CPU f64, `termal`, `mi = 800`,
  via `mandelbrot.cpu` dallo shim `mandel`) →
  `icon_src.png` + `mandelbrot.ico` (16,24,32,48,64,128,256) + su macOS
  `mandelbrot.icns` via Pillow. Versione distributivi dall'header `# VERSIONE`
  (§1). Processo/ritenzione/gitignore: vedi `AGENTS.md`.

## 12. Note tecniche (gotchas)
### 12.1 LUT come costanti
Passate come buffer device, CuPy le sovrascriveva con l'output → incorporate
nel sorgente (CUDA/Metal) o storage concatenato (Vulkan, §2).

### 12.2 FMA numpy (critica per bit-identità)
`np.square` su complessi è compilato con FMA (build SIMD-dipendente), Numba
senza fastmath no → 1 ULP su orbite caotiche, escape diverso su pochi pixel di
bordo. Regola: quadrato in parti esplicite (`a*a-b*b`, `2*a*b`, ufunc singoli)
→ f64 bit-identico. Eccezione f32: Numba/LLVM contrae in FMA in modo
contesto-dipendente (non riproducibile in numpy) → `it[]` uguale, al massimo 1
entry LUT; per questo il self-test f32 usa `it[]` esatto + `mag[]` entro 1e-2.

### 12.3 Associatività IEEE
Riordinare `*`/`/` cambia l'arrotondamento: `cx + half*X/s` non è bit-identico
a `cx + half*(X/s)`. Conservare l'ordine e verificare col gate multi-zona.

### 12.4 Memoria Numba + cancellazione
In `prange` la lettura cross-thread non è affidabile (hoisting) → check di
cancellazione solo a livello Python tra bande (§7). Inoltre: niente
`break`/`continue` in `prange` (uccide parallelismo); `cache=True` inutile
(modulo rinominato a ogni load; paga il warmup); nessun ufunc `fma` in Numba.

### 12.5 Gate di correttezza (soglie per coppia)
| coppia | criterio |
|---|---|
| CPU f64/f32 vs riferimento (`<zona>_cpu.npy` / `<zona>_cpu_f32.npy`) | bit-identico |
| CUDA f32/f64 vs `<zona>_gpu_f32/_gpu_f64.npy` | bit-identico |
| CPUf64 vs CUDAf64 | al massimo 2% pixel con diff > 8 (bordo + ULP libm-vs-CUDA) |
| Metal f32 | bit-identico a `<zona>_metal_f32.npy` + 2 run identiche + scarto vs CPUf32 entro `max(2%, 1.5x CPUf32-vs-CUDAf32)` (gold CUDAf32 = righello nube f32) |
| Deep-zoom f32 (Numba/CUDA/Metal/Vulkan) | 10–25% pixel di bordo divergono per FMA: atteso, non difetto |

### 12.6 Tkinter
`delete`/`insert` su `Entry` disabilitata sono no-op → `state="normal"` prima.
- Note operative (versionamento, GPU condivisa con llama.cpp, benchmark
  bounded/A-B, warmup clock dopo pause > 10 s): vedi `AGENTS.md`.
