# MandelbrotViewer (macOS / Avalonia port)

Replica platform-agnostic di **../SPEC_FOR_LLM_PLATFORM_AGNOSTIC.md** v2.22.7.
Scelte implementatore (§1.6): target **macOS arm64**, UI **Avalonia 11**,
GPU **Metal** (fase pendente, fallback CPU per §4.9).

## Build / run / publish (.NET 8 SDK)

- `export PATH=$HOME/.dotnet:$PATH` (SDK installato via dotnet-install.sh)
- Build: `dotnet build MandelbrotViewer`
- Test: `dotnet test MandelbrotViewer.Tests` (10 test fedeltà §8)
- Run: `dotnet run --project MandelbrotViewer`
- Publish: `dotnet publish MandelbrotViewer -c Release -r osx-arm64 --self-contained -p:PublishSingleFile=true -o published`
- CLI: `published/MandelbrotViewer --diag-gpu | --bench-gpu [dev] [--csv f] | --bench-cpu [float] [--csv f]`, `--bench-check [W H [SCALE [MAXITER]]]` (proof-of-work GPU vs CPU)

## Layout

- `MandelbrotViewer/` — app (mappa file in SPECS.md)
- `MandelbrotViewer.Tests/` — test xUnit fedeltà
- `published/` — binario self-contained osx-arm64
- Dati utente: `~/Library/Application Support/MandelbrotViewer/` (settings.json, zone/)
- `dist/` — `MandelbrotViewer.app` (bundle firmato ad-hoc, icona) e
  `MandelbrotViewer-2.22.7-arm64.dmg` (con symlink Applicazioni).
  Nota: firma ad-hoc, non notarizzato — al primo avvio da DMG usare
  tasto destro → Apri (serve Developer ID a pagamento per distribuire senza avvisi)
- Shortcut: binding reference con Ctrl mappato a Cmd su macOS (§1.6)
