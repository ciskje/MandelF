"""UI Tkinter: MandelbrotApp + main (pipeline, file, foto, benchmark)."""
import tkinter as tk
import tkinter.filedialog
import tkinter.messagebox
import tkinter.font as tkfont
import threading
import queue
import math
import os
import json
import time
import ctypes
import struct
import numpy as np
from PIL import Image, ImageTk
from . import state as S
from . import VERSION, HISTORY
from .config import (INIT_W, INIT_H, CX0, CY0, HALF0, MI0, MI_AUTO_BASE,
    MI_AUTO_MIN, MI_AUTO_MAX, MIN_HALF, MIN_DIM, PHOTO_HEADROOM,
    PHOTO_HOST_RESERVE, PHOTO_BACKSTOP_MPX, CONFIG_PATH, BENCH, BENCH_REF,
    BENCH_GPU_SCALE, METAL_TIMEOUT_S, BENCH_RENDER_LIMIT_S, BACKEND_FG, ERR_FG,
    auto_mi)
from .palette import PALETTES
from .state import apply_palette
from .mem import _photo_mem_ok
from .engine import (backend, hw_name, compute, compute_gpu, _backend_ok,
    _default_backend, _backend_fg, _gpu_supports_f64, set_prec,
    _warmup_cuda_device, _warmup_vulkan_adapter)
from .cuda import (set_cuda_device, set_cuda_split, _cuda_split_devs,
    _cuda_split_ready, _cuda_calibrate_split, _cuda_split_diag,
    _cuda_short_name, _cuda_label)
from .vulkan import set_vulkan_adapter, _vulkan_short_name, _vulkan_label
from .cpu import _numba_diag
from .expert import make_code, verify_code, fmt_code

class MandelbrotApp:
    # ---------------- Costruzione UI ----------------
    def __init__(self, root):
        self.root = root
        self._setup_fonts()
        self.cx, self.cy, self.half = CX0, CY0, HALF0
        self.mi = MI0
        self.mi_auto = True
        self.view_file = None
        self.bench = dict(BENCH)
        self._build_toolbar()
        self._build_canvas_status()
        self._build_menu()
        self._bind_events()
        self._start_pipeline()
        self._refresh_title()
        if self.load_config():
            self.request_render("config caricata")
        else:
            self.request_render("iniziale")

    def _setup_fonts(self):
        # UI piu leggibile: ingrandisce il font default di tutti i widget
        # (checkbutton, etichette, pulsanti, menu) preservando la famiglia nativa
        for _fn in ("TkDefaultFont", "TkTextFont", "TkMenuFont"):
            try:
                tkfont.nametofont(_fn).config(size=13)
            except Exception:
                pass

    def _build_toolbar(self):
        self.canvas = tk.Canvas(self.root, width=INIT_W, height=INIT_H, bg="black", highlightthickness=0)
        # --- barra comandi in alto ---
        self.ctl = tk.Frame(self.root)
        self.ctl.pack(fill="x")
        bk = tk.Frame(self.ctl)
        bk.pack(side="left", padx=(8, 12))
        tk.Label(bk, text="Motore:").pack(side="left")
        # v5.6.0: 4 radio (CPU/CUDA/Metal/Vulkan) nel MESSIMO frame = gruppo
        # radio unico (Tkinter li raggruppa da solo, senza 'variable'). I
        # backend non disponibili restano visibili ma DISABILITATI (grigi).
        self.backend_btns = {}
        for _be in ("cpu", "cuda", "metal", "vulkan"):
            _b = tk.Radiobutton(bk, text=_be.capitalize(), value=_be,
                               command=lambda b=_be: self.set_backend(b))
            _b.pack(side="left", padx=2, pady=3)
            self.backend_btns[_be] = _b
        for _be, _b in self.backend_btns.items():
            if not _backend_ok(_be):
                _b.config(state="disabled")
        self.backend_btns[S._ACTIVE].select()
        # v5.9.0/v5.9.2: dropdown scelta GPU: contenuto per motore attivo
        # (device CUDA / adapter Vulkan), visibile solo se > 1.
        # v6.2.5: nella seconda riga (self.btns), creato dopo.
        self.gpu_var = None
        self.gpu_menu = None
        self.gpu_cal_btn = None
        pl = tk.Frame(self.ctl)
        pl.pack(side="left", padx=12)
        tk.Label(pl, text="Palette:").pack(side="left")
        # Pulsanti generati dal registro PALETTES (ordine = indice kernel).
        self.pal_btns = {}
        for _name in PALETTES:
            _b = tk.Checkbutton(pl, text=_name.capitalize(),
                                command=lambda n=_name: self.choose_palette(n))
            _b.pack(side="left", padx=2, pady=3)
            self.pal_btns[_name] = _b
        self.pal_btns["fuoco"].select()
        pc = tk.Frame(self.ctl)
        pc.pack(side="left", padx=12)
        tk.Label(pc, text="Precisione:").pack(side="left")
        self.f32_btn = tk.Checkbutton(pc, text="f32", command=lambda: self.set_precision("f32"))
        self.f32_btn.pack(side="left", padx=2, pady=3)
        self.f64_btn = tk.Checkbutton(pc, text="f64", command=lambda: self.set_precision("f64"))
        self.f64_btn.pack(side="left", padx=2, pady=3)
        self.f32_btn.select()
        # v5.4.0: la disponibilita' di f64 dipende dallo slot GPU corrente
        # (Metal e' f32-only; CUDA senza kernel f64). Stato dinamico: si
        # aggiorna a ogni cambio di motore (vedi _sync_precision_buttons).
        self._sync_precision_buttons()
        mif = tk.Frame(self.ctl)
        mif.pack(side="left", padx=12)
        tk.Label(mif, text="Iter:").pack(side="left")
        self.mi_auto_var = tk.BooleanVar(value=True)
        self.auto_btn = tk.Checkbutton(mif, text="Auto", variable=self.mi_auto_var,
                                        command=self.toggle_auto_mi)
        self.auto_btn.pack(side="left", padx=2, pady=3)
        self.btns = tk.Frame(self.root)
        self.btns.pack(fill="x")
        self.mi_caption = tk.Label(self.btns, text="Iterazioni:")
        self.mi_caption.pack(side="left", padx=(8, 2), pady=3)
        self.mi_entry = tk.Entry(self.btns, width=7, justify="right")
        self.mi_entry.pack(side="left", padx=2, pady=3)
        self.mi_entry.bind("<Return>", self._commit_mi_entry)
        self.mi_entry.bind("<FocusOut>", self._commit_mi_entry)
        self._update_mi_label()
        self.mi_minus = tk.Button(self.btns, text="-1000", command=lambda: self.change_mi(-1000))
        self.mi_minus.pack(side="left", padx=2, pady=3)
        self.mi_plus = tk.Button(self.btns, text="+1000", command=lambda: self.change_mi(+1000))
        self.mi_plus.pack(side="left", padx=2, pady=3)
        self.mi_minus.config(state="disabled")
        self.mi_plus.config(state="disabled")
        self.bench_btn = tk.Button(self.btns, text="Benchmark", command=self.run_benchmark)
        self.bench_btn.pack(side="right", padx=(16, 8), pady=3)
        self.reset_btn = tk.Button(self.btns, text="Reset", command=self.reset)
        self.reset_btn.pack(side="right", padx=2, pady=3)
        # v6.2.5: GPU selettore nella seconda riga (a destra, prima di Benchmark).
        self.gpu_frame = tk.Frame(self.btns)
        tk.Label(self.gpu_frame, text="GPU:").pack(side="left")
        self._refresh_gpu_menu()
        # v5.11.0: scala esplicita sempre visibile (1x1 = vista corrente,
        # NxN = antialiasing NxN). Sostituisce i 3 pulsanti separati
        # (Ricalcola + Ricalcola 2x2/4x4): un solo pulsante + dropdown.
        # v6.0: dropdown impacchettato PRIMA del pulsante cosi' a video
        # sta [Ricalcola][NxN] (pack side=right impila da destra); default
        # 1x1 (interattivo leggero); la selezione ricalcola subito (trace)
        # e resta persistente: tutti i calcoli successivi usano quella scala.
        self.recalc_var = tk.StringVar(value="1x1")
        self.recalc_menu = tk.OptionMenu(self.btns, self.recalc_var,
                                         "1x1", "2x2", "4x4", "8x8")
        self.recalc_menu.pack(side="right", padx=2, pady=3)
        self.recalc_btn = tk.Button(self.btns, text="Ricalcola", command=self.recalc_scaled)
        self.recalc_btn.pack(side="right", padx=2, pady=3)
        self.recalc_var.trace_add("write", lambda *_: self.recalc_scaled())

    def _build_canvas_status(self):
        # --- barra accento, canvas al centro, status in fondo ---
        self.accent = tk.Frame(self.root, height=3, bg=_backend_fg())
        self.accent.pack(fill="x")
        self.canvas.pack(fill="both", expand=True)
        self.status = tk.Label(self.root, text="render...")
        self.status.pack(fill="x")

    def _build_menu(self):
        self.menu = tk.Menu(self.root)
        self.mfile = tk.Menu(self.menu, tearoff=0)
        self.mfile.add_command(label="Salva immagine... (Ctrl+S)", command=self.save_png)
        self.mfile.add_command(label="Carica zona...", command=self.load_zone_as)
        self.mfile.add_command(label="Salva zona", command=self.save_zone)
        self.save_zone_entry = self.mfile.index(tk.END)
        self.mfile.add_command(label="Salva zona con nome...", command=self.save_zone_as)
        self.mfile.add_separator()
        self.mfile.add_command(label="Esci", command=self.on_exit)
        self.menu.add_cascade(label="File", menu=self.mfile)
        self.mhelp = tk.Menu(self.menu, tearoff=0)
        self.mhelp.add_command(label="Istruzioni...", command=self.show_help)
        self.mhelp.add_command(label="Novità recenti...", command=self.show_recent)
        self.mhelp.add_command(label="Verifica benchmark...", command=self.show_verify)
        self.mhelp.add_separator()
        self.mhelp.add_command(label="Informazioni...", command=self.show_about)
        self.menu.add_cascade(label="Help", menu=self.mhelp)
        self.root.config(menu=self.menu)
        self.root.protocol("WM_DELETE_WINDOW", self.on_exit)
        self._update_save_zone_state()

    def _update_save_zone_state(self):
        # "Salva zona" e' disponibile solo se esiste gia' un nome/file di zona
        # (view_file); altrimenti va prima definito con "Salva zona con nome..."
        # o con "Carica zona...".
        state = "normal" if self.view_file else "disabled"
        self.mfile.entryconfig(self.save_zone_entry, state=state)

    def show_help(self):
        # Guida rapida (menu Help -> Istruzioni...).
        win = tk.Toplevel(self.root)
        win.title("Istruzioni")
        win.resizable(False, False)
        body = tk.Frame(win, padx=26, pady=20)
        body.pack(fill="both", expand=True)
        tk.Label(body, text="Insieme di Mandelbrot \u2014 istruzioni",
                 font=("Segoe UI", 14, "bold"),
                 foreground=_backend_fg()).pack(anchor="w")
        text = (
            "Navigazione: rotella per zoomare al cursore (x1.25 / x0.8), "
            "click per zoom x2 al cursore, click destro (due dita sul trackpad) "
            "per zoom x0.5 al cursore, trascinamento per spostare la vista, "
            "tasti + / - per zoom x2 / x0.5 al centro (funzionano sempre, "
            "tranne mentre si scrive nelle caselle), r per il reset.\n\n"
            "Motore e precisione: toolbar Motore (CPU / CUDA / Metal / Vulkan) "
            "e Precisione (f32 / f64); i backend non disponibili restano grigi. "
            "Con piu' GPU, il dropdown GPU sceglie device/adapter del motore, "
            "o Entrambe per spartire il render sulle 2 CUDA (rapporto "
            "auto-calibrato; il benchmark resta sulla singola selezionata). "
            "CUDA richiede driver NVIDIA + runtime; Vulkan funziona out-of-the-box.\n\n"
            "Iterazioni: Auto calcola mi dallo zoom; in manuale si imposta il "
            "valore (Invio) o lo si cambia di \u00b11000.\n\n"
            "Palette, file, foto e benchmark: palette dal registro (default fuoco); "
            "menu File per salvare PNG (Ctrl+S) e zone JSON; Ricalcola + scala "
            "1x1 (default, vista corrente) / 2x2 / 4x4 / 8x8: la selezione "
            "ricalcola subito e resta attiva (ogni vista successiva e' "
            "calcolata in quel modo); NxN ricalcola a N volte per lato con "
            "antialiasing (media NxN; 4x4 = 16x pixel, 8x8 = 64x pixel, "
            "molto piu' lenti; se VRAM/RAM libere non bastano vengono "
            "rifiutati); "
            "Benchmark esegue "
            "il test standardizzato di 8 s nella vista corrente (Standard), "
            "oppure 3 prove da 8 s di cui vale la migliore (Esperta). Su GPU "
            "il test renderizza a 4x area (2x per lato) e riporta il "
            "risultato normalizzato x4: diluisce l'overhead fisso per "
            "iterazione; la CPU resta 1x. "
            "Il risultato mostra un codice di autenticità a 64 bit che lega "
            "rendering/s + hardware + resto dei campi: smaschera il ritocco "
            "dello screenshot (Help > Verifica benchmark... per "
            "controllarlo)."
        )
        tk.Label(body, text=text, wraplength=460, justify="left").pack(anchor="w", pady=(8, 0))
        def chiudi(_e=None):
            win._annullato = False
            win.destroy()
        tk.Button(win, text="Chiudi", command=chiudi).pack(pady=(18, 20))
        win.bind("<Return>", chiudi)
        self._modal(win)

    def show_recent(self):
        # Ultime 10 modifiche (menu Help -> Novità recenti...), da HISTORY.
        win = tk.Toplevel(self.root)
        win.title("Novità recenti")
        win.resizable(False, False)
        body = tk.Frame(win, padx=26, pady=20)
        body.pack(fill="both", expand=True)
        tk.Label(body, text="Ultime modifiche",
                 font=("Segoe UI", 14, "bold"),
                 foreground=_backend_fg()).pack(anchor="w")
        rows = tk.Frame(body)
        rows.pack(fill="x", pady=(8, 0))
        for i, (ver, date, desc) in enumerate(HISTORY[:10]):
            tk.Label(rows, text=f"v{ver} ({date})", width=18, anchor="w",
                     font=("Consolas", 11, "bold")).grid(row=i, column=0, sticky="nw", pady=2)
            tk.Label(rows, text=desc, wraplength=340, justify="left",
                     anchor="w").grid(row=i, column=1, sticky="nw", pady=2)
        def chiudi(_e=None):
            win._annullato = False
            win.destroy()
        tk.Button(win, text="Chiudi", command=chiudi).pack(pady=(18, 20))
        win.bind("<Return>", chiudi)
        self._modal(win)

    def show_about(self):
        # Scheda autore/versione (menu Help -> Informazioni...).
        win = tk.Toplevel(self.root)
        win.title("Informazioni")
        win.resizable(False, False)
        body = tk.Frame(win, padx=26, pady=20)
        body.pack(fill="both", expand=True)
        tk.Label(body, text="Insieme di Mandelbrot",
                 font=("Segoe UI", 14, "bold"),
                 foreground=_backend_fg()).pack(anchor="w")
        tk.Label(body, text=f"Versione {VERSION}").pack(anchor="w", pady=(6, 0))
        tk.Label(body, text=f"{backend()} ({hw_name()})").pack(anchor="w")
        # v7.2.1: riga OS (facilita i report su hardware non collaudati).
        import platform
        tk.Label(body, text="OS: %s" % platform.platform()).pack(anchor="w")
        tk.Label(body, text="Autore: Francesco Ferrara").pack(anchor="w", pady=(10, 0))
        tk.Label(body, text="Email: occhiobello@gmail.com").pack(anchor="w")
        # v5.9.5: diagnostica CPU/Numba (perche' single-core?).
        tk.Label(body, text="CPU: " + _numba_diag(), wraplength=460,
                 justify="left").pack(anchor="w", pady=(10, 0))
        # v6.2.1: diagnostica split CUDA (rapporto + esito parity).
        if S._CUDA_OK and len(S._CUDA_DEVICES) >= 2:
            tk.Label(body, text="Split: " + _cuda_split_diag(), wraplength=460,
                     justify="left").pack(anchor="w")
        def chiudi(_e=None):
            win._annullato = False
            win.destroy()
        tk.Button(win, text="Chiudi", command=chiudi).pack(pady=(18, 20))
        win.bind("<Return>", chiudi)
        self._modal(win)

    def show_verify(self):
        # v7.1.0: verifica del codice di autenticità di un benchmark (Help ->
        # Verifica benchmark...). L'utente digita codice + nome hardware letto
        # dallo screenshot (+ resto dei campi, precompilati coi default): il
        # codice viene ricalcolato e confrontato. Solo decodifica+confronto,
        # niente rete. Limite onesto: non ferma chi ricalcola il codice col
        # programma modificato (open-source, offline), ma smaschera qualunque
        # ritocco delle cifre nello screenshot.
        win = tk.Toplevel(self.root)
        win.title("Verifica benchmark")
        win.resizable(False, False)
        body = tk.Frame(win, padx=26, pady=20)
        body.pack(fill="both", expand=True)
        tk.Label(body, text="Verifica codice di autenticità",
                 font=("Segoe UI", 14, "bold"),
                 foreground=_backend_fg()).pack(anchor="w")
        tk.Label(body, text="Digita codice e dati letti dallo screenshot:",
                 wraplength=460, justify="left").pack(anchor="w", pady=(6, 10))
        b = self.bench
        fields = {}
        grid = tk.Frame(body)
        grid.pack(fill="x")
        def _row(i, label, default, width=34):
            tk.Label(grid, text=label, width=16, anchor="w").grid(
                row=i, column=0, sticky="w", pady=3)
            e = tk.Entry(grid, width=width, font=("Consolas", 11))
            e.insert(0, default)
            e.grid(row=i, column=1, sticky="w", padx=(10, 0), pady=3)
            fields[label] = e
        _row(0, "Codice", "")
        _row(1, "Hardware", hw_name(), width=34)
        _row(2, "Motore", backend())
        _row(3, "Precisione", S._PREC, width=10)
        _row(4, "Versione", VERSION, width=10)
        _row(5, "Iterazioni", str(auto_mi(b["half"])), width=10)
        _row(6, "Secondi", str(float(b["secs"])), width=10)
        out = tk.Label(body, text="", wraplength=460, justify="left",
                       font=("Segoe UI", 12, "bold"))
        out.pack(anchor="w", pady=(14, 0))
        det = tk.Label(body, text="", wraplength=460, justify="left")
        det.pack(anchor="w")
        def verifica(_e=None):
            v, rps, msg = verify_code(fields["Codice"].get(),
                                      fields["Hardware"].get(),
                                      fields["Motore"].get(),
                                      fields["Precisione"].get(),
                                      fields["Versione"].get(),
                                      fields["Iterazioni"].get(),
                                      fields["Secondi"].get())
            col = {"OK": "#2ea44f", "HW DIVERSO": "#d97706",
                   "MANOMESSO": "#e5534b",
                   "FORMATO INVALIDO": "#e5534b"}.get(v, "#e5534b")
            if rps is None:
                out.config(text=v, foreground=col)
            else:
                out.config(text=f"{v} \u2014 {rps:.2f} rendering/s", foreground=col)
            det.config(text=msg)
        btns = tk.Frame(win)
        btns.pack(fill="x", padx=26, pady=(16, 20))
        tk.Button(btns, text="Verifica", command=verifica).pack(side="right")
        tk.Button(btns, text="Chiudi",
                  command=lambda: (setattr(win, "_annullato", False),
                                   win.destroy())).pack(side="right", padx=(0, 10))
        win.bind("<Return>", verifica)
        self._modal(win)

    def _bind_events(self):
        self.press_pos = None
        self.dragged = False
        self._size = (0, 0)
        self.root.bind("<Control-s>", lambda e: self.save_png())
        # v5.9.8: tasti zoom/reset legati alla FINESTRA (non al canvas):
        # prima valevano solo col focus sul canvas, dopo un click su
        # bottoni/caselle sembravano morti (tipico su macOS). La guardia
        # in _key_zoom evita di rubare i tasti mentre si scrive in entry.
        self.root.bind("<Key-r>", lambda e: self._key_reset())
        self.root.bind("<Key-plus>", lambda e: self._key_zoom(2.0))
        self.root.bind("<Key-minus>", lambda e: self._key_zoom(0.5))
        self.canvas.bind("<ButtonPress-1>", self.on_press)
        self.canvas.bind("<B1-Motion>", self.on_drag)
        self.canvas.bind("<ButtonRelease-1>", self.on_release)
        # v5.9.8: click destro (due dita sul trackpad) = zoom x0.5 al
        # cursore: alternativa alla rotella che non dipende dagli eventi
        # MouseWheel. Bind su 2 e 3 (mapping destro su macOS/X11).
        self.canvas.bind("<ButtonPress-2>",
                         lambda e: self.zoom_at(*self.p2c(e.x, e.y), 0.5))
        self.canvas.bind("<ButtonPress-3>",
                         lambda e: self.zoom_at(*self.p2c(e.x, e.y), 0.5))
        self.canvas.bind("<MouseWheel>", self.on_wheel)
        self.canvas.bind("<Configure>", self.on_configure)

    def _key_zoom(self, f):
        # Zoom da tastiera: ignorato solo mentre il focus e' su una Entry
        # (l'utente sta digitando, es. '-' nelle iterazioni).
        try:
            if isinstance(self.root.focus_get(), tk.Entry):
                return
        except Exception:
            pass
        self.zoom_center(f)

    def _key_reset(self):
        try:
            if isinstance(self.root.focus_get(), tk.Entry):
                return
        except Exception:
            pass
        self.reset()

    # ---------------- Helper UI ----------------
    def _refresh_title(self):
        t = f"Insieme di Mandelbrot v{VERSION} - {backend()} ({hw_name()})"
        if self.view_file:
            t += " - " + os.path.basename(self.view_file)
        self.root.title(t)

    def _select_palette(self, name):
        apply_palette(name)
        name = S._PALETTE
        for b in self.pal_btns.values():
            b.deselect()
        self.pal_btns[name].select()

    def _select_backend(self, be):
        # v5.6.0: 4 backend (cpu/cuda/metal/vulkan); selezionabile solo se
        # disponibile all'avvio (gli altri restano grigi/disabilitati).
        if not _backend_ok(be):
            return False
        S._ACTIVE = be
        for b in self.backend_btns.values():
            b.deselect()
        self.backend_btns[be].select()
        if getattr(self, "accent", None) is not None:
            self.accent.config(bg=_backend_fg())
        return True

    def _select_precision(self, p):
        if not set_prec(p):
            return False
        self.f32_btn.deselect()
        self.f64_btn.deselect()
        (self.f32_btn if p == "f32" else self.f64_btn).select()
        return True

    def _sync_precision_buttons(self):
        # v5.4.0: la disponibilita' di f64 dipende dallo slot GPU corrente:
        # Metal e' f32-only, CUDA senza kernel f64 non fa f64. Con CPU f32/f64
        # sono sempre selezionabili. Se la precisione corrente non e' piu'
        # disponibile si torna a f32; il bottone selezionato riflette S._PREC.
        f64_ok = (S._ACTIVE == "cpu") or _gpu_supports_f64()
        if not f64_ok and S._PREC == "f64":
            set_prec("f32")
        self.f64_btn.config(state="normal" if f64_ok else "disabled")
        self.f32_btn.deselect()
        self.f64_btn.deselect()
        (self.f32_btn if S._PREC == "f32" else self.f64_btn).select()

    def _update_mi_label(self):
        self.mi_caption.config(text="Iterazioni (auto):" if self.mi_auto else "Iterazioni:")
        # ATTENZIONE: in Tkinter delete/insert su Entry disabilitato sono no-op
        # silenziosi -> riabilitare PRIMA di scrivere, poi disabilitare di nuovo
        # (il widget arriva gia' disabilitato dalla chiamata precedente).
        self.mi_entry.config(state="normal")
        self.mi_entry.delete(0, "end")
        self.mi_entry.insert(0, str(self.eff_mi()))
        if self.mi_auto:
            self.mi_entry.config(state="disabled")

    # ---------------- Vista e interazione (geometria + mouse/tastiera) ----------------
    def canvas_size(self):
        w = self.canvas.winfo_width()
        h = self.canvas.winfo_height()
        if w < MIN_DIM or h < MIN_DIM:
            w, h = INIT_W, INIT_H
        return w, h

    def p2c(self, px, py):
        w, h = self.canvas_size()
        return (self.cx + (px - w / 2) / (w / 2) * self.half,
                self.cy + (py - h / 2) / (h / 2) * self.half * (h / w))

    def zoom_at(self, ux, uy, f):
        self.cx += (ux - self.cx) * (1 - 1 / f)
        self.cy += (uy - self.cy) * (1 - 1 / f)
        self.half = max(self.half / f, MIN_HALF)
        self.request_render("zoom")

    def zoom_center(self, f):
        self.zoom_at(self.cx, self.cy, f)

    def on_press(self, e):
        try:
            self.canvas.focus_set()
        except Exception:
            pass
        self.press_pos = (e.x, e.y)
        self.dragged = False

    def on_drag(self, e):
        if self.press_pos is None:
            return
        x0, y0 = self.press_pos
        if abs(e.x - x0) < 4 and abs(e.y - y0) < 4:
            return
        w, h = self.canvas_size()
        self.cx -= (e.x - x0) / (w / 2) * self.half
        self.cy -= (e.y - y0) / (h / 2) * self.half * (h / w)
        self.press_pos = (e.x, e.y)
        self.dragged = True
        self.request_render("pan")

    def on_release(self, e):
        if self.press_pos is not None and not self.dragged:
            self.zoom_at(*self.p2c(e.x, e.y), 2.0)
        self.press_pos = None

    def on_wheel(self, e):
        f = 1.25 if e.delta > 0 else 0.8
        self.zoom_at(*self.p2c(e.x, e.y), f)

    def on_configure(self, e):
        if (e.width, e.height) == self._size:
            return
        self._size = (e.width, e.height)
        if e.width < MIN_DIM or e.height < MIN_DIM:
            return
        self.request_render("ridimensionata")

    # ---------------- Controlli (MI, motore, palette, precisione, reset) ----------------
    def eff_mi(self):
        if not self.mi_auto:
            return self.mi
        return auto_mi(self.half)

    def toggle_auto_mi(self):
        new_auto = self.mi_auto_var.get()
        if new_auto:
            self.mi_auto = True
        else:
            # Disattivando l'auto: congela self.mi sul valore auto corrente
            # (eff_mi calcolato con mi_auto ancora True), invece di mostrare
            # il valore fisso iniziale (stale).
            self.mi = self.eff_mi()
            self.mi_auto = False
        st = "disabled" if self.mi_auto else "normal"
        self.mi_minus.config(state=st)
        self.mi_plus.config(state=st)
        self._update_mi_label()
        self.request_render("MI: auto" if self.mi_auto else "MI: fissi")

    def change_mi(self, d):
        self.mi = max(50, self.mi + d)
        self._update_mi_label()
        self.request_render("iterazioni modificate")

    def _commit_mi_entry(self, e=None):
        if self.mi_auto:
            return
        txt = self.mi_entry.get().strip()
        try:
            v = int(txt)
        except ValueError:
            v = 0
        if v < 50 or v > 100000:
            self.status.config(text="MI invalidi: intero tra 50 e 100000",
                                   foreground=ERR_FG)
            self.mi_entry.delete(0, "end")
            self.mi_entry.insert(0, str(self.mi))
            return
        if v == self.mi:
            return
        self.mi = v
        self.request_render("iterazioni modificate")

    def set_backend(self, b):
        if not self._select_backend(b):
            return
        # v7.1.4: il cambio motore riporta l'antialias a 1x1 (via trace
        # parte un ricalcolo intermedio, poi collassa sul request_render
        # finale come nel reset).
        if self._recalc_n() != 1:
            self.recalc_var.set("1x1")
        self._refresh_gpu_menu()
        self._sync_precision_buttons()
        self._refresh_title()
        self.request_render("motore: " + b.upper())

    def _gpu_labels(self):
        # v5.9.2: voci del dropdown per il motore attivo (vuoto = nascosto).
        # v6.2: con 2+ CUDA c'e' anche "Entrambe (split)" con le quote
        # calibrate (dropdown gpu1/gpu2/both).
        if S._ACTIVE == "cuda" and len(S._CUDA_DEVICES) > 1:
            labs = [_cuda_label(i) for i in range(len(S._CUDA_DEVICES))]
            labs.append("Entrambe (split)")
            return labs
        if S._ACTIVE == "vulkan" and len(S._VULKAN_ADAPTERS) > 1:
            return [_vulkan_label(i) for i in range(len(S._VULKAN_ADAPTERS))]
        return []

    def _gpu_pos(self, labels):
        if S._ACTIVE == "cuda" and S._CUDA_SPLIT_ON and labels:
            return len(labels) - 1
        return S._VULKAN_DEV if S._ACTIVE == "vulkan" else S._CUDA_DEV

    def _refresh_gpu_menu(self):
        # v5.9.2: ricostruisce il dropdown per il motore attivo (set_backend,
        # load config, reset); nascosto se il motore ha <= 1 GPU.
        if self.gpu_menu is not None:
            self.gpu_menu.destroy()
            self.gpu_menu = None
            self.gpu_var = None
        # v6.2.5: rimuovi eventuali widget split (li ricreo se servono).
        if getattr(self, "gpu_cal_btn", None) is not None:
            self.gpu_cal_btn.destroy()
            self.gpu_cal_btn = None
        if getattr(self, "gpu_ratio_menu", None) is not None:
            self.gpu_ratio_menu.destroy()
            self.gpu_ratio_menu = None
        if getattr(self, "gpu_ratio_lbl", None) is not None:
            self.gpu_ratio_lbl.destroy()
            self.gpu_ratio_lbl = None
        if getattr(self, "gpu_ratio_var", None) is not None:
            self.gpu_ratio_var = None
        labels = self._gpu_labels()
        if not labels:
            self.gpu_frame.pack_forget()
            return
        self.gpu_frame.pack(side="left", padx=(10, 0))
        self.gpu_var = tk.StringVar(value=labels[self._gpu_pos(labels)])
        self.gpu_menu = tk.OptionMenu(self.gpu_frame, self.gpu_var, *labels,
                                      command=self.choose_gpu_device)
        self.gpu_menu.pack(side="left", padx=2, pady=3)
        # Bottone ricampiona le GPU + dropdown ratio + label ratio (solo CUDA + 2+).
        # v6.2.5: visibili solo quando "Entrambe" e' selezionata.
        if S._ACTIVE == "cuda" and len(S._CUDA_DEVICES) >= 2:
            self.gpu_ratio_var = tk.StringVar(
                value=self._ratio_label())
            self.gpu_ratio_menu = tk.OptionMenu(
                self.gpu_frame, self.gpu_ratio_var,
                "33/66", "40/60", "50/50", "60/40", "66/33",
                command=self._set_split_ratio)
            self.gpu_ratio_lbl = tk.Label(self.gpu_frame, text=self._ratio_pct())
            self.gpu_cal_btn = tk.Button(
                self.gpu_frame, text="\u21bb", width=2,
                command=self._recalibrate_split)
            self._sync_split_widgets()

    def _ratio_pct(self):
        """Stringa percentuale corrente es. '33/66'."""
        r = min(0.9, max(0.1, S._CUDA_SPLIT_RATIO))
        p1 = round(r * 100)
        return "%d/%d" % (p1, 100 - p1)

    def _update_ratio_lbl(self):
        if getattr(self, "gpu_ratio_lbl", None) is not None:
            self.gpu_ratio_lbl.config(text=self._ratio_pct())

    def _ratio_label(self):
        """Label corrente dal S._CUDA_SPLIT_RATIO."""
        p = round(S._CUDA_SPLIT_RATIO * 100)
        return "%d/%d" % (p, 100 - p)

    def _set_split_ratio(self, label):
        """Imposta il rapporto split dal dropdown."""
        _p = int(label.split("/")[0])
        S._CUDA_SPLIT_RATIO = _p / 100.0
        self._update_ratio_lbl()
        self._refresh_title()
        self.request_render("split ratio: " + label)

    def _recalibrate_split(self):
        """Ricalibra il rapporto split + parity (background)."""
        if S._CUDA_SPLIT_CALIBRATING:
            self.status.config(text="calibrazione gia' in corso...")
            return
        self.status.config(text="ricalibrating split...")
        threading.Thread(target=self._recalibrate_split_worker,
                         daemon=True).start()

    def _recalibrate_split_worker(self):
        _cuda_calibrate_split()
        def _ui():
            self.status.config(text="split: " + _cuda_split_diag())
            self._update_ratio_lbl()
            self._refresh_title()
            self._sync_gpu_menu()
            self.request_render("split ricalibrato")
        self.root.after(0, _ui)

    def _sync_gpu_menu(self):
        # v5.9.0: allinea il valore senza ricostruire (dopo una selezione).
        labels = self._gpu_labels()
        if self.gpu_var is not None and labels:
            self.gpu_var.set(labels[self._gpu_pos(labels)])

    def _select_cuda_device(self, i):
        if not set_cuda_device(i):
            return False
        self._sync_gpu_menu()
        return True

    def _select_vulkan_adapter(self, i):
        # v5.9.2: come sopra per l'adapter Vulkan.
        if not set_vulkan_adapter(i):
            return False
        self._sync_gpu_menu()
        return True

    def choose_gpu_device(self, label):
        # v5.9.0/v5.9.2: cambio GPU dal dropdown ("<id>: <nome>").
        # v6.2: "Entrambe (split...)" attiva lo split sulle prime 2 CUDA
        # (il benchmark resta comunque sul device singolo S._CUDA_DEV).
        if S._ACTIVE == "cuda" and str(label).startswith("Entrambe"):
            # v6.2.1: se la parity ha bocciato lo split, rifiuto esplicito.
            if S._CUDA_SPLIT_PARITY_OK is False:
                self._sync_gpu_menu()
                self.status.config(
                    text=("split disabilitato: parity fallita (diff %.1f%%%s)"
                          % (S._CUDA_SPLIT_PARITY_DIFF * 100.0,
                             S._CUDA_SPLIT_PARITY_INFO)),
                    foreground=ERR_FG)
                return
            if set_cuda_split(True):
                self._refresh_title()
                self._sync_gpu_menu()
                self._sync_split_widgets()
                self.request_render("gpu: entrambe (split)")
            else:
                self._sync_gpu_menu()
            return
        try:
            _id = int(str(label).split(":", 1)[0])
        except (TypeError, ValueError):
            self._sync_gpu_menu()
            return
        if S._ACTIVE == "vulkan":
            if 0 <= _id < len(S._VULKAN_ADAPTERS) and _id != S._VULKAN_DEV:
                self._select_vulkan_adapter(_id)
                self._refresh_title()
                threading.Thread(target=_warmup_vulkan_adapter,
                                 daemon=True).start()
                self.request_render("gpu: " + _vulkan_short_name(
                    S._VULKAN_ADAPTERS[S._VULKAN_DEV][1]))
            else:
                self._sync_gpu_menu()
            return
        _pos = next((k for k, (d, _n) in enumerate(S._CUDA_DEVICES) if d == _id),
                    None)
        # v7.1.2: con lo split attivo anche il "ritorno" sullo stesso device
        # e' un cambio (lo split non tocca S._CUDA_DEV: senza questo check
        # riselezionare la gpu di partenza veniva ignorato e il menu tornava
        # su "Entrambe").
        if _pos is None or (_pos == S._CUDA_DEV and not S._CUDA_SPLIT_ON):
            self._sync_gpu_menu()
            return
        self._select_cuda_device(_pos)
        self._refresh_title()
        self._sync_split_widgets()
        threading.Thread(target=_warmup_cuda_device, daemon=True).start()
        self.request_render("gpu: " + _cuda_short_name(S._CUDA_DEVICES[S._CUDA_DEV][1]))

    def _sync_split_widgets(self):
        """Mostra/nasconde ratio+calibra in base a 'Entrambe' selezionata."""
        _split = S._ACTIVE == "cuda" and len(S._CUDA_DEVICES) >= 2 and S._CUDA_SPLIT_ON
        if getattr(self, "gpu_ratio_menu", None) is not None:
            if _split:
                self.gpu_ratio_menu.pack(side="left", padx=(4, 0), pady=3)
                self.gpu_ratio_lbl.pack(side="left", padx=(4, 0), pady=3)
                self.gpu_cal_btn.pack(side="left", padx=(2, 0), pady=3)
            else:
                self.gpu_ratio_menu.pack_forget()
                self.gpu_ratio_lbl.pack_forget()
                self.gpu_cal_btn.pack_forget()

    def choose_palette(self, name):
        self._select_palette(name)
        self.request_render("palette: " + S._PALETTE)

    def set_precision(self, p):
        if not self._select_precision(p):
            return
        self._refresh_title()
        self.request_render("precisione: " + p)

    def reset(self):
        self.cx, self.cy, self.half = CX0, CY0, HALF0
        self.mi = MI0
        self.mi_auto = True
        self.mi_auto_var.set(True)
        self.mi_minus.config(state="disabled")
        self.mi_plus.config(state="disabled")
        # v6.1.2: il reset riporta la scala a 1x1 (via trace parte un
        # ricalcolo intermedio, poi collassa sul request_render finale).
        self.recalc_var.set("1x1")
        self._select_palette("fuoco")
        self._select_backend(_default_backend())
        self._select_cuda_device(0)
        self._select_vulkan_adapter(0)
        self._refresh_gpu_menu()
        self._select_precision("f32")
        self._sync_precision_buttons()
        self._refresh_title()
        self._cfg_dirty = True
        self._update_mi_label()
        self.request_render("reset totale")

    # ---------------- Pipeline rendering (asincrona latest-wins) ----------------
    def _start_pipeline(self):
        # pipeline asincrona latest-wins (worker + Condition, niente blocco UI)
        self._cv = threading.Condition()
        self._job = None
        self._gen = 0
        self._frames = queue.Queue()
        self._last_msg = ""
        self._full_timer = None
        self._worker = threading.Thread(target=self._worker_loop, daemon=True)
        self._worker.start()
        self._cfg_dirty = False
        self._bench_running = False
        self._bench_result = None
        self._bench_finished = False
        self._bench_after_photo = False
        self._photo_running = False
        self._photo_result = None
        self._photo_finished = False
        self._photo_pending = False
        self.root.after(30, self._poll)
        self.root.after(1000, self._flush_config)

    def request_render(self, msg):
        self._last_msg = msg
        self._cfg_dirty = True
        self._update_mi_label()
        if self._full_timer is not None:
            self.root.after_cancel(self._full_timer)
            self._full_timer = None
        w, h = self.canvas_size()
        view = (self.cx, self.cy, self.half, self.eff_mi())
        # v5.10.2: preview draft a 1/8 SOLO se fatta dalla CPU (4x pixel in
        # meno -> bozza pronta prima durante l'interazione); GPU resta a 1/4.
        div = 8 if S._ACTIVE == "cpu" else 4
        self._submit(view, max(w // div, 16), max(h // div, 16))
        self._full_timer = self.root.after(500, lambda: self._maybe_full(view))

    def _recalc_n(self):
        # v6.0: scala persistente dal dropdown (1 = interattivo, N>1 =
        # antialiasing NxN per tutti i calcoli successivi).
        try:
            return max(1, min(8, int(self.recalc_var.get().split("x")[0])))
        except Exception:
            return 1

    def _maybe_full(self, view):
        self._full_timer = None
        if (self.cx, self.cy, self.half, self.eff_mi()) == view:
            # v6.1.1: durante il benchmark niente full (ne' 1x1 ne' NxN):
            # la GPU e' del bench; al termine _bench_done rinfresca.
            if getattr(self, "_bench_running", False):
                return
            # v6.0: con scala NxN persistente il full va in antialiasing
            # (la preview draft resta leggera per l'interazione).
            if self._recalc_n() > 1:
                self.take_photo(self._recalc_n())
            else:
                w, h = self.canvas_size()
                self._submit(view, w, h)

    def _submit(self, view, w, h):
        # v5.0.0: ogni nuovo job e' una nuova "generazione"; il render CPU in
        # corso (se obsoleto) se ne accorge a fine riga e si ferma (il frame
        # verra' scartato dal worker).
        self._gen += 1
        S._GEN[0] = self._gen
        with self._cv:
            self._job = (view, w, h, self._gen)
            self._cv.notify()

    def _busy_on(self):
        # v7.1.5: il watch va messo ANCHE sul canvas: su macOS il cursore
        # del toplevel non si propaga al canvas sotto il puntatore (resta
        # la freccia durante il render, o la clessidra resta appesa fino
        # al primo movimento del mouse). Sul canvas invece vale subito,
        # in entrambe le direzioni (verificato via NSCursor).
        self.root.config(cursor="watch")
        self.canvas.config(cursor="watch")

    def _busy_off(self):
        # vedi _busy_on (il clear sul solo root restava appeso).
        self.root.config(cursor="")
        self.canvas.config(cursor="")

    def _worker_loop(self):
        while True:
            with self._cv:
                while self._job is None:
                    self._cv.wait()
                job = self._job
                self._job = None
            # v5.9.1: durante il benchmark i render interattivi sono sospesi
            # (niente contesa col thread bench sullo stesso device: su una
            # GPU display, kernel pesante in vista + benchmark insieme
            # rischiava TDR/reset -> cudaErrorDevicesUnavailable).
            if self._bench_running:
                continue
            view, w, h, gen = job
            try:
                self.root.after(0, self._busy_on)
                t0 = time.perf_counter()
                img = compute(view[0], view[1], view[2], w, h, view[3], my_gen=gen)
                rt = time.perf_counter() - t0
            except Exception:
                self.root.after(0, self._busy_off)
                continue
            if S._GEN[0] != gen:
                # obsoleto (vista cambiata): scarto il frame parziale.
                # v7.1.4: se non c'e' lavoro piu' nuovo in coda, tolgo il
                # watch (prima restava appeso: solo _show lo toglieva, ma
                # i frame scartati non arrivano mai a _show).
                with self._cv:
                    _pending = self._job is not None
                if not _pending:
                    self.root.after(0, self._busy_off)
                continue  # obsoleto (vista cambiata): scarto il frame parziale
            self._frames.put((img, self._last_msg, rt))

    def _poll(self):
        frame = None
        try:
            while True:
                frame = self._frames.get_nowait()
        except queue.Empty:
            pass
        if frame is not None:
            self._show(frame[0], frame[1], frame[2])
        if self._bench_finished:
            self._bench_finished = False
            count, secs, err, factor = self._bench_result
            self._bench_done(count, secs, err, factor)
        if self._photo_finished:
            self._photo_finished = False
            img, view, n, rt, err = self._photo_result
            self._photo_done(img, view, n, rt, err)
        self.root.after(30, self._poll)

    def _show(self, img, msg, rt=0.0):
        self._busy_off()
        w, h = self.canvas_size()
        if img.size != (w, h):
            img = img.resize((w, h), Image.NEAREST)
        self.pil = img
        self.photo = ImageTk.PhotoImage(img)
        self.canvas.delete("all")
        self.canvas.create_image(w // 2, h // 2, image=self.photo)
        # v5.9.5: se la CPU resta in single-core, avviso persistente (rosso)
        # col rimando al motivo (si auto-cancella al passaggio a multi-core).
        _single = (S._ACTIVE == "cpu" and not S._NUMBA_OK.get(S._PREC))
        _suffix = (" \u00b7 single-core: motivo in Help > Informazioni"
                   if _single else "")
        self.status.config(text=f"{msg} | {backend()} \u00b7 {hw_name()} | palette: {S._PALETTE} | render: {rt*1000:.0f} ms{_suffix}",
                           foreground=ERR_FG if _single else _backend_fg())
        # v5.8.10: il titolo segue il warmup Numba (single->multi a warmup
        # concluso); backend() e' ricalcolato a ogni frame, il titolo no.
        self._refresh_title()

    # ---------------- File: PNG, zona (JSON), config ----------------
    def save_png(self):
        if getattr(self, "pil", None) is None:
            self.status.config(text="niente immagine da salvare",
                                   foreground=ERR_FG)
            return
        default = "mandelbrot_" + time.strftime("%Y%m%d_%H%M%S") + ".png"
        path = tk.filedialog.asksaveasfilename(
            parent=self.root, defaultextension=".png", initialfile=default,
            filetypes=[("Immagini PNG", "*.png")])
        if not path:
            return
        try:
            self.pil.save(path, "PNG")
            self.status.config(text="salvata: " + path)
        except Exception as ex:
            self.status.config(text="errore salvataggio: " + str(ex),
                                   foreground=ERR_FG)

    def save_zone(self):
        if self.view_file:
            self._save_zone_to(self.view_file)
        else:
            self.save_zone_as()

    def save_zone_as(self):
        default = "mandelbrot_" + time.strftime("%Y%m%d_%H%M%S") + ".json"
        path = tk.filedialog.asksaveasfilename(
            parent=self.root, defaultextension=".json", initialfile=default,
            filetypes=[("File JSON", "*.json"), ("Tutti i file", "*.*")])
        if not path:
            return
        self._save_zone_to(path)

    def _save_zone_to(self, path):
        c = {
            "app": "mandelbrot",
            "versione": VERSION,
            "cx": self.cx,
            "cy": self.cy,
            "half": self.half,
            "mi": self.mi,
            "mi_auto": self.mi_auto,
        }
        try:
            with open(path, "w", encoding="utf-8") as f:
                json.dump(c, f, indent=2, ensure_ascii=False)
                f.write("\n")
            self.view_file = path
            self._refresh_title()
            self._update_save_zone_state()
            self.status.config(text="zona salvata: " + path)
        except Exception as ex:
            self.status.config(text="errore salvataggio zona: " + str(ex),
                                   foreground=ERR_FG)

    def load_zone_as(self):
        path = tk.filedialog.askopenfilename(
            parent=self.root, title="Carica zona",
            filetypes=[("File JSON", "*.json"), ("Tutti i file", "*.*")])
        if not path:
            return
        self._load_zone_from(path)

    def _load_zone_from(self, path):
        try:
            with open(path, "r", encoding="utf-8") as f:
                c = json.load(f)
            self.cx = float(c["cx"])
            self.cy = float(c["cy"])
            self.half = max(float(c["half"]), MIN_HALF)
            self.mi = int(c.get("mi", self.mi))
            self.mi_auto = bool(c.get("mi_auto", self.mi_auto))
        except Exception as ex:
            self.status.config(text="errore caricamento zona: " + str(ex),
                                   foreground=ERR_FG)
            return
        self.mi_auto_var.set(self.mi_auto)
        st = "disabled" if self.mi_auto else "normal"
        self.mi_minus.config(state=st)
        self.mi_plus.config(state=st)
        self.view_file = path
        self._refresh_title()
        self._update_save_zone_state()
        self.request_render("zona caricata: " + os.path.basename(path))

    def save_config(self):
        # v5.1.2: la VISTA (cx/cy/half/mi) non e' piu' persistita in config:
        # l'app parte sempre con la configurazione di default (intero insieme,
        # MI auto); la vista si salva solo col file zona ('Salva zona').
        # NB: view_file NON e' persistito (v5.1.1): 'Salva zona' chiede sempre
        # il nome finche' non si carica/salva una zona in quella sessione.
        # v5.6.0: salvo il backend ATTIVO per nome (cpu/cuda/metal/vulkan).
        c = dict(precision=S._PREC, palette=S._PALETTE,
                 backend=S._ACTIVE,
                 cuda_device=S._CUDA_DEV,
                 cuda_split=S._CUDA_SPLIT_ON,
                 cuda_split_ratio=S._CUDA_SPLIT_RATIO,
                 vulkan_adapter=S._VULKAN_DEV,
                 bench_mode=getattr(self, "_bench_mode", "standard"),
                 bench=dict(self.bench))
        try:
            d = os.path.dirname(CONFIG_PATH)
            if d:
                os.makedirs(d, exist_ok=True)
            with open(CONFIG_PATH, "w", encoding="utf-8") as f:
                json.dump(c, f, indent=2)
        except Exception:
            pass

    def load_config(self):
        if not os.path.exists(CONFIG_PATH):
            return False
        try:
            with open(CONFIG_PATH, "r", encoding="utf-8") as f:
                c = json.load(f)
        except Exception:
            return False
        # v5.1.2: la vista (cx, cy, half, mi, mi_auto) NON viene ripristinata:
        # l'app parte SEMPRE con la configurazione di default (intero insieme,
        # MI auto, come la prima volta). I vecchi valori in config sono
        # ignorati; la vista si recupera solo con 'Carica zona...'.
        # v5.1.1: view_file non viene ripristinato: 'Salva zona' chiede sempre
        # il nome finche' non si carica/salva una zona in questa sessione.
        self._load_bench(c.get("bench"))
        # v7.2.0: modalita' benchmark memorizzata (solo standard/esperto).
        bm = c.get("bench_mode", "standard")
        self._bench_mode = bm if bm in ("standard", "esperto") else "standard"
        self._select_palette(c.get("palette", "fuoco"))
        # v5.6.0: il backend e' per nome (cpu/cuda/metal/vulkan); i vecchi
        # valori "gpu" (v5.4.x/5.5.0) migrano al default GPU. _select_backend
        # scarta (restituendo False) i backend non disponibili, restando sul
        # default di avvio (che e' sempre disponibile).
        be = c.get("backend", _default_backend())
        if be == "gpu":
            be = _default_backend()
        self._select_backend(be)
        # v5.9.0: device CUDA persistito (set_cuda_device clamp-a il range).
        self._select_cuda_device(c.get("cuda_device", 0))
        # v6.2: split + rapporto persistiti (solo con 2+ CUDA e motore cuda).
        try:
            S._CUDA_SPLIT_RATIO = min(0.9, max(0.1,
                                             float(c.get("cuda_split_ratio", 0.5))))
        except (TypeError, ValueError):
            S._CUDA_SPLIT_RATIO = 0.5
        if c.get("cuda_split", False) and S._ACTIVE == "cuda":
            set_cuda_split(True)
        # v5.9.2: adapter Vulkan persistito (idem); poi il dropdown segue il
        # motore attivo (potrebbe cambiare contenuto rispetto all'avvio).
        self._select_vulkan_adapter(c.get("vulkan_adapter", 0))
        self._refresh_gpu_menu()
        # la precisione va DOPO il motore: la disponibilita' di f64 dipende
        # dallo slot GPU corrente (set_prec la rifiuta se lo slot non la fa).
        self._select_precision(c.get("precision", "f32"))
        self._sync_precision_buttons()
        self._refresh_title()
        self._update_mi_label()
        return True

    def _load_bench(self, b):
        if not isinstance(b, dict):
            return
        # v7.1.1: clamp di robustezza (un valore spurio in config — es. secs
        # scritto da una sessione di test — non deve piu' accorciare o
        # rompere il benchmark; ogni campo malformato ricade sul default).
        def _f(key, lo, hi):
            try:
                v = float(b.get(key, BENCH[key]))
            except (TypeError, ValueError):
                return BENCH[key]
            if not math.isfinite(v):
                return BENCH[key]
            return min(hi, max(lo, v))
        def _i(key, lo, hi):
            try:
                v = int(b.get(key, BENCH[key]))
            except (TypeError, ValueError):
                return BENCH[key]
            return min(hi, max(lo, v))
        self.bench = dict(BENCH)
        self.bench["cx"] = _f("cx", -2.0, 2.0)
        self.bench["cy"] = _f("cy", -2.0, 2.0)
        self.bench["half"] = _f("half", 1e-12, 2.0)
        # 'mi' non si carica piu': e' derivato da auto_mi(bench['half'])
        self.bench["w"] = _i("w", 64, 7680)
        self.bench["h"] = _i("h", 64, 7680)
        self.bench["secs"] = _f("secs", 1.0, 120.0)

    def _flush_config(self):
        if self._cfg_dirty:
            self._cfg_dirty = False
            self.save_config()
        self.root.after(1000, self._flush_config)

    def on_exit(self):
        self._cfg_dirty = False
        try:
            self.save_config()
        except Exception:
            pass
        self.root.destroy()

    # ---------------- Benchmark ----------------
    def _bench_rows(self, mode=None):
        b = self.bench
        mi = auto_mi(b["half"])
        # v7.2.0: metodo di misura (GPU diluizione 4x area, CPU 1x).
        scale = BENCH_GPU_SCALE if S._ACTIVE != "cpu" else 1
        rows = [
            ("Regione", f"c = ({b['cx']}, {b['cy']})"),
            ("Met\u00e0 lato", f"{b['half']:.3e}"),
            ("Iterazioni", f"{mi}\u00a0 (formula auto)"),
            ("Risoluzione", f"{b['w']} \u00d7 {b['h']} px"
             + (f" \u00d7{scale:g} per lato ({b['w']*scale}\u00d7{b['h']*scale})"
                if scale > 1 else "")),
            ("Metodo", "GPU: diluizione 4x area, rps \u00d74"
                     if scale > 1 else "CPU: 1x (no diluizione)"),
            ("Motore", f"{backend()} (corrente)"),
            ("Hardware", hw_name()),
            ("Durata", f"{b['secs']:.0f} s"),
        ]
        if mode == "esperto":
            rows.append(("Modalit\u00e0", "Esperta: 3 \u00d7 8 s, vale la migliore"))
        elif mode == "standard":
            rows.append(("Modalit\u00e0", "Standard: 8 s"))
        return rows

    def _bench_chart(self, parent, rps):
        """Grafico a barre orizzontali del benchmark: i riferimenti storici
        (BENCH_REF, con nome hardware) + la run corrente (rps) evidenziata, col
        solo metodo attivo nell'etichetta (es. "CUDA f32 - questa run"). Il nome
        hardware della run corrente NON e' nel grafico (v5.8.1: mostrato sotto
        dal dialog chiamante). Scala orizzontale lineare automatica sul massimo
        dei valori; altezza in base al numero di barre. Ritorna il Canvas
        (da packare da chi chiama)."""
        bars = list(BENCH_REF)
        # v5.8.1: la run corrente mostra solo il metodo (backend); il nome
        # hardware NON e' nel grafico -> mostrato sotto dal dialog chiamante.
        bars.append((backend() + " \u2014 questa run",
                      float(rps)))
        raw_max = max(v for _, v in bars)
        # step "nice" per ~5 divisioni (serie 1/2/5 x 10^n); l'asse termina
        # esattamente sull'ultimo tick >= massimo (mai oltre)
        raw = raw_max / 5.0
        mag = 10 ** math.floor(math.log10(raw))
        step = next(m2 * mag for m2 in (1.0, 2.0, 5.0, 10.0) if m2 * mag >= raw)
        nt = max(1, int(math.ceil(raw_max / step - 1e-9)))
        axis_max = nt * step
        W = 590
        L, R, T, B = 230, 60, 8, 26
        pw = W - L - R
        bar_h, gap = 20, 10
        n = len(bars)
        total = n * bar_h + (n - 1) * gap
        # v5.9.1: altezza dinamica (150px fissi non bastavano per 6 barre).
        H = T + total + B
        y0 = T
        cv = tk.Canvas(parent, width=W, height=H, highlightthickness=0)
        def x(v):
            return L + pw * v / axis_max
        for k in range(nt + 1):
            t = k * step
            xt = x(t)
            cv.create_line(xt, T, xt, H - B, fill="#a0a0a0")
            cv.create_text(xt, H - B + 12, text="%g" % t, font=("Consolas", 9))
        cv.create_line(L, H - B, W - R, H - B, fill="#8a8a8a")
        for i, (name, v) in enumerate(bars):
            y = y0 + i * (bar_h + gap)
            cur = (i == n - 1)
            col = "#2ea44f" if cur else "#8a8a8a"
            cv.create_text(L - 8, y + bar_h / 2, text=name, anchor="e",
                           font=("Segoe UI", 9, "bold" if cur else "normal"))
            xe = max(x(v), L + 1)
            cv.create_rectangle(L, y, xe, y + bar_h, fill=col, outline="")
            cv.create_text(xe + 6, y + bar_h / 2, text="%g" % v, anchor="w",
                           font=("Consolas", 10, "bold"))
        return cv

    def _modal(self, win):
        """Rende 'win' modale e centrato su self.root; blocca fino a chiusura.
        Ritorna True se la finestra e' stata chiusa normalmente (distrutta),
        False se annullata (X, Esc o pulsante 'Annulla')."""
        def cancel(_e=None):
            win._annullato = True
            win.destroy()
        win._annullato = False
        win.protocol("WM_DELETE_WINDOW", cancel)
        win.bind("<Escape>", cancel)
        win.transient(self.root)
        win.grab_set()
        win.update_idletasks()
        w, h = win.winfo_width(), win.winfo_height()
        x = self.root.winfo_rootx() + (self.root.winfo_width() - w) // 2
        y = self.root.winfo_rooty() + max((self.root.winfo_height() - h) // 3, 0)
        win.geometry(f"+{max(x, 0)}+{max(y, 0)}")
        self.root.wait_window(win)
        self.root.focus_force()
        return not win._annullato

    def _bench_ask(self):
        """Dialog di conferma personalizzato: 'standard'/'esperto' o None."""
        win = tk.Toplevel(self.root)
        win.title("Benchmark Mandelbrot")
        win.resizable(False, False)
        body = tk.Frame(win, padx=26, pady=20)
        body.pack(fill="both", expand=True)
        tk.Label(body, text="Benchmark standardizzato",
                 font=("Segoe UI", 14, "bold")).pack(anchor="w")
        tk.Label(body, text="Parametri del test (regione e durata comparabili tra versioni):").pack(anchor="w", pady=(2, 12))
        rows = tk.Frame(body)
        rows.pack(fill="x")
        for i, (k, v) in enumerate(self._bench_rows()):
            tk.Label(rows, text=k, width=12, anchor="w").grid(row=i, column=0, sticky="w", pady=3)
            tk.Label(rows, text=v, anchor="e",
                     font=("Consolas", 12)).grid(row=i, column=1, sticky="e",
                                                  padx=(18, 0), pady=3)
        # v7.2.0: pre-selezionata la modalita' memorizzata (config bench_mode).
        mode_var = tk.StringVar(value=getattr(self, "_bench_mode", "standard"))
        tk.Radiobutton(body, text="Standard: 1 prova da 8 s",
                       variable=mode_var, value="standard").pack(anchor="w", pady=(12, 0))
        tk.Radiobutton(body, text="Esperta: 3 prove da 8 s, vale la migliore",
                       variable=mode_var, value="esperto").pack(anchor="w")
        btns = tk.Frame(win)
        btns.pack(fill="x", padx=26, pady=(16, 18))

        def go(_e=None):
            win._annullato = False
            win._mode = mode_var.get()
            win.destroy()
        def cancel(_e=None):
            win._annullato = True
            win.destroy()
        annulla = tk.Button(btns, text="Annulla", command=cancel)
        annulla.pack(side="right")
        avvia = tk.Button(btns, text="Avvia", command=go)
        avvia.pack(side="right", padx=(0, 10))
        win.bind("<Return>", go)
        avvia.focus_set()
        if not self._modal(win):
            return None
        return getattr(win, "_mode", "standard")

    def _bench_result_dialog(self, count, secs, err, factor=1.0):
        """Dialog risultato: rendering/s grande e evidente come vero risultato.
        v7.2.0: factor = diluizione GPU (4x area -> rps misurato x4);
        le statistiche mostrano i rendering equivalenti a 1x."""
        win = tk.Toplevel(self.root)
        win.title("Benchmark \u2014 risultato")
        win.resizable(False, False)
        body = tk.Frame(win, padx=30, pady=22)
        body.pack(fill="both", expand=True)
        tk.Label(body, text="Risultato",
                 font=("Segoe UI", 14, "bold")).pack(anchor="w")
        mode = getattr(self, "_bench_mode", "standard")
        if count > 0:
            rps = count / secs * factor  # v7.2.0: rps equivalenti a 1x
            tk.Frame(body, bg="#8a8a8a", height=1).pack(fill="x", pady=(10, 0))
            tk.Label(body, text=f"{rps:.2f}",
                     font=("Segoe UI", 42, "bold"),
                     foreground="#2ea44f").pack(pady=(16, 0))
            tk.Label(body, text="rendering / secondo",
                     font=("Segoe UI", 13, "bold"),
                     foreground="#2ea44f").pack(pady=(0, 8))
            if factor > 1:
                n_eff = count * factor
                stat = (f"{count} rendering {factor:g}x in {secs:.1f} s   \u00b7   "
                        f"= {n_eff:.0f} equivalenti ({secs*1000/n_eff:.0f} ms "
                        f"ciascuno)")
            else:
                stat = (f"{count} rendering in {secs:.1f} s   \u00b7   "
                        f"{secs/count*1000:.0f} ms ciascuno")
            tk.Label(body, text=stat).pack(pady=(0, 8))
            if mode == "esperto":
                tk.Label(body, text="migliore di 3 prove da 8 s",
                         font=("Segoe UI", 11, "italic")).pack(pady=(0, 8))
            # v7.1.0: codice di autenticità (tamper-evident): lega rps +
            # hardware + resto dei campi; verificabile in Help > Verifica.
            # v7.1.2: campo copiabile (Entry readonly + pulsante Copia).
            b = self.bench
            code = make_code(rps, hw_name(), backend(), S._PREC,
                             VERSION, auto_mi(b["half"]), b["secs"])
            tk.Label(body, text="Codice di autenticità:",
                     ).pack(anchor="w", pady=(0, 2))
            crow = tk.Frame(body)
            crow.pack(anchor="w", pady=(0, 16))
            code_entry = tk.Entry(crow, font=("Consolas", 14, "bold"), width=19,
                                  readonlybackground=body.cget("bg"))
            code_entry.insert(0, fmt_code(code))
            code_entry.config(state="readonly")
            code_entry.pack(side="left")
            code_entry.bind("<Button-1>", lambda e: (
                code_entry.select_range(0, "end"), code_entry.icursor("end")))
            copied = tk.Label(crow, text="", font=("Segoe UI", 10),
                              foreground="#2ea44f")
            def copia():
                win.clipboard_clear()
                win.clipboard_append(fmt_code(code))
                copied.config(text="copiato!")
            tk.Button(crow, text="Copia", command=copia).pack(side="left",
                                                              padx=(10, 0))
            copied.pack(side="left", padx=(8, 0))
            tk.Label(body, text="Confronto coi riferimenti storici (rendering/s):",
                     ).pack(anchor="w", pady=(0, 4))
            self._bench_chart(body, rps).pack(anchor="w", pady=(2, 14))
        else:
            tk.Label(body, text="BENCHMARK FALLITO",
                     font=("Segoe UI", 20, "bold"),
                     foreground="#e5534b").pack(pady=(16, 6))
            tk.Label(body, text=str(err), foreground="#e5534b",
                     wraplength=420, justify="left").pack(anchor="w", pady=(0, 16))
            # v5.9.1: hint operativo se il device e' occupato o in reset (TDR).
            if err is not None and ("DevicesUnavailable" in str(err)
                                    or "busy or unavailable" in str(err)):
                tk.Label(body, text="Device CUDA occupato o in reset: riprova "
                         "tra poco o riavvia l'app prima di rilanciare.",
                         wraplength=420, justify="left").pack(anchor="w",
                                                             pady=(0, 16))
        tk.Label(body, text="Parametri del test:").pack(anchor="w", pady=(0, 4))
        rows = tk.Frame(body)
        rows.pack(fill="x", anchor="w")
        for i, (k, v) in enumerate(self._bench_rows(mode=mode)):
            tk.Label(rows, text=k, width=12, anchor="w").grid(row=i, column=0, sticky="w", pady=2)
            tk.Label(rows, text=v, anchor="e",
                     font=("Consolas", 11)).grid(row=i, column=1, sticky="e",
                                                  padx=(18, 0), pady=2)
        def chiudi(_e=None):
            win._annullato = False
            win.destroy()
        tk.Button(win, text="Chiudi", command=chiudi).pack(pady=(18, 20))
        win.bind("<Return>", chiudi)
        self._modal(win)

    def run_benchmark(self):
        if getattr(self, "_bench_running", False):
            self.status.config(text="benchmark gia' in corso")
            return
        mode = self._bench_ask()
        if mode is None:
            self.status.config(text="benchmark annullato")
            return
        self._bench_mode = mode
        # v7.2.0: modalita' scelta memorizzata (flush config entro 1 s / exit).
        self._cfg_dirty = True
        # v6.1.1: mai in contesa col ricalcolo NxN sullo stesso device
        # (un kernel gigante in parallelo al bench crollava il conteggio).
        # Se una foto e' in corso, il bench parte accodato a fine ricalcolo.
        if getattr(self, "_photo_running", False):
            self._bench_after_photo = True
            self._photo_pending = False
            self.status.config(text="ricalcolo in corso: il benchmark parte appena pronto...")
            return
        self._start_bench()

    def _start_bench(self):
        # v6.1.1: avvio effettivo (immediato o accodato da _photo_done).
        # Il bench ha la GPU in esclusiva: cancella foto pendenti e il
        # full-timer (niente take_photo concorrente per gli 8 s).
        # v6.2: il bench e' sempre single-device (buf dedicato -> mai split).
        S._BENCH_ACTIVE = True
        self._bench_after_photo = False
        self._photo_pending = False
        if self._full_timer is not None:
            self.root.after_cancel(self._full_timer)
            self._full_timer = None
        self._bench_running = True
        self._busy_on()
        _n = 3 if getattr(self, "_bench_mode", "standard") == "esperto" else 1
        self.status.config(text=f"benchmark in corso ({self.bench['secs']:.0f} s"
                                + (f" \u00d7 {_n}..." if _n > 1 else "...") + ")")
        threading.Thread(target=self._bench_worker, daemon=True).start()

    def _bench_worker(self):
        b = self.bench
        mi = auto_mi(b["half"])
        # v7.2.0: diluizione GPU — 2x per lato (4x area): l'overhead fisso
        # per-iterazione (Python/launch/sync) si ammortizza x4 e l'rps
        # riportato = misurato x4 (kernel e copie restano nella misura:
        # scalano col'area). La CPU resta 1x (a 4x area sarebbe troppo lenta).
        scale = BENCH_GPU_SCALE if S._ACTIVE != "cpu" else 1
        bw = min(b["w"] * scale, 7680)
        bh = min(b["h"] * scale, 7680)
        factor = (bw * bh) / (b["w"] * b["h"])
        need = bw * bh * 3
        bench_buf = None
        # v5.6.0: il buffer di lavoro serve solo al percorso CUDA (Metal/Vulkan
        # usano il proprio buffer, CPU la memoria numpy).
        # v6.2.3: se lo split e' attivo, non passo buf (lo split alloca i
        # propri buffer per-device; passare buf=... forzo il single).
        if S._ACTIVE == "cuda" and not _cuda_split_ready(b["h"]):
            try:
                import cupy as cp
                _dev = S._CUDA_DEVICES[S._CUDA_DEV][0] if S._CUDA_DEVICES else 0
                with cp.cuda.Device(_dev):
                    bench_buf = cp.empty((need,), dtype=cp.uint8)
            except Exception:
                bench_buf = None
        def render():
            return compute(b["cx"], b["cy"], b["half"], bw, bh, mi,
                           buf=bench_buf)
        # v7.2.1: deadline PER RENDERING (pre-warmup e finestra di misura):
        # un singolo render bloccato deve dare un errore, non un hang
        # infinito del benchmark (niente da fare col cap di 0.7 s / b['secs']
        # se il blocco e' DENTRO un render). Metal ha gia' il proprio timeout
        # (METAL_TIMEOUT_S, qui +5 s di margine); CPU: single-core numpy
        # puo' essere davvero lento -> limite generoso.
        rlimit = (METAL_TIMEOUT_S + 5) if S._ACTIVE == "metal" \
            else BENCH_RENDER_LIMIT_S
        def render_dl():
            t0 = time.perf_counter()
            r = render()
            if time.perf_counter() - t0 > rlimit:
                raise RuntimeError(
                    "rendering singolo oltre %d s (GPU/CPU bloccata?)"
                    % rlimit)
            return r
        # v7.2.0: pre-warmup PRIMA della finestra di misura (bounded: <=50
        # rendering e <=0.7 s): clock GPU, buffer pinned, kernel esatto —
        # tutti fuori dalla misura. Errore qui = benchmark fallito.
        t_w = time.perf_counter()
        try:
            for _ in range(50):
                if time.perf_counter() - t_w > 0.7:
                    break
                render_dl()
        except Exception as ex:
            self._bench_log("pre-warmup fallito", ex)
            self._bench_result = (0, b["secs"], str(ex), factor)
            self._bench_finished = True
            return
        # v7.1.0 (esperto): N prove da b['secs'] s, vale la migliore tra
        # quelle completate; errore -> stop (il device e' probabilmente
        # occupato/in reset, riprovare non serve).
        runs = 3 if getattr(self, "_bench_mode", "standard") == "esperto" else 1
        best = 0
        err = None
        for run in range(runs):
            if runs > 1:
                self.root.after(0, lambda r=run: self.status.config(
                    text=f"benchmark esperto in corso (prova {r + 1}/{runs})..."))
            t_end = time.perf_counter() + b["secs"]
            count = 0
            ok = True
            while time.perf_counter() < t_end:
                try:
                    render_dl()
                    count += 1
                except Exception as ex:
                    self._bench_log("misura fallita (prova %d/%d)"
                                    % (run + 1, runs), ex)
                    err = str(ex)
                    ok = False
                    break
            if count > best:
                best = count  # anche parziale (come prima: il dialog mostra
                # il risultato se count > 0, l'errore solo se tutto fallito)
            if not ok:
                break
        count = best
        if count == 0 and err is None:
            err = "nessun rendering completato"
        if count == 0:
            self._bench_log("benchmark completato a 0 rendering "
                            "(err=%r)" % (err,))
        # thread-safe: il thread principale (in _poll) rileva il flag e mostra il risultato
        self._bench_result = (count, b["secs"], err, factor)
        self._bench_finished = True

    def _bench_log(self, ctx, ex=None):
        # v7.2.1: esito/ambiente del benchmark in ~/mandelbrot/bench.log —
        # per diagnosticare hang/crash su hardware non collaudato (l'utente
        # puo' inviarci il file). Troncato a ~200 righe per restare piccolo.
        # Silenzioso: un errore qui non deve mai rompere il benchmark.
        import platform
        import sys
        import traceback
        p = os.path.join(os.path.dirname(CONFIG_PATH), "bench.log")
        try:
            os.makedirs(os.path.dirname(p), exist_ok=True)
            old = []
            if os.path.exists(p):
                with open(p, "r", encoding="utf-8", errors="replace") as f:
                    old = f.readlines()
            e = [
                "=" * 62 + "\n",
                "%s  %s\n" % (time.strftime("%Y-%m-%d %H:%M:%S"), ctx),
                "versione : %s\n" % VERSION,
                "motore   : %s\n" % backend(),
                "hw       : %s\n" % hw_name(),
                "os       : %s\n" % platform.platform(),
                "python   : %s\n" % sys.version.split()[0],
                "numba    : %s\n" % (S._NUMBA_OK,),
                "bench    : cx=%s cy=%s half=%s w=%s h=%s secs=%s mode=%s\n"
                    % (self.bench["cx"], self.bench["cy"], self.bench["half"],
                       self.bench["w"], self.bench["h"], self.bench["secs"],
                       getattr(self, "_bench_mode", "standard")),
            ]
            if ex is not None:
                e.append("errore   : %r\n" % (ex,))
                e.append("".join(traceback.format_exception(
                    type(ex), ex, ex.__traceback__)))
            with open(p, "w", encoding="utf-8") as f:
                f.write("".join(old[-150:] + e))
        except Exception:
            pass

    def _bench_done(self, count, secs, err, factor=1.0):
        S._BENCH_ACTIVE = False
        self._bench_running = False
        self._busy_off()
        self.status.config(text="benchmark completato")
        self._bench_result_dialog(count, secs, err, factor)
        # v6.1.1: rinfresca la vista corrente (durante il bench i render
        # erano sospesi); rispetta la scala NxN persistente.
        self.recalc_scaled()

    def recalc(self):
        # v5.10.0: rifa il rendering della vista corrente (preview + full).
        # v5.11.0: resta come percorso 1x1 di recalc_scaled.
        self.request_render("ricalcolo manuale")

    def recalc_scaled(self):
        # v5.11.0: dispatcher del pulsante unico "Ricalcola" + dropdown
        # esplicito 1x1/2x2/4x4/8x8. 1x1 = vista corrente (pipeline
        # interattiva), NxN = antialiasing in background (take_photo).
        # v6.0: chiamato anche dal trace sul dropdown (selezione =
        # ricalcolo immediato); legge la scala persistente via _recalc_n.
        n = self._recalc_n()
        if n <= 1:
            self.recalc()
        else:
            self.take_photo(n)

    def take_photo(self, n=2):
        # v5.9.6: foto antialiasing: ricalcola la vista corrente a NxN per
        # lato e mostra la media NxN dei pixel vicini. Eseguita
        # in background (come il benchmark): cursore hourglass, UI viva.
        # Per vedere la foto bisogna aspettare senza toccare: se vista,
        # palette, motore o precisione cambiano durante il calcolo, la
        # foto stantia viene scartata.
        # v5.9.7: all'avvio invalida i render interattivi in volo/pendenti
        # (stessa vista): bump di S._GEN (ferma le bande CPU, fa scartare il
        # frame al worker) + cancella il full-render ritardato di
        # request_render, altrimenti sovrascriverebbero la foto con la
        # versione non antialiased.
        # v5.10.0: fattore N parametrico (2 = Ricalcola 2x2, 4 = Ricalcola 4x4):
        # 4x4 = 16x pixel, molto piu' lento e pesante in memoria.
        # v5.11.0: scala 1x1/2x2/4x4/8x8 dal dropdown unico + guardia memoria
        # (rifiuto prima di allocare) + w,h nello snapshot stantio (un resize
        # durante il calcolo scarta invece di mostrare uno stirato NEAREST).
        # v6.0: se un ricalcolo e' gia' in corso, la richiesta non si perde
        # (pending latest-wins: _photo_done rilancia sulla vista corrente).
        # v6.1.1: durante il benchmark niente foto (GPU in esclusiva al bench).
        if getattr(self, "_bench_running", False):
            self.status.config(text="benchmark in corso (ricalcolo sospeso)")
            return
        if getattr(self, "_photo_running", False):
            self._photo_pending = True
            self.status.config(text="ricalcolo accodato (parte appena pronto)...")
            return
        try:
            n = int(n)
        except Exception:
            n = 2
        n = max(2, min(8, n))
        w, h = self.canvas_size()
        # v6.1: niente tetto statico: rifiuto solo se la stima supera la
        # memoria davvero disponibile (VRAM + RAM).
        ok, why = _photo_mem_ok(n, w, h)
        if not ok:
            self.status.config(text=why, foreground=ERR_FG)
            return
        view = (self.cx, self.cy, self.half, self.eff_mi(),
                S._PALETTE, S._ACTIVE, S._PREC, w, h)
        if self._full_timer is not None:
            self.root.after_cancel(self._full_timer)
            self._full_timer = None
        self._gen += 1
        S._GEN[0] = self._gen
        self._photo_running = True
        self.recalc_btn.config(state="disabled")
        self._busy_on()
        self.status.config(text=f"ricalcolo in corso (antialiasing {n}x{n}, non toccare)...")
        threading.Thread(target=self._photo_worker, args=(view, w, h, n),
                         daemon=True).start()

    def _photo_worker(self, view, w, h, n):
        # Box-filter NxN sullo spazio RGB: unico code-path per tutti i
        # backend (la GPU colora in-kernel, nessun it/mag su host).
        # Nessuna cancellazione durante il calcolo (il risultato stantio
        # e' scartato in _photo_done, non qui).
        # v5.11.0: MemoryError -> errore pulito (mostrato in _photo_done);
        # workspace CPU gigante non inquinato in cache (pop dopo l'uso).
        t0 = time.perf_counter()
        try:
            big = compute(view[0], view[1], view[2], n * w, n * h, view[3])
            if big.size != (n * w, n * h):
                big = big.resize((n * w, n * h), Image.BILINEAR)
            a = np.asarray(big)
            small = (a.reshape(h, n, w, n, 3).mean(axis=(1, 3)) + 0.5).astype(np.uint8)
            img = Image.fromarray(small, "RGB")
            self._photo_result = (img, view, n, time.perf_counter() - t0, None)
        except MemoryError:
            self._photo_result = (None, view, n, 0.0,
                                  "memoria insufficiente: riduci la finestra o usa N minore")
        except Exception as ex:
            self._photo_result = (None, view, n, 0.0, str(ex))
        finally:
            try:
                S._CPU_WS.pop((n * w, n * h, "f32"), None)
                S._CPU_WS.pop((n * w, n * h, "f64"), None)
            except Exception:
                pass
        self._photo_finished = True

    def _photo_done(self, img, view, n, rt, err):
        # Unico punto di uscita (anche in errore): ripristina sempre
        # cursore e pulsanti, come _bench_done.
        # v6.0: se durante il calcolo e' arrivata un'altra richiesta
        # (pending), rilancia sulla vista corrente con la scala persistente.
        # v6.1.1: ma il benchmark accodato ha precedenza (esclusiva GPU).
        self._photo_result = None
        self._photo_running = False
        self.recalc_btn.config(state="normal")
        self._busy_off()
        if getattr(self, "_bench_after_photo", False):
            self._photo_pending = False
            self._start_bench()
            return
        if self._photo_pending:
            self._photo_pending = False
            self.recalc_scaled()
            return
        if err:
            self.status.config(text=f"ricalcolo fallito: {err}")
        elif (self.cx, self.cy, self.half, self.eff_mi(),
                S._PALETTE, S._ACTIVE, S._PREC, *self.canvas_size()) != view:
            self.status.config(text="ricalcolo scartato (vista cambiata)")
        else:
            self._show(img, f"ricalcolo antialiasing {n}x{n}", rt)
def main():
    root = tk.Tk()
    MandelbrotApp(root)
    root.mainloop()

if __name__ == "__main__":
    main()

