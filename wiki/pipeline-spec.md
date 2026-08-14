# Posit Pipeline Specification — Aug 14, 2026

> **This is the canonical spec. All code in the deterministic pipeline must comply.**
> The carapace doctrine governs contracts. This spec governs the code that enforces them.

## Scope

This document specifies the deterministic pipeline: every phase from architecture
through Docker test, every file name, every type conversion, every path. It is the
reference that the code must match — not the other way around.

## What We Keep (proven, untouched)

- **Pattern Registry** (`patterns/`) — 20 patterns + 6 stubs + 16 C# stub templates,
  all Z3-verified. The trireme kit.
- **DafnyRuntime** (`src/Posit.DafnyRuntime/`) — pre-built DLL, provides Dafny types.
- **Trial Specs** (`wiki/trials/trial-specs.md`) — T1-T24, Tier 0-3.
- **Contracts** (`src/Posit.Contracts/`) — ArchitectureContract, Component, ConnectionSpec,
  MethodSignature, etc. These define the carapace schema. Keep as-is.
- **Core** (`src/Posit.Core/`) — SessionState, FsmReducer, DependencyGraph. The FSM
  that routes phases and handles retry. Keep as-is.
- **Data** (`src/Posit.Data/`) — ArtifactRepository, StateStore, AuditRepository,
  PromptLogger, MigrationRunner. DB persistence. Keep as-is.
- **AI** (`src/Posit.AI/`) — OllamaModelGateway. The model interface. Keep as-is.
  (But add the CorrectionSignal injection that was fixed this session.)
- **Database** — PostgreSQL 18 + pgvector on port 5434. Schema stays.

## What We Wipe (rebuilt from scratch)

- **Posit.Phases** — ALL phase implementations (Architecture, DafnyContracts,
  DafnyImplementation, CSharpImplementation, QA). These accreted bandaids.
  Rebuild each under 200 lines with proper type tracking.
- **Posit.Tools** — Z3Runner, PatternRegistry (the C# side, not the .dfy patterns),
  BotHarness, DockerVerifier. The runtime stripping, wiring generator, scanner —
  all rebuilt.
- **Posit.Cli** — Program.cs and PositOrchestrator. The CLI wiring and carapace
  enforcement. Rebuild cleanly.
- **Posit.Dt** — Desktop UI. Not part of the core pipeline. Defer.
- **Posit.Web** — Web UI. Not part of the core pipeline. Defer.
- **Prompts** — Architecture and QA prompts need updating for the new pipeline.
- **Wiki handoffs** — All handoff-*.md are session logs, not docs. Wipe them.
  Keep carapace-doctrine, proof-methodology, connector-diagnosis, current-abilities,
  desktop-port-plan, trial-specs.
- **Scripts** — Keep registry scripts. Wipe trial scripts that reference old phases.

## The Deterministic Pipeline

```
AI (thinking):    Ideation → Architecture → Design Review
Code (no model):   Dafny Verify → Dafny Translate → C# Assemble → Docker Test
```

### Phase 1: Architecture (AI — deepseek-v4-flash:cloud)

The architect decomposes the spec, classifies components, selects patterns from
the registry, and fills the carapace: method signatures, connections, shared types.

**Output:** ArchitectureContract (JSON)

**NAMING CONVENTION (authoritative — all downstream code MUST match):**

The architect fills the carapace with names. Every name must trace to the pattern
registry or the component set. The ContractScanner enforces this BEFORE the pipeline
proceeds. This is the single point where naming is locked down — everything downstream
reads from here.

1. **patternName** — must exist in the pattern registry (e.g. "parser", not "Parse").
   The pattern's Dafny source is the authority for method names, param types, and
   return types available on that component.

2. **toMethod** — the method name called on a dependency. Must match a real method
   on the target's pattern (from the registry's ExtractMethodSignatures) OR a
   declared MethodSignature on the target component (with PatternMethod mapping).
   The architect may use semantic names ("Parse") but MUST provide PatternMethod
   ("ParseLine") if the name differs from the pattern's real method.

3. **fromMethod** — the method on THIS component that initiates the connection.
   Must match a MethodSignature name on this component.

4. **stubName** — must exist in the C# stub registry (e.g. "file-io", not "FileIO").
   The stub's C# template is the authority for the io-shell class name and methods.

5. **dependency** — must reference a real component name in this contract.

6. **MethodSignature.Name** — the architect's semantic name for a method.
   **MethodSignature.PatternMethod** — the REAL method name on the pattern
   (e.g. "HandleRequest"). If blank, the scanner assumes Name == PatternMethod.

7. **ConnectionSpec.ToComponent** — must reference a real component.
   **ConnectionSpec.ToMethod** — must match a pattern method or declared signature
   on the target. The scanner checks against the pattern registry (authoritative)
   first, then the component's MethodSignatures (fallback).

**ContractScanner** validates ALL of these against the pattern registry BEFORE
the pipeline proceeds. If any name doesn't match, reject with a listing of what's
available. Feed the listing back to the model via CorrectionSignal. Retry until clean.

**Key:** The architect's names are SUGGESTIONS. The pattern registry's names are
AUTHORITATIVE. The scanner enforces this at the design boundary. Once the contract
passes the scanner, every downstream phase reads names from the contract — no
guessing, no inventing, no mismatching.

### How Names Flow Downstream

After the scanner passes, the naming is locked. Every phase reads from the same
source:

```
ArchitectureContract (locked by scanner)
    ↓
Phase 2 (Dafny Contracts): reads patternName → composes skeleton from registry
    ↓ skeleton file has the REAL method names (from pattern .dfy source)
    ↓
Phase 3 (Dafny Implementation): translates skeleton → C# file
    ↓ translated C# has __default.Frame or __default.HandleRequest (from pattern)
    ↓
Phase 4 (C# Assembly):
    ↓ Scanner reads translated C# → gets REAL C# method signatures
    ↓ WiringGenerator reads contract connections → resolves toMethod via scanner
    ↓   if toMethod matches a scanned method → use it
    ↓   if toMethod matches PatternMethod → use the scanned method it maps to
    ↓   if no match → use pattern registry fallback (first method)
    ↓ ResolveDafnyClass reads scanner for actual class name (Frame vs __default)
    ↓ Wire.cs calls the REAL method on the REAL class
    ↓
Phase 6 (Docker Test):
    ↓ Harness loads source bundle (file names are {Module}.cs, NOT skeleton-*.cs)
    ↓ Docker builds — every name matches because they were locked at design time
```

**The chain of custody:** pattern registry → contract (scanner-validated) → skeleton
→ translated C# → scanner → wiring → Docker. No name is ever invented or guessed
at any stage. Every name traces back to the pattern registry through the contract.

### Phase 2: Dafny Contracts (deterministic — Z3)

Compose .dfy skeletons from the pattern registry. The architect selects a pattern;
the pipeline composes the file. No model call.

**Output:** .dfy skeleton files on disk + DafnyContractResult[]

**Key:** Every skeleton comes from the quarry. The carapace (skeleton file) is the
authority — names, types, contracts, dependencies are tattooed on it.

### Phase 3: Dafny Implementation (deterministic — Z3 + translate)

For pre-verified patterns (bodies already proven): skip Z3, translate directly.
For unverified: Z3 verifies, then translate.

**Translation** (`dafny translate cs --translate-standard-library:false`):
- Output: `{ModuleName}.cs` (NOT `skeleton-*.cs`)
- Post-process: extract ONLY `namespace _module_{moduleName} { ... }`
  Discard ALL runtime boilerplate (DafnyAssembly, FuncExtensions, ArrayHelpers,
  namespace Dafny). These are in DafnyRuntime.dll.
- Rename `namespace _module` → `namespace _module_{moduleName}`
- Rename internal refs `_module.` → `_module_{moduleName}.`

**Output:** One clean .cs file per Dafny module, containing only the module code.

### Phase 4: C# Assembly (deterministic — no model)

Three sub-steps, each producing files for the source bundle:

**4a: Extern Portal Caps** — For each Dafny component with `{:extern}` stubs,
materialize the C# stub template from the registry. File name: `{Module}Extern.cs`.
Content: `partial class` implementing the extern methods.

**4b: Io-Shell Stubs** — For each io-shell component, materialize the C# stub
templates. File name: `{Module}.{stubName}.cs`. Content: ConsoleIO, FileIO, etc.
NEVER materialize `io-console-program` (it's an entry point, not a stub).

**4c: Wiring** — For each component with connections, generate Wire.cs.
The wiring generator tracks ACTUAL C# types (not Dafny types):
- Scans translated C# → gets real method signatures (names, param types, return types)
- Scans io-shell stubs → gets real C# method signatures
- For each connection: resolves method name, resolves class name (`__default` or
  `Frame` — from scanner, not hardcoded), builds args with type conversion
- Type conversion at Dafny/io-shell boundary:
  `ISequence<Rune>` → `string`: `Dafny.Helpers.SequenceToString(var)`
  `string` → `ISequence<Rune>`: `Dafny.Sequence<Dafny.Rune>.UnicodeFromString(var)`
- If types incompatible: return null → emit type-appropriate default (compiles,
  doesn't segfault from a type mismatch)
- Entry param for io-shell CLI: `args[0]` (C# string), NOT `UnicodeFromString`
- Entry param for Dafny CLI: `UnicodeFromString(args[0])` (ISequence<Rune>)
- `args` collision: rename to `inputArgs` (NOT `@args` — @ is just a prefix)

**File naming (carapace-compliant):**
- `{Module}.cs` — translated Dafny source
- `{Module}Extern.cs` — extern stub cap
- `{Module}.{stubName}.cs` — io-shell stub
- `{Module}/Wire.cs` — wiring file

**Deduplication:** the 2-pass flow may add the same file twice. Dedup by path,
keep last occurrence.

**Output:** SourceCodeBundle (deduplicated, carapace-compliant filenames)

### Phase 5: QA (deterministic — no model)

Records metadata only. No test generation, no model call.
- Verified (Dafny) modules: "proof IS the test"
- Unverified (io-shell) modules: "bot harness will test"

**Output:** TestSuite (empty, metadata only)

### Phase 6: Docker Test (deterministic — BotHarness)

The bot harness IS the test. Push data through the CLI, capture output, compare
to spec.

**Steps:**
1. Load artifacts from DB (ArchitectureContract, SourceCodeBundle, TestSuite)
2. Find CLI component (the one with connections)
3. Clean temp dir (delete if exists, create fresh)
4. Materialize files from DB (deduplicated, correct names)
5. Generate .csproj for each component (isExe only for CLI component)
6. Generate .sln
7. Copy DafnyRuntime.dll to `DafnyRuntime/` subdir
8. Build in Docker: `dotnet build PositGenerated.sln -c Release`
9. Generate test data from spec
10. For each test case: build run image, run container, capture output
11. Compare output to expected, report pass/fail

**Dockerfile.run:**
```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet build PositGenerated.sln -c Release

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /src/{cliComponent}/bin/Release/net10.0/ ./
COPY DafnyRuntime/DafnyRuntime.dll ./
ENTRYPOINT ["dotnet", "{cliComponent}.dll"]
```

**Key:** The run image copies from the per-project output (includes runtimeconfig.json),
and copies DafnyRuntime.dll from the build context (not the build stage).

## Module Size Constraints

Every module under 200 lines (carapace cap). If a module needs more, split it.

### Posit.Phases (target: ~6 files, ~1200 lines total)

| File | Responsibility | Target Lines |
|------|---------------|-------------|
| `ArchitecturePhase.cs` | Model call, parse contract, compose skeletons, ContractScanner validation | 200 |
| `ContractScanner.cs` | Scan contract against registry, produce correction listing | 150 |
| `DafnyContractsPhase.cs` | Z3 verify skeletons | 100 |
| `DafnyImplementationPhase.cs` | Pre-verified check, translate, post-process | 150 |
| `CSharpImplementationPhase.cs` | Extern caps, io-shell stubs, wiring, source bundle | 200 |
| `QaPhase.cs` | Metadata only, no model | 50 |
| `WiringGenerator.cs` | C# type tracking, type conversion, Wire.cs generation | 200 |
| `TranslatedCSharpScanner.cs` | Read translated C#, extract real signatures | 150 |
| `IPhase.cs` / `PhaseController.cs` | Interface, dispatch | 50 |

### Posit.Tools (target: ~5 files, ~1000 lines total)

| File | Responsibility | Target Lines |
|------|---------------|-------------|
| `Z3Runner.cs` | Verify + translate, post-process (extract module namespace) | 200 |
| `PatternRegistry.cs` | Load patterns/stubs, compose skeletons, select C# stubs | 200 |
| `BotHarness.cs` | Materialize, build, test — split if over 200 | 200 |
| `BotHarnessProjects.cs` | Generate .csproj, .sln, Program.cs | 150 |
| `BotHarnessDocker.cs` | Docker build + run | 150 |

### Posit.Cli (target: ~2 files, ~500 lines total)

| File | Responsibility | Target Lines |
|------|---------------|-------------|
| `Program.cs` | CLI entry, command dispatch | 200 |
| `PositOrchestrator.cs` | Phase execution, FSM routing, snowball, carapace | 200 |

## CorrectionSignal (the retry loop)

The gateway injects CorrectionSignal into the prompt so the model sees errors:

```
═══ CORRECTION SIGNAL — your previous output had these errors ═══
Fix ALL of the following before resubmitting:

• scan.toMethod: component 'CsvCli' — connection toMethod 'Parse' does not
  exist on target 'CsvParser' (pattern 'parser'). Available toMethods: ParseLine,
  ParseLines, GetDelimiter, CountFields

═══ END CORRECTION SIGNAL ═══
```

The FSM handles retry: `phase.failed → retry.dispatch → re-execute` until clean
or retries exhausted.

## Known Bandaid to Remove

**io-shell auto-repair in ArchitecturePhase** — silently strips io-shell→non-io-shell
dependencies instead of rejecting them. The carapace doctrine says: reject and send
back to the architect with a listing. Remove the auto-repair; let the ContractScanner
catch it and the CorrectionSignal feed it back.

## Build Order

1. Wipe `src/Posit.Phases/`, `src/Posit.Tools/`, `src/Posit.Cli/`
2. Clean wiki handoffs, keep doctrine/methodology/trial specs
3. Rebuild Posit.Tools first (Z3Runner, PatternRegistry, BotHarness)
4. Rebuild Posit.Phases (ArchitecturePhase + ContractScanner, then Dafny phases,
   then C# phase + WiringGenerator, then QA)
5. Rebuild Posit.Cli (Program + Orchestrator)
6. Build solution, verify 0 errors 0 warnings
7. Run T1 trial end-to-end
8. Run T2-T5 sequentially as each passes

## Verification

Each module built → `dotnet build` clean (0 errors, 0 warnings).
Each phase → ad-hoc probe for the specific behavior.
Full pipeline → T1 trial: pipeline completes, Docker build succeeds, CLI runs.
No bandaids. No per-error patching. Every name, type, and path accounted for.

## Compliance

All code in the deterministic pipeline MUST comply with this spec. The carapace
doctrine says the skeleton is the source of truth for contracts. This spec is the
source of truth for the code that enforces them. If the code and this spec disagree,
the spec wins. If the spec and the doctrine disagree, the doctrine wins.