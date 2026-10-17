# TODO

- [x] Milestone 2 — GPU reale (OpenGL 4.1, non Metal: SharpMetal 1.1.0 restituisce
      oggetti dangling dal completion handler; Metal M1 senza FP64 comunque).
      GLSL render+bench float, LUT texture, FBO riusati, gate singola,
      TuneBlocks via split-tiles + margine 1.5%, fence 60s, VRAM pre-check,
      preview/export/benchmark/video su GPU attiva, fallback CPU §4.9.
- [ ] Verifica visuale manuale completa (tooltip, chart benchmark, video MP4).
- [x] Icona: app.ico/app.png/app.icns sono il frattale (verificato: nessuna
  placeholder); app.icns in uso nel bundle .app via packaging/make-app-dmg.sh.
- [ ] Chiarire con autore SPEC le 3 discrepanze in QUESTIONS.md.
- [ ] Verifica visuale manuale UI (layout 1152x720, tooltip, menu, chart).
- [ ] `ffmpeg` via brew per test video MP4 (fallback PNG già ok).
- [ ] Chiarire con autore SPEC le 3 discrepanze in QUESTIONS.md.

