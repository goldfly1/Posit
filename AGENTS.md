# AGENTS.md

## Project

**Posit** — a spec compiler. The architect posits contracts (requires/ensures). Z3 confirms or denies. The code that survives is proven. Nothing ships unproven.

## Repos

- **Posit:** `C:\Users\goldf\Posit\` — this repo
- **Shepherd (reference):** `C:\Users\goldf\orch\` — working pipeline with Dafny phase, QA budget fix, wiki search

## Status

18+ commits, 8 projects, build clean (0 errors, 0 warnings). 6 of 11 phases built. Pipeline runs end-to-end with DB persistence. Data capture live (prompts_log, audit_events, artifacts, sessions).

## Git

- Git repo root: `C:\Users\goldf\Posit\` (separate from Shepherd)
- Remote: `https://github.com/goldfly1/Posit.git`
- Branch: `master`

## Toolchain

- **.NET SDK 10.0.302** — target framework `net10.0`
- **Dafny:** 4.11.0 at `C:\Users\goldf\.dotnet\tools\dafny.exe`
- **Z3:** 4.12.1 at `C:\Users\goldf\.dotnet\tools\z3\bin\z3.exe`
- **Ollama:** localhost:11434 — all model calls go through here
- **PostgreSQL 18 + pgvector:** Docker container on port 5434, `shepherd` database (shared with Posit for wiki)
- **Shell:** git-bash (MSYS), POSIX syntax. NOT PowerShell.
- **Kill dotnet:** `powershell.exe -Command "Get-CimInstance Win32_Process -Filter \"Name='dotnet.exe'\" | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }"`

## Locked Decisions

1. **Ollama-only** — all model calls through localhost:11434. No provider abstraction. `:cloud` suffix is just an Ollama tag.
2. **Models:** `deepseek-v4-pro:cloud` (Architecture, Dafny Implementation), `kimi-k2.7-code:cloud` (Design Review, Imp Appeal — proxy responds but not in /api/tags), `glm-5.2:cloud` (C# Implementation, QA), `deepseek-v4-flash:cloud` (Approach 4 generation engine)
3. **Bodyless methods with `{:axiom}`** — skeletons have no method bodies. Empty `{ }` fails verification.
4. **`{:extern}` portals** — I/O stubs in Dafny. Z3 assumes contract. `dafny translate cs` produces `partial class` holes.
5. **Two-pass implementation** — Pass 1: Dafny bodies (dsv4pro). Pass 2: C# plugs into extern portals (glm).
6. **C# only** — multi-target is future. No target-language abstraction.
7. **Determinism is target-specific** — not a core property.
8. **Skeleton correction loops back to Architecture** — max 2 loopbacks, then downgrade to io-shell.
9. **Mixed modules split at Architecture** — architect outputs two Component records.
10. **Imp appeal for io-shell only** — Dafny modules have Z3 as judge, no appeal. Kimi reviews appeals (max 1 per module).
11. **Architect writes the tests** — the architect is enjoined to write test cases for every module. These are the acceptance criteria. QA tests against the architect's test cases and the module's public surface, not against the full architecture. This minimizes the QA context snowball — QA doesn't need the whole design, just the test cases and the skeleton for namespace access. Test cases are hung on the skeleton (`// test: ParseLine("a,b,c") returns ["a","b","c"]`) and carried in the `Component.TestCases` field.
12. **Skeleton is the carapace** — the .dfy file on disk is the authority. The artifact carries the path, not the content. Names, types, contracts, dependencies are tattooed on the carapace. No guessing, no making it up. Imp inlays the function within the pre-cut shape.
13. **Pattern registry** — the architect selects from a pre-cut registry of hull shapes (patterns) and I/O portals (stubs), not invents from scratch. 17 patterns + 6 stub files, all Z3-verified (116 VC, 0 errors). `PatternRegistry.ComposeSkeleton()` bolts stubs onto patterns. 2 from column A, 6 from column B.
14. **Approach 3 — pre-cut planks not blanks.** Patterns ship with Z3-proven method bodies. The architect selects a pattern and sets parameters. The pipeline composes the skeleton with bodies already in place. Dafny Implementation pre-verifies the skeleton — if Z3 passes, it translates directly without calling Imp. T1 run: 1m27s, 14K tokens, 2 model calls, Imp NOT called.
15. **Registry vector DB.** `posit_registry.variants` table in Postgres (port 5434, shepherd/shepherd). Stores pattern, params, description, source_path, verified, embedding (768-dim pgvector). Searchable by semantic similarity. Scripts: `scripts/index-registry.py` (index + search + list), `scripts/generate-batch.py` (generate variants). ~1290 variant files on disk, ~755 indexed. Substitution = free variants. Model generation = `deepseek-v4-flash:cloud` via Ollama. 20/call = sweet spot. Thinking-mode bug: model sometimes produces 65K output tokens with 0 extractable code — fix: disable thinking via Ollama API.
16. **Self-review HARMFUL.** Testing showed the model rewrites correct code when asked to self-review, introducing errors. Z3 error feedback is the reliable correction mechanism. Do not add self-review steps.

## Pipeline

```
Ideation → Architecture → API Definition → Pseudocode → Design Review
  → Dafny Contracts (Z3 verifies skeletons)
  → Dafny Implementation (Pass 1: fill bodies, Z3 verify, translate cs)
  → C# Implementation (Pass 2: plug into extern portals, wire I/O)
  → QA (compile verified, test unverified)
  → Deployment → Observability → Documentation
```

## Project Structure

```
Posit/
  src/
    Posit.Contracts/     # Artifacts, enums, interfaces, IDs, DesignContext
    Posit.Core/           # FSM, state machine, dependency graph
    Posit.Data/           # DB, repositories, migrations, PromptLogger
    Posit.AI/              # OllamaModelGateway
    Posit.Phases/          # 6 phases built (Architecture, DafnyContracts, DafnyImpl, C#Impl, QA)
    Posit.Tools/           # Z3Runner, BuildJudge
    Posit.Cli/             # CLI + Orchestrator
    Posit.Web/             # Blazor dashboard (stub)
  prompts/                 # Phase prompts (copied to build output)
  migrations/              # 6 SQL migrations
  wiki/                    # Architecture, plans, contracts, handoff
```

## Data Capture

Every model call is captured to `posit_qa.prompts_log`. Every phase transition to `posit_audit.events`. Every artifact to `posit_artifacts.artifacts`. Session state to `posit_state.sessions`. Dafny results to `posit_qa.dafny_results`.

## Key Documents

- `wiki/architecture/posit-architecture.md` — full pipeline, phases, limits, model assignments, built infrastructure
- `wiki/plans/dafny-first-pipeline-plan.md` — implementation steps (1-3d done, 4-5 remaining)
- `wiki/contracts/dafny-contract-templates.md` — contract patterns + rules for the architect
- `wiki/handoff-2026-08-07.md` — handoff sheet with commit history, proven items, roadmap