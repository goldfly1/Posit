# Posit Pipeline Specification — Aug 26, 2026 (C#-Direct)

> **Canonical spec. All code in the pipeline must comply.**
> Dafny dropped Aug 24. Pipeline is 3 phases: Architecture → C# Implementation → QA.

## Scope

This document specifies the C#-direct pipeline: every phase from architecture
through Docker test, every file name, every type, every path.

## Pipeline Phases

### Phase 1: Architecture

**Input:** User spec text
**Output:** `ArchitectureContract` (JSON, stored in DB)

The architect model decomposes the spec into 2-3 components:
- **io-shell** — handles I/O (file read, stdin, console output)
- **logic** — pure logic, no I/O

For each logic component, the architect writes a **C# interface** (`I<Name>.cs`).
This interface IS the carapace — the structural contract the implementation must satisfy.

Method signatures use **native C# types only**: `string`, `int`, `bool`, `string[]`, `long`, `double`.

The architect also defines:
- **Connections** — linear chain: io-shell reads input → passes to logic → prints result
- **Test cases** — description + expected behavior shape (not specific values)
- **Stub selection** — which C# stub templates to use for io-shell components

**Files written:**
- `staging/<session>/interfaces/I<Name>.cs` — C# interface files for logic components

### Phase 2: C# Implementation

**Input:** ArchitectureContract (with C# interfaces)
**Output:** `SourceCodeBundle` (C# source files, stored in DB)

For each logic component, the model:
1. Reads the C# interface
2. Writes a class implementing the interface: `class <Name> : I<Name>`
3. Implements every method with correct logic
4. Uses only native C# types — no Dafny runtime
5. **Must declare `namespace <ComponentName>`** — the class and interface must share a namespace

**Build correction loop (4 attempts):**
1. Model generates C# implementation (temperature 0.2)
2. StaticChecker pre-flight: no Dafny runtime types, no markdown fences, has namespace, has class/interface
3. `dotnet build` in temp project (interface + impl + minimal .csproj)
4. If build fails: extract `error CS` lines → feed back to model → retry (temperature 0.3, max 4 attempts)

**Static checker** scans for:
- Dafny runtime types (`Dafny.`, `ISequence<`, `BigRational`, `UnicodeFromString`)
- Missing namespace declaration
- Missing class/interface declaration
- Markdown fences (```) in output

**Wiring (deterministic, no model call):**
- `WiringGenerator.Generate` reads C# method signatures + connections
- Emits `using <ComponentName>;` for each referenced component's namespace
- Logic components (instance classes): emits `var inst_X = new Namespace.Class();` then calls instance methods
- Io-shell stubs (static classes): calls directly (e.g. `File.ReadAllLines(path)`)
- Type conversion: native C# only (string→int via `int.Parse`, string[]→string via `string.Join`, etc.)
- Final step: `Console.WriteLine(result)`

If the deterministic generator returns empty, falls back to `ModelWiringGenerator` (LLM-based).

**Files written:**
- `<ComponentName>/<ComponentName>.cs` — C# implementation files (model-generated)
- `<ComponentName>/Wire.cs` — auto-generated wiring (deterministic)
- `<ComponentName>/<ComponentName>Extern.<stubName>.cs` — io-shell stub files (from templates)

### Phase 3: QA

**Input:** SourceCodeBundle + ArchitectureContract (test cases)
**Output:** `QaResult` (test pass/fail per test case)

The architect **frames** the test — test case descriptions with expected behavior prose.
The QA model **generates concrete input + derives expected output** from the architect's frame:

1. QA model generates 3-6 test cases (valid input, edge case, invalid input, empty input)
2. Each test case includes: fileName, content (concrete input), expectedOutput (exact stdout),
   expectedExitCode (0 or 1)
3. Model may return a bare JSON array `[...]` or a wrapped object `{"testData":[...]}` — deserializer handles both

The Docker harness:
1. Creates test data files from QA output (matched by index to test cases)
2. Builds the C# project (`dotnet build` in Docker)
3. Runs each test case with generated input
4. **Compares output EXACTLY** (whitespace-trimmed) against expectedOutput + expectedExitCode
5. Falls back to fuzzy comparison (keyword/shape matching) only when no expected output is available

**Exact comparison catches real bugs that fuzzy matching rubber-stamps.**
Example: program outputs `[]` (empty array) instead of `[{"name":"Alice",...}]`.
Fuzzy matcher passes (output starts with `[`). Exact comparison fails.

**Build failures** → WireFixer (fixes C# wiring/type mismatches)
**Test failures** → WireFixer (fixes type conversion/serialization)
Retry loop: max 6 attempts.

**Note:** WireFixer can only fix wiring, not implementation bugs. If the C# implementation
produces wrong output (e.g., empty array), the fix belongs in the C# implementation phase,
not WireFixer.

**Fallback:** If AI test data generation fails, the harness uses the architect's test case descriptions
or its own deterministic `GenerateTestData` heuristic.

## What We Keep

- **Pattern Registry** (`patterns/`) — C# stub templates for io-shell components
- **Trial Specs** (`wiki/trials/trial-specs.md`) — T1-T24, Tier 0-3
- **Contracts** (`src/Posit.Contracts/`) — ArchitectureContract, Component, ConnectionSpec
- **Core** (`src/Posit.Core/`) — SessionState, FsmReducer, DependencyGraph
- **Data** (`src/Posit.Data/`) — ArtifactRepository, StateStore, DB persistence
- **AI** (`src/Posit.AI/`) — OllamaModelGateway
- **Database** — PostgreSQL 18 + pgvector
- **Wiki** — C# language reference (`csharp-reference/*` in Postgres wiki chunks), historical Dafny docs as derivation chain

## What We Dropped (Aug 24)

- **Dafny** — all Dafny phases, Z3 verification, DafnyFixer, PatternRegistryComposer
- **DafnyRuntime** — pre-built DLL with Dafny C# types (ISequence, BigRational, etc.)
- **Dafny patterns** — `.dfy` pattern files and Dafny stubs
- **Static checker Dafny rules** — function ban, invariants, C#-isms in Dafny
- **Type conversion** — ISequence<Rune> ↔ string, UnicodeFromString, all Dafny runtime conversions

## File Layout

```
<ComponentName>/
  <ComponentName>.cs              # C# implementation (model-generated, logic components)
  Wire.cs                         # Auto-generated wiring (deterministic, CLI components only)
  <ComponentName>Extern.<stub>.cs # Io-shell stub files (from templates)
```

In the Docker harness build context:
```
<temp-dir>/
  <ComponentName>/
    <ComponentName>.cs
    Wire.cs
    ...
  PositGenerated.sln
  Dockerfile.run
  testdata_tc1.csv               # Test data files from QA phase
  testdata_tc2.txt
  ...
```

## Types

All types are native C#:
- `string` — text
- `int`, `long` — integers
- `double` — floating point
- `bool` — boolean
- `string[]` — array of strings (e.g. file lines)

No Dafny types. No `ISequence<Rune>`. No `BigRational`. No `BigInteger` (use `long`).

## Model Routing

All 3 phases use `deepseek-v4-flash:cloud`.

## Codebase Metrics (Aug 26)

- **Core pipeline (src/):** 81 files, 4,513 LOC, 763 comment lines (9.9% comment ratio)
- **Largest file:** Program.cs (275 LOC)
- **No file exceeds 300 LOC**
- **9 projects:** Posit.Cli, Posit.Phases, Posit.Contracts, Posit.Core, Posit.Data, Posit.AI, Posit.Tools, Posit.Dt, Posit.Web