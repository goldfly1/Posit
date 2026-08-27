# AGENTS.md

## Project

**Posit** — a C#-direct spec compiler. Natural language spec in → working C# program out. No Dafny, no Z3, no formal verification. The architect writes C# interfaces (the carapace). The model writes C# implementations. `dotnet build` is the compile gate. Docker tests are the behavioral gate.

## The Carapace Doctrine

The C# interface is the carapace — the structural contract. The architect writes `I<Name>.cs` interface files. The model writes `<Name>.cs` classes implementing them. The orchestrator enforces that every filename traces to a component and every method is implemented. WiringGenerator reads the interfaces deterministically to generate Wire.cs.

See `wiki/carapace-doctrine.md` for the canonical text.

## Pipeline (3 phases)

```
Architecture → C#Impl → QA → Docker Harness
```

1. **Architecture** — model decomposes spec into 2-3 components, writes C# interfaces, defines test cases, connections, stub selection.
2. **C# Implementation** — model writes C# class implementing the interface. Build correction loop: static check → `dotnet build` in temp project → feed compiler errors back → retry up to 4 times.
3. **QA** — model generates test input data. Docker harness builds and runs the program. WireFixer correction loop (6 retries) fixes compile errors and test failures.

## Repos

- **Posit:** `C:\Users\goldf\Posit\` — this repo
- **Remote:** `https://github.com/goldfly1/Posit.git` — branch `master`
- **Shepherd (reference):** `C:\Users\goldf\orch\`

## Toolchain

- **.NET SDK 10.0.302** — target framework `net10.0`
- **Ollama:** localhost:11434 — all model calls go through here
- **PostgreSQL 18 + pgvector:** Docker container `shepherd-postgres` on port 5434, `shepherd` database
- **Docker:** Bot harness builds generated code in Docker containers
- **Wiki vector index:** `wiki.wiki_chunks` table — C# reference, patterns, examples indexed with pgvector embeddings. WikiSearcher does semantic search over all chunks.
- **C# language reference:** 9 chunks in wiki (`csharp-reference/keywords`, `csharp-reference/types`, `csharp-reference/interfaces`, `csharp-reference/generics`, etc.) — ingested and searchable.
- **Shell:** git-bash (MSYS), POSIX syntax. NOT PowerShell.

## Model Routing

All 3 phases use `deepseek-v4-flash:cloud` via Ollama. No per-phase model differentiation.

## Key Decisions

1. **Dafny dropped (Aug 24, 2026)** — 1/4 model hit rate, opaque CoCo/R parser errors, decorative contracts. C#-direct is simpler and the pipeline already targets C#.
2. **C# interface is the carapace** — replaces `.dfy` files. Architect writes `I<Name>.cs`, model writes `<Name> : I<Name>`.
3. **`dotnet build` is the compile gate** — compiler errors replace Z3 errors as the correction signal. Temp project with interface + impl + minimal .csproj.
4. **Docker harness is the behavioral gate** — builds generated projects, runs test data, checks output.
5. **WireFixer correction loop** — 6 retries, feeds compile errors and test failures back to the model.
6. **Native C# types only** — string, int, double, bool, string[], List<string>. No Dafny runtime types (ISequence, Rune, BigRational).
7. **Namespace = component name** — interface and implementation share `namespace <ComponentName>`. Wire.cs references `<ComponentName>.<ComponentName>`.
8. **Architect writes the tests** — test cases defined at architecture time, exercised by the Docker harness.
9. **StaticChecker guards** — pre-compile check for Dafny runtime types (ISequence, BigRational, etc.) in generated C# output.
10. **Self-review harmful** — testing showed models rewrite correct code when asked to self-review. Compiler error feedback is the reliable correction mechanism.

## Project Structure

```
Posit/
  src/
    Posit.Contracts/     # Artifacts, enums, interfaces, IDs, DesignContext
    Posit.Core/           # FSM, state machine, dependency graph
    Posit.Data/           # DB, repositories, migrations, PromptLogger
    Posit.AI/              # OllamaModelGateway
    Posit.Phases/          # ArchitecturePhase, CSharpImplementationPhase, QaPhase, WiringGenerator, WireFixer, StaticChecker, ContractScanner, TypeChainChecker
    Posit.Tools/           # PatternRegistry, BotHarness, BotHarnessDocker, BotHarnessProjects, WikiSearcher
    Posit.Cli/             # CLI + Orchestrator + PromptBuilder
    Posit.Dt/              # Data tools (trace viewer)
    Posit.Web/             # Web dashboard (minimal)
  patterns/
    csharp-stubs/          # 16 C# stub templates (console-io, file-io, etc.)
  wiki/                    # Architecture, plans, handoffs, reference docs
```

## Data Capture

Every model call captured to `posit_qa.prompts_log`. Session state to `posit_state.sessions`. Artifacts to `posit_artifacts.artifacts`. Legacy `posit_qa.dafny_results` table kept for DB compatibility.

## Key Documents

- `wiki/pipeline-spec.md` — C#-direct 3-phase pipeline spec
- `wiki/carapace-doctrine.md` — C# interface as carapace
- `wiki/plans/csharp-direct-pivot.md` — the pivot plan (11 steps)
- `wiki/handoff-2026-08-24-csharp-direct.md` — handoff from the Dafny→C# pivot session
- `wiki/trials/trial-specs.md` — trial spec definitions (T1, T6, T8, T12)