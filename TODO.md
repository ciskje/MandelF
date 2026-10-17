# TODO.md — change tracker

Every requested change is classified **first** by scope, then appended here
before implementing:

- `generic` — shared intent: implement in **every** environment (each on its own
  machine/build). The row is done only when all environments have it.
- `windows-c` / `macos-avalonia` / `macos-swift` — environment-only: implement
  (and rebuild the app) only where relevant; others ignore the row.
- Implement only for the current platform, update the row; Done rows are
  archived (deleted here) at each release — history lives in tags and
  release notes, not in this file.
- Generic intents are also mirrored in `AGENTS.md` (§1–§8).

Legend per row: `win-c:[x]` / `mac-avalonia:[x]` / `mac-swift:[x]` = shipped
in that environment.

## Pending

- [ ] `macos-avalonia`: fresh screenshots (main window + benchmark; no
  `screenshots/` in the repo) + manual visual checks (tooltip, chart,
  video MP4 — `ffmpeg` present at `~/.local/bin/ffmpeg`). Requires a GUI session.
- [ ] publish `macos-avalonia-v7.3.1` / `macos-swift-v7.3.1` /
  `macos-python-v7.3.1` (tags + `dist/` + GitHub releases; local 7.3.1
  builds verified on this Mac 2026-10-17, see Done below).

## Done

- [x] `macos-avalonia`: v7.3.1 intent parity verified on this Mac —
  `dotnet build` 0 warnings, `dotnet test` 19/19, `--diag-gpu` GPU-ready
  (Apple M1, test render ok), `--bench-cpu` 0.3 Mpixel/s (= documented M1
  value), §1.3 file map complete. Known gaps: AVX512 N/A on ARM (by
  design, `Vector<T>` uses AdvSIMD); manual UI checks still pending (above).
- [x] `macos-swift`: 7.3.1 build verified on this Mac — 0 warnings (one-line
  `var`→`let` fix in `GpuEngine.swift`), SelfCheck 27/27, `MandelCLI --shot`
  ok. Screenshots already fresh (`screenshots/`, today).
- [x] `macos-python`: 7.3.1 version coherence (`VERSION` + shim header),
  headless CPU render smoke ok, `auto_mi` spot values 1011/10915 exact.

## Done

- [x] `generic`: bump `7.3.0` → `7.3.1` (ponytail-audit refactor, lockstep all envs) — `win-c:[x]` / `mac-avalonia:[x]` source + tests green on Windows; `mac-swift:[ ]` `windows-python:[ ]` `macos-python:[ ]` rebuild/publish on their machines.
- [x] `windows-c`: publish `windows-c-v7.3.0` GitHub release with setup exe
  (`dist/MandelF-Setup-7.3.0-win-x64.exe`, 47 MB, built + install-compiled
  on Windows; tag moved to release commit).
- [x] `windows-python`: publish `windows-python-v7.3.0` (win64 zip
  `MandelF-python-7.3.0-win64.zip`, 139 MB: fixed `base_dir` pre-rename bug
  + win32 anti-bloat slim in `.spec`; smoke-tested 15 s; tag moved to
  release commit).

## Done

- [x] `windows-c`: adopt the repo layout scheme (`packaging/` + `dist/`
  for the setup exe, scripts updated; built on Windows:
  `dist/MandelF-Setup-7.3.0-win-x64.exe`, 3-part name via .iss strip).
- [x] `windows-python`: CUDA backend detection verified on Windows (CuPy
  14.2 `is_available=True`, 1 device RTX 4070 SUPER, f32+f64 kernels
  compile, 320x200 render OK; ex-MandelPy issue #4 not reproduced —
  no code change).

- [x] `macos-python`: publish `macos-python-v7.3.0` (DMG built on this Mac,
  release with asset on GitHub).

- [x] `macos-swift`: benchmark chart with windows-c reference bars
  (Risultato + CUDA/DirectX/CPU, no-overlap layout).
- [x] `macos-swift`: full SPEC CPU parity + Metal GPU v0.3.0 (MSL float
  render+bench, params 88, LUT texture, autotune, gate, fallback; UI engine
  row, bench/export/video/log/CLI wired; SelfCheck incl. GPU smoke test).
- [x] `macos-avalonia`: .NET 8 SDK installed, `dotnet build` 0 warnings,
  `dotnet test` 19/19.
- [x] `macos-swift`: full SPEC CPU parity v0.2.0 (4 paths, LUT, SSAA, tiles,
  Julia, history, zones/favorites, settings, export, benchmark+CSV, video,
  back-to-set, log, about, CLI subset; SelfCheck green).
- [x] `generic`: monorepo bootstrap — `windows-c` imported from MandelC
  (history preserved), `macos-avalonia` copied from MandelMET, root
  AGENTS/TODO/README/LICENSE/gitignore/gitattributes plus SPEC to root.
- [x] `macos-swift`: native SwiftUI port v0.1.0 (CPU scalar, pan/zoom/iter,
  SelfCheck green, release artifact in `macos-swift/dist/`).
