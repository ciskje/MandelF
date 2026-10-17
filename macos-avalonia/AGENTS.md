# AGENTS (workflow)

- Fonte normativa: `../AGENTS.md` (§1–§8: formule, costanti, contratti).
- Stack: C# 12, .NET 8, Avalonia 11, `AllowUnsafeBlocks`, `InvariantGlobalization`.
- Verifica dopo ogni modifica: `dotnet build source` (0 warning),
  `dotnet test source.tests` (19/19), CLI `--diag-gpu` / `--bench-cpu`.
  (Intermediates bin/obj su SSD via `Directory.Build.props`; non toccare.)
- Mai modificare costanti SPEC per far passare test: discrepanze → QUESTIONS.md.
- GPU: solo interfaccia §4.1 fino alla milestone Metal; mai crash, fallback CPU.
