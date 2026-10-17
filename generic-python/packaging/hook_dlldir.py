# Runtime hook PyInstaller (Windows): mette la cartella dell'EXE e _internal sul
# percorso di ricerca DLL (os.add_dll_directory). E' difensivo: garantisce che le
# DLL bundled (es. llvmlite.dll) siano sempre risolvibili e permette a cupy di
# trovare DLL aggiuntive poste accanto all'EXE. Senza effetto su altre piattaforme.
import os
import sys

if (
    sys.platform == "win32"
    and getattr(sys, "frozen", False)
    and hasattr(os, "add_dll_directory")
):
    _exe_dir = os.path.dirname(os.path.abspath(sys.executable))
    _internal = os.path.join(_exe_dir, "_internal")
    for _d in (_exe_dir, _internal):
        if os.path.isdir(_d):
            try:
                os.add_dll_directory(_d)
            except OSError:
                pass
