# Current Abilities — Aug 26, 2026 (C#-Direct)

## What Posit Is

A C#-direct spec compiler. Natural language spec in → working C# program out. No Dafny, no Z3, no formal verification. The architect writes C# interfaces (the carapace). The model writes C# implementations. `dotnet build` is the compile gate. Docker tests are the behavioral gate.

## Pipeline (3 phases)

```
Architecture → C# Implementation → QA → Docker Harness
```

1. **Architecture** — model decomposes spec into 2-3 components, writes C# interfaces, defines test cases, connections, stub selection.
2. **C# Implementation** — model writes C# class implementing the interface. Build correction loop: StaticChecker → `dotnet build` temp project → feed compiler errors back → retry (max 4). Deterministic WiringGenerator emits Wire.cs from scanned signatures.
3. **QA** — model generates test data. Docker harness builds and runs the program. WireFixer correction loop (6 retries) fixes compile errors and test failures. ImplFixer (3 retries) regenerates component code on test failure.

## What's Built

### Core Infrastructure
- 9 .NET 10 projects: Posit.Cli, Posit.Phases, Posit.Contracts, Posit.Core, Posit.Data, Posit.AI, Posit.Tools, Posit.Dt, Posit.Web
- 4,454 LOC, 20 source files. No file exceeds 613 LOC.
- PostgreSQL 18 + pgvector for artifacts, session state, prompt logging
- Docker harness for build + run + test
- WikiSearcher (semantic search over C# reference chunks)

### Correction Loops
- **Build correction loop** (C#Impl phase) — 4 attempts: model generates C# → StaticChecker → `dotnet build` → feed CS errors → retry
- **WireFixer** — 6 retries: fixes Wire.cs compile errors and test failures
- **ImplFixer** — 3 retries: regenerates component code with test failure feedback when WireFixer can't fix it

### Stubs and Patterns
- 16 C# stub templates (console-io, file-io, network-io, database-io, stream-io, time-random, banking, cicd, ecommerce, healthcare, etc.)
- Deterministic WiringGenerator reads C# interfaces → emits Wire.cs without model

### Wiki
- 9 C# reference chunks in Postgres (keywords, types, interfaces, generics, strings, classes, operators, statements, builtin-types)
- Historical Dafny docs preserved as derivation chain (not active)

## Trial Results (C#-Direct)

| Trial | Spec | Status | Notes |
|-------|------|--------|-------|
| T1 (CSV→JSON) | Linear data flow | ✅ 3/3 Docker tests | Deterministic wiring works first attempt |
| T6 (Temperature Converter) | Pure computation | Not run | |
| T8 (Log Analyzer) | Filter + aggregate | Not run | |
| T12 (Config Merger) | Multi-input, conflict resolution | Not run | |

Only T1 has been run end-to-end in the C#-direct pipeline. T6, T8, T12 are defined and ready.

## What This Covers

Business software: CLI tools, data transformers, parsers, validators, schedulers. The pipeline generates C# classes implementing architect-defined interfaces, wires them together, and tests them in Docker.

## What This Does NOT Cover

- GPU rendering / graphics
- Real-time frame loops
- Audio/video processing
- Desktop GUIs (CLI only)
- Formal verification (Dafny dropped Aug 24 — no Z3 proof)
- Multi-threaded/concurrent code (not tested)

These are future expansion targets, not current capabilities.

## Model

All 3 phases use `deepseek-v4-flash:cloud` via Ollama. No per-phase model differentiation yet.

## See Also

- `wiki/pipeline-spec.md` — canonical 3-phase pipeline spec
- `wiki/carapace-doctrine.md` — C# interface as carapace
- `wiki/handoff-2026-08-26.md` — latest handoff
- `AGENTS.md` — project context and locked decisions
- `wiki/trials/trial-specs.md` — trial definitions (T1-T12)