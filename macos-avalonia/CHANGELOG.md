# CHANGELOG

## 7.3.1 — Ponytail-audit refactor
- No behavior change: removed Avalonia MVVM template scaffolding
  (`ViewLocator`, `ViewModels`, `DelegateCommand`, `CommunityToolkit.Mvvm`),
  dead debug backdoors (`MV_ANIM`/`MV_DUMP`), shared scalar tails and
  interpolation math, shrunk the hand-rolled PNG writer.
  Verified: `dotnet build` 0 warnings, `dotnet test` 19/19.

## 7.3.0 — MandelF lockstep alignment
- Version 7.3.0 (was 2.22.7); executable renamed `MandelF`;
  `.app`/`dmg` named `MandelF-avalonia-*`; `source/` layout.
  No behavior change.

## 2.22.7 (2026-09-14) — fix animazione Home + rifiniture
- Back-to-set: ogni frame ora atteso e mostrato in ordine (prima i frame lenti
  venivano cancellati dal successivo: freeze all'inizio, salti).
- Back-to-set: centro lineare nella SCALA ("center follows the scale linearly"):
  prima era lineare nel parametro eased e il contenuto schizzava fuori schermo
  nei primi frame; ora l'animazione parte dal punto inquadrato (test continuità:
  passo centro ≤ 5% della larghezza vista per tutti i 120 frame).
- Menu: mnemonici `_` (convenzione Avalonia) al posto di `&`.
- Resize: preview subito + full 300ms dopo l'ultimo SizeChanged (debounce).
- Diagnostica: `MV_ANIM=1` anima da deep-zoom con dump periodici.

## 2.22.7 (2026-09-13) — replica macOS/Avalonia, milestone 1
- Core matematico §2 (formule, costanti, LUT 4096, early-out, view math).
- CPU engine §3: 4 path, tiling pixel-identical (test), benchmark double+float.
- UI §5 + dialoghi §6 (benchmark/export/video/log/about), settings §7, CLI §6.6.
- GPU Metal: solo contratto + fallback CPU (§4.9); MSL in milestone 2.
- Verificato: 10/10 test, `--bench-cpu [float]` + CSV, publish osx-arm64 single-file.

# TODO

- [x] Milestone 2 — GPU reale (OpenGL 4.1, non Metal: SharpMetal 1.1.0 restituisce
      oggetti dangling dal completion handler; Metal M1 senza FP64 comunque).
      GLSL render+bench float, LUT texture, FBO riusati, gate singola,
      TuneBlocks via split-tiles + margine 1.5%, fence 60s, VRAM pre-check,
      preview/export/benchmark/video su GPU attiva, fallback CPU §4.9.
- [ ] Verifica visuale manuale completa (tooltip, chart benchmark, video MP4).
- [ ] Icona: app.ico/app.png ok; app.icns solo risorsa (bundle .app futuro).
- [ ] Chiarire con autore SPEC le 3 discrepanze in QUESTIONS.md.
- [ ] Verifica visuale manuale UI (layout 1152x720, tooltip, menu, chart).
- [ ] `ffmpeg` via brew per test video MP4 (fallback PNG già ok).
- [ ] Icona app.ico dedicata (ora placeholder Avalonia).
- [ ] Chiarire con autore SPEC le 3 discrepanze in QUESTIONS.md.

# QUESTIONS (per autore SPEC)

1. `AutoIterForScale(9.36)`: testo dice ≈1944, la formula dà 1011
   (1.5/4.68=0.3205, log10=−0.494, 2000×0.506=1011). Implementata la formula
   (benchmark 10915 torna esatto). Quale valore è corretto?
2. Early-out, esempio `c = 0.3`: è fuori dal cardioid (max 0.25) e diverge entro
   256 iter → non nero. Implementata matematica esatta. Esempio da correggere?
3. Vista iniziale `scale = 9.36` vs `re in [-2.68, 1.68]` (larghezza 4.36):
   implementato scale come larghezza complessa. Range da correggere?
