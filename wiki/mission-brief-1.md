# Posit Mission Brief — Aug 19, 2026

## What Posit Is

A spec compiler. The architect posits contracts (requires/ensures). Z3 confirms or denies. The code that survives is proven. Nothing ships unproven. C# target, Dafny intermediate.

## Pipeline (Aug 18, current)

```
Architecture → Pseudocode Reduction → Dafny Contracts → Dafny Implementation → C# Implementation → QA → Bot Harness
```

### Phase models (Ollama, localhost:11434)

| Phase | Model | Notes |
|-------|-------|-------|
| Architecture | deepseek-v4-flash:cloud | Decomposes spec, sets patternName=null for all dafny |
| Pseudocode Reduction | deepseek-v4-flash:cloud | Recursively reduces spec → Dafny fragments. Max 5 passes. No Z3. |
| Dafny Contracts | deterministic + Z3 | Verifies skeleton. No model call. |
| Dafny Implementation | deepseek-v4-pro:cloud | Writes Dafny bodies from pseudocode + signatures + dictionary. Z3 verifies. 4-attempt correction loop. |
| C# Implementation | glm-5.2:cloud | Writes Wire.cs from connections + signatures + ISequence API. |
| QA | glm-5.2:cloud | AI test data generation. Bot harness runs tests. |

**All calls through Ollama at localhost:11434. `:cloud` is just an Ollama tag. `think: false` by default (thinking mode causes 65K output runaway).**

## Key Invariants

- Skeleton is the carapace — `.dfy` file on disk is authority. Names, types, contracts, dependencies tattooed on it.
- Self-review is HARMFUL — Z3 error feedback is the correction mechanism. Do not add self-review steps.
- 17 patterns + 6 I/O stubs, all Z3-verified (116 VC, 0 errors).
- Pattern reference card (`patterns/dafny-reference-card.dfy`, 86 entries, 5.6K chars) is injected into Dafny writer + DafnyFixer + PseudocodeReducer prompts. It IS the crystallization vocabulary.
- `function` is BANNED in DafnyImpl — always use `method`. Functions are pure expressions (no loops, no mutable assignment). This eliminates the #1 Dafny parse error class.
- DafnyImpl prompt framing: "You are refactoring reduced pseudocode into valid Dafny. The pseudocode IS the algorithm. Do NOT redesign it."

## Repos

- **Posit:** `C:\Users\goldf\Posit\` — this project. Git, branch `master`, remote `github.com/goldfly1/Posit.git`
- **Shepherd (reference):** `C:\Users\goldf\orch\` — working pipeline with Dafny phase

## Key Files

| File | Purpose |
|------|---------|
| `AGENTS.md` | Project bible — locked decisions 1-29, toolchain, git, pipeline |
| `wiki/architecture/posit-architecture.md` | Full pipeline, phases, model assignments, limits |
| `wiki/handoff-2026-08-19.md` | Latest handoff (T12 GREEN, DafnyImpl correction loop, error translation) |
| `wiki/pipeline-spec.md` | Canonical spec for what the pipeline code must do |
| `wiki/trials/trial-specs.md` | Trial definitions T1-T24, Tier 0-3 |
| `wiki/carapace-doctrine.md` | Canonical carapace text |
| `wiki/connector-diagnosis.md` | Pipeline shrink rationale, connector forms |
| `wiki/current-abilities.md` | What Posit can build today, trial scorecard |
| `src/Posit.Phases/PseudocodeReductionPhase.cs` | The pseudocode reduction phase |
| `src/Posit.Phases/DafnyImplementationPhase.cs` | Consumes pseudocode, writes Dafny, Z3 correction loop |
| `src/Posit.Phases/DafnyFixer.cs` | Z3 correction specialist for Dafny |
| `src/Posit.Phases/WireFixer.cs` | C# wiring correction specialist |
| `src/Posit.Tools/Z3Runner.cs` | Z3 verify + opaque error translation layer |
| `patterns/dafny-reference-card.dfy` | Dafny language dictionary (86 entries) |
| `scripts/verify-trial.py` | Compiles a trial dir's C# output into a .NET project |

## Wiki

29 markdown docs under `wiki/`, ~29K lines total. Indexed in PostgreSQL `wiki.wiki_chunks` (port 5434, shepherd/shepherd) with pgvector embeddings. `scripts/sync-wiki-html.sh` re-indexes.

Key reference docs:
- `wiki/reference/dafny-stdlib.md` — 18,800 lines, 57 modules
- `wiki/reference/dafny-runtime-cs.md` — C# runtime
- `wiki/reference/dafny-runtime-system-cs.md` — system runtime

## Toolchain

- **.NET SDK 10.0.302** — target `net10.0`
- **Dafny 4.11.0** — `C:\Users\goldf\.dotnet\tools\dafny.exe`
- **Z3 4.12.1** — `C:\Users\goldf\.dotnet\tools\z3\bin\z3.exe`
- **PostgreSQL 18 + pgvector** — Docker, port 5434, database `shepherd`, user `shepherd`, password `shepherd`
- **Shell:** git-bash (MSYS), POSIX syntax. NOT PowerShell.
- **Kill dotnet:** `powershell.exe -Command "Get-CimInstance Win32_Process -Filter \"Name='dotnet.exe'\" | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }"`

## Running a Trial

```bash
cd C:/Users/goldf/Posit
dotnet run --project src/Posit.Cli -- run --spec="A CLI tool that reads a CSV file, parses each line into fields, validates that all rows have the same number of fields, transforms each row into a JSON object with field names from the header row, and prints the JSON array to stdout."
```

CLI commands:
- `run --spec="..."` — full pipeline + auto Docker harness + retry loop
- `harness <sessionId>` — run bot harness on existing session
- `status` — list all sessions
- `resume <sessionId>` — resume paused/failed session
- `artifacts <sessionId>` — list artifacts for session

Output per trial (in `trials/<name>/`): `architecture.json`, `dafny-verification.json`, `source-code-bundle.json`, `test-suite.json`, `csharp/` dir.

Postgres captures: `posit_qa.prompts_log` (every model call), `posit_audit.events` (phase transitions), `posit_artifacts.artifacts`, `posit_state.sessions`, `posit_qa.dafny_results`.

## Trial Status

| Trial | With cut-outs | Without cut-outs |
|-------|--------------|------------------|
| T1-T6 | ✅ 6/6 PASS | T6: Dafny ✅, wiring ❌ (casing). Others: Dafny fails on C#-isms |
| T8 | — | Dafny ❌ (map syntax + while-in-function, correction loop ran) |
| T12 | — | ✅ **GREEN** — Z3 verified attempt 4, 3/3 tests pass. First non-trivial end-to-end pass with custom Dafny |

### T12 (task scheduler) — first GREEN without cut-outs
- Architecture: ✅
- Pseudocode: ✅ crystallized at pass 2
- Dafny contracts: ✅
- Dafny: ✅ Z3 verified + translated on attempt 4
- C# Implementation: ✅
- QA: ✅
- Docker Harness: ✅ 3/3 tests PASS

Correction loop progression: attempt 1 JSON noise → attempt 2 while-in-function → attempt 3 JSON again → attempt 4 Z3 verified.

## Pseudocode Reduction (the current focus)

### How it works

`PseudocodeReductionPhase` takes method signatures from the architect and recursively reduces spec-level descriptions into Dafny-statement-level fragments. Each pass replaces English concepts with Dafny language elements from the reference card dictionary.

- Pass 0: raw spec (signature + responsibility + test cases)
- Passes 1-5: model reduces, replacing English with Dafny tokens
- Crystallization check: every substantive (non-comment, non-sig, non-test) line must contain a Dafny token from the dictionary
- Stops when crystallized, or model says STOP, or very short output (< 5 chars = model gave up)
- All passes stored in DB (PseudocodeModule artifact)
- DafnyImplementationPhase reads the last non-STOP pass as the crystallized pseudocode

### How DafnyImpl consumes it

`ExtractPseudocodeForComponent()` reads the PseudocodeReductionBundle artifact, finds the component, gets the last non-STOP pass per method, and injects it into the prompt:
```
Pseudocode to refactor into Dafny (this IS the algorithm — wrap it, don't redesign it):
// MethodName:
<crystallized pseudocode>
```

### Known issues

1. **Crystallization may be premature or noise.** Some methods crystallize at pass 1 (too fast?), others at pass 3-4. Need to inspect actual reduced output quality — is the crystallized pseudocode genuinely useful to the Dafny writer, or is it just noise that the model ignores?

2. **Per-phase model routing not built.** `GetModelForPhase()` returns one model for all phases. Need flash for architecture/pseudocode, pro for Dafny. Session resume (`posit resume <sessionId>`) allows swapping models between phases.

3. **WireFixer casing issue (T6).** Dafny datatype fields are lowercase (`isValid`, `result`) but C# properties are PascalCase (`IsValid`, `Result`). WireFixer cycles between casings, never finds the right one. Needs to see the actual translated C# type definitions.

4. **Reference card pitfalls.** Could add more syntax pitfalls (map initialization, seq angle brackets) to prevent errors before they happen.

## Git State

- Branch: `master`, up to date with `origin/master`
- Working tree: clean
- Latest commit: `be97154` — Swap wiring order: deterministic WiringGenerator primary, model fallback
- Z3Runner `--verbose` + `TranslateOpaqueErrors` is at commit `936f50e` (Aug 18). Translates opaque CoCo parser errors ("this symbol not expected", "invalid UnaryExpression") into plain-English hints the model can act on.