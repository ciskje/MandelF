#!/usr/bin/env python
# Build self-contained (one-dir) multipiattaforma per l'app Mandelbrot.
# Ordine: icona -> PyInstaller (mandelbrot.spec) -> post (verifica + zip).
#   Windows: python build_app.py   -> app su disco di sistema + zip nel progetto
#   macOS  : python build_app.py   -> app su disco di sistema + .dmg nel progetto
# Nota: il runtime CUDA NON e' incluso; la GPU CUDA funziona solo se l'utente ha
# installato driver NVIDIA + runtime CUDA (trovati da cupy via cuda-pathfinder).
# La GPU Vulkan (wgpu/wgpu-native, AMD/NVIDIA/Intel) E' invece bundled e funziona
# out-of-the-box.
import os
import re
import shutil
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
IS_WIN32 = (sys.platform == "win32")
IS_DARWIN = (sys.platform == "darwin")
# Workpath PyInstaller (build/) e output app (dist/) sul DISCO DI SISTEMA (temp
# utente), NON nel progetto (che puo' stare su una share di rete, dove la build
# e' lenta). Sul progetto/NAS resta SOLO lo zip (Windows).
WORKDIR = os.path.join(tempfile.gettempdir(), "mandelbrot_build")
DISTDIR = os.path.join(tempfile.gettempdir(), "mandelbrot_dist")
DMGDIR = os.path.join(tempfile.gettempdir(), "mandelbrot_dmg")
# Regola generale: su dist/ (progetto/NAS) tenere le ultime KEEP_N versioni
# PER PIATTAFORMA (win64.zip e macos.dmg separatamente); le piu' vecchie
# vengono rimosse automaticamente a fine build.
KEEP_N = 3

with open(os.path.join(HERE, "mandel.py"), encoding="utf-8") as _f:
    _head = _f.read(4096)
VERSION = re.search(r"VERSIONE:\s*([\d.]+)", _head).group(1)


def run(cmd, **kw):
    print("+", " ".join(cmd), flush=True)
    return subprocess.run(cmd, cwd=HERE, **kw)


def fail(msg):
    print("ERRORE:", msg, file=sys.stderr)
    sys.exit(1)


def keep_last_n_dist(n=KEEP_N):
    # Tieni le ultime n versioni (X.Y.Z) PER PIATTAFORMA dei distributivi
    # in dist/ (progetto/NAS); rimuovi gli artefatti delle versioni piu' vecchie.
    d = os.path.join(HERE, "dist")
    if not os.path.isdir(d):
        return
    items = []
    for fn in os.listdir(d):
        # v6.0: accetta anche versioni a 2 parti (es. v6.0 oltre a X.Y.Z);
        # il patch mancante vale 0 ai fini dell'ordinamento.
        m = re.match(r"mandelbrot-v(\d+)\.(\d+)(?:\.(\d+))?-(win64|macos)\.", fn, re.IGNORECASE)
        if m:
            ver = (int(m.group(1)), int(m.group(2)), int(m.group(3) or 0))
            plat = m.group(4).lower()
            items.append((ver, fn, plat))
    if not items:
        print("cleanup dist/: nessun artefatto versionato")
        return
    keep = set()
    for plat in set(p for _, _, p in items):
        plat_items = sorted([(v, fn) for v, fn, p in items if p == plat],
                           key=lambda x: x[0], reverse=True)
        for _, fn in plat_items[:n]:
            keep.add(fn)
    removed = [fn for _, fn, _ in items if fn not in keep]
    for fn in removed:
        os.remove(os.path.join(d, fn))
    if removed:
        print("cleanup dist/: rimosse", ", ".join(sorted(removed)))
    else:
        print("cleanup dist/: ok (%d per piattaforma trattenute)" % n)


# 1) Icona (icon_src.png + mandelbrot.ico + mandelbrot.icns su macOS) via motore CPU di mandel.py
r = run([sys.executable, os.path.join(HERE, "make_icon.py")])
if r.returncode != 0:
    fail("make_icon.py non riuscita")
if not os.path.isfile(os.path.join(HERE, "mandelbrot.ico")):
    fail("mandelbrot.ico non generata")
if IS_DARWIN and not os.path.isfile(os.path.join(HERE, "mandelbrot.icns")):
    fail("mandelbrot.icns non generata (serve sips su macOS)")

# 2) PyInstaller (ricetta mandelbrot.spec) — workpath (build/) e distpath (app)
#    su disco di sistema; l'app NON finisce sul NAS/progetto
print("Workpath (build/):", WORKDIR, flush=True)
print("Distpath (app):  ", DISTDIR, flush=True)
r = run([sys.executable, "-m", "PyInstaller", "--noconfirm", "--clean",
         "--workpath", WORKDIR, "--distpath", DISTDIR,
         os.path.join(HERE, "mandelbrot.spec")])
if r.returncode != 0:
    fail("PyInstaller non riuscito")

# 3) Post-build
if IS_WIN32:
    app_dir = os.path.join(DISTDIR, "Mandelbrot")
    exe = os.path.join(app_dir, "Mandelbrot.exe")
    if not os.path.isfile(exe):
        fail("app non trovata dopo la build (distpath=%s)" % DISTDIR)
    # Zip sul progetto/NAS: l'app resta sul disco di sistema, sul NAS va solo lo zip
    out_dir = os.path.join(HERE, "dist")
    os.makedirs(out_dir, exist_ok=True)
    base = os.path.join(out_dir, "Mandelbrot-v%s-win64" % VERSION)
    if os.path.exists(base + ".zip"):
        os.remove(base + ".zip")
    zip_path = shutil.make_archive(base, "zip",
                                   root_dir=DISTDIR, base_dir="Mandelbrot")
    print("OK: app =", app_dir, "(disco di sistema)")
    print("OK: exe =", exe)
    print("OK: zip =", zip_path, "(progetto/NAS)")
elif IS_DARWIN:
    app = os.path.join(DISTDIR, "Mandelbrot.app")
    if not os.path.isdir(app):
        fail("app non trovata dopo la build (distpath=%s)" % DISTDIR)
    run(["codesign", "--force", "--deep", "--sign", "-", app])
    # DMG: staging in temp (app + link /Applications per il drag-and-drop),
    # poi .dmg sul progetto/NAS. L'app e le intermedie restano sul disco di sistema.
    if os.path.isdir(DMGDIR):
        shutil.rmtree(DMGDIR)
    os.makedirs(DMGDIR)
    shutil.copytree(app, os.path.join(DMGDIR, "Mandelbrot.app"), symlinks=True)
    os.symlink("/Applications", os.path.join(DMGDIR, "Applications"))
    out_dir = os.path.join(HERE, "dist")
    os.makedirs(out_dir, exist_ok=True)
    dmg_base = os.path.join(out_dir, "Mandelbrot-v%s-macos" % VERSION)
    if os.path.exists(dmg_base + ".dmg"):
        os.remove(dmg_base + ".dmg")
    run(["hdiutil", "create", "-volname", "Mandelbrot", "-srcfolder", DMGDIR,
         "-ov", "-format", "UDZO", dmg_base + ".dmg"])
    if os.path.isdir(DMGDIR):
        shutil.rmtree(DMGDIR)
    print("OK: app =", app, "(disco di sistema)")
    print("OK: dmg =", dmg_base + ".dmg", "(progetto/NAS)")
else:
    fail("piattaforma non supportata per la build: %s" % sys.platform)

# 4) Cleanup dist/ (progetto/NAS): regola generale, tenere solo le ultime KEEP_N versioni
keep_last_n_dist(KEEP_N)
