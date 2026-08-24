# Posit Pipeline Specification — Aug 24, 2026 (C#-Direct)

> **Canonical spec. All code in the pipeline must comply.**
> Dafny dropped Aug 24. Pipeline is now 3 phases: Architecture → C# Implementation → QA.

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

**Correction loop (future):** `dotnet build` compiler errors → feed back to model → retry (max 4).
For now, single-shot model generation with static checker pre-flight.

**Static checker** scans for:
- Dafny runtime types (Dafny., ISequence, BigRational, UnicodeFromString)
- Missing namespace/class declarations
- Markdown fences in output

**Files written:**
- `staging/<session>/src/<Name>.cs` — C# implementation files
- `staging/<session>/src/Wire.cs` — auto-generated wiring (deterministic)

### Phase 3: QA

**Input:** SourceCodeBundle + ArchitectureContract (test cases)
**Output:** `QaResult` (test pass/fail per test case)

The QA model generates test input data matching each test case description.
The Docker harness:
1. Builds the C# project (`dotnet build`)
2. Runs each test case with generated input
3. Checks output against expected behavior shape

**Build failures** → WireFixer (fixes C# wiring/type mismatches)
**Test failures** → WireFixer (fixes type conversion/serialization)

Retry loop: max 6 attempts.

## What We Keep

- **Pattern Registry** (`patterns/`) — C# stub templates for io-shell components
- **Trial Specs** (`wiki/trials/trial-specs.md`) — T1-T24, Tier 0-3
- **Contracts** (`src/Posit.Contracts/`) — ArchitectureContract, Component, ConnectionSpec
- **Core** (`src/Posit.Core/`) — SessionState, FsmReducer, DependencyGraph
- **Data** (`src/Posit.Data/`) — ArtifactRepository, StateStore, DB persistence
- **AI** (`src/Posit.AI/`) — OllamaModelGateway
- **Database** — PostgreSQL 18 + pgvector

## What We Dropped (Aug 24)

- **Dafny** — all Dafny phases, Z3 verification, DafnyFixer, PatternRegistryComposer
- **DafnyRuntime** — pre-built DLL with Dafny C# types (ISequence, BigRational, etc.)
- **Dafny patterns** — `.dfy` pattern files and Dafny stubs
- **Static checker Dafny rules** — function ban, invariants, C#-isms in Dafny
- **Type conversion** — ISequence<Rune> ↔ string, UnicodeFromString, all Dafny runtime conversions

## File Layout

```
staging/<session>/
  interfaces/     # C# interface files (from Architecture phase)
  src/            # C# implementation + Wire.cs (from C# Implementation phase)
  tests/          # Test input data (from QA phase)
```

## Types

All types are native C#:
- `string` — text
- `int`, `long` — integers
- `double` — floating point
- `bool` — boolean
- `string[]` — array of strings (e.g. file lines)
- `List<T>` — dynamic collection

No Dafny types. No `ISequence<Rune>`. No `BigRational`. No `BigInteger` (use `long`).

## Wiring

`WiringGenerator` reads C# method signatures and generates `Wire.cs` deterministically:
- Linear chaining: output of step N → input of step N+1
- Type conversion: native C# only (string→int via `int.Parse`, string[]→string via `string.Join`, etc.)
- Stub calls: ReadLines → `File.ReadAllLines`, ReadFile → `File.ReadAllText`
- Final step: `Console.WriteLine(result)`