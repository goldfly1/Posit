# Plan: Interfaces as the Carapace + Wiki Search for All Phases

**Date:** Aug 24, 2026
**Status:** Proposed
**Depends on:** Gateway JSON fix (committed `284519c`)

## Problem

Two structural gaps are blocking progress:

1. **No skeleton.** The architect outputs JSON with method signatures. PatternRegistry composes .dfy skeletons from cut-out patterns. But `patternName` is now always `null` for dafny components, so `ComposeSkeletons` skips entirely — no .dfy file written. DafnyImpl has no interface to implement against. The model writes Dafny from scratch with only responsibility text and method names.

2. **Wiki search is single-phase.** Only DafnyImpl uses the 3,675 indexed Dafny stdlib chunks. Architecture, DafnyFixer, WireFixer, and C# Implementation all fly blind.

## Goal

The architect writes the Dafny interface itself — `module`, `abstract type`, `datatype`, method signatures with `requires`/`ensures`, `{:extern}` portals. This becomes the .dfy file on disk. The carapace is no longer a concept applied by the orchestrator — it IS the interface the architect produces. Every phase that touches a model gets wiki search for relevant examples.

## Changes

### 1. ArchitectureContract — new field

**File:** `src/Posit.Contracts/Artifacts/ArchitectureContract.cs`

Add `DafnyInterface` field to `Component`:

```csharp
/// <summary>
/// The Dafny interface definition written by the architect.
/// Contains: module declaration, abstract types/datatypes, method signatures
/// with requires/ensures contracts, {:extern} portals for I/O.
/// This IS the carapace — written to .dfy on disk, enforced at every phase boundary.
/// Null for io-shell components (they use stub names + C# templates).
/// </summary>
public string? DafnyInterface { get; init; }
```

### 2. Architect prompt — write the interface

**File:** `src/Posit.Cli/Orchestration/PromptBuilder.cs`

The architect prompt changes to ask the model to produce, for each dafny component, a Dafny interface block. The JSON output gains a `dafnyInterface` field on each component.

What the interface contains:
- `module <Name> { ... }`
- `abstract type` or `datatype` declarations for component-specific types
- Method signatures with `requires`/`ensures` contracts (bodyless — no implementation)
- `{:extern}` method declarations for I/O portals (ReadLines, PrintLine, etc.)
- `include` directives for shared type modules (e.g., `include "result.dfy"`)

What the interface does NOT contain:
- Method bodies (DafnyImpl fills those)
- `function` definitions (those are implementation helpers, not interface)
- C# code

The prompt includes wiki search results for relevant Dafny patterns so the architect sees how interfaces are written in the stdlib.

Example interface the architect would produce:
```dafny
module LogFilterCounter {
  // No extern portals needed — pure logic module
  method FilterAndCount(lines: seq<string>, filterLevel: string) returns (summary: string)
    requires |lines| >= 0
    ensures summary != null
  // test: empty file → "No entries"
  // test: filter ERROR → "ERROR: 2"
}
```

### 3. ArchitecturePhase — write interface to disk

**File:** `src/Posit.Phases/ArchitecturePhase.cs`

`ComposeSkeletons` changes:
- For components with `DafnyInterface` != null: write the interface text to `.posit/staging/<session>/dafny/<Name>.dfy`
- For components with `patternName` != null (legacy cut-outs): keep existing ComposeSkeleton path
- For io-shell: skip (no .dfy needed)

Remove `CheckCutOutTypes` entirely — dead code with no cut-outs.

### 4. ContractScanner — rewrite for interface validation

**File:** `src/Posit.Phases/ArchitecturePhase.cs` (inline) or separate file

Current: validates pattern names + stub names against registry.
New checks:
- **Structure:** `DafnyInterface` contains `module <Name>` matching component name
- **Method coverage:** every method in `MethodSignatures` appears in the interface
- **Extern portals:** if component has stub dependencies, interface declares `{:extern}` methods for them
- **Registry name validation:** stub names still checked against registry (registry stays as reference)
- **No bodies:** interface methods are bodyless (no `{ ... }` with statements — only signatures + contracts)

### 5. DafnyContracts phase — verify the interface

**File:** `src/Posit.Phases/DafnyContractsPhase.cs` (if exists) or ArchitecturePhase

The .dfy interface file written by the architect gets Z3-verified. Bodyless methods with `{:axiom}` or `{:extern}` should pass Z3 (contracts are sound). This is the gate: if the architect's contracts are inconsistent, Z3 catches it here, before DafnyImpl ever runs.

### 6. DafnyImpl — read interface from disk (already works)

**File:** `src/Posit.Phases/DafnyImplementationPhase.cs`

Already reads the skeleton from disk (line 351-354) and injects it as "INTERFACE DEFINITION" in the prompt. With the architect now writing the interface, the skeleton file exists. No change needed here — it just works.

### 7. Wiki search — wire into all phases

| Phase | Search trigger | Query | Limit |
|-------|---------------|-------|-------|
| **Architecture** | Pre-generation | Spec text + component responsibility | 3 |
| **DafnyImpl** | Pre-generation (existing) + post-error (existing) | Responsibility + signatures / Z3 error | 3 / 2 |
| **DafnyFixer** | Pre-generation | Z3 error + module name | 2 |
| **WireFixer** | Pre-generation | Compile error keywords | 2 |
| **C# Implementation** | Pre-generation | Component responsibility + ISequence | 2 |
| **QA** | Not needed — QA uses test cases, not Dafny | — | — |

Implementation: each phase creates a `WikiSearcher` instance (or receives one via DI) and calls `SearchAsync` before building the prompt. Results injected as "REFERENCE EXAMPLES" block, same format as DafnyImpl does today.

### 8. StaticChecker — update for interface-aware validation

**File:** `src/Posit.Phases/StaticChecker.cs`

Current: checks Dafny code for C#-isms, function/method violations, missing invariants.
Add: check that DafnyImpl output preserves the interface structure from the .dfy file — same module name, same method signatures, same extern declarations. If DafnyImpl changed the interface, flag it (carapace enforcement).

### 9. Dead code cleanup

- `CheckCutOutTypes` — remove
- `PatternRegistry.ComposeSkeleton` calls in ArchitecturePhase — keep for legacy but dead path
- Pseudocode re-reduction escalation in DafnyImpl — remove (pseudocode phase is disabled, re-reduction can never produce output)
- `ExtractPseudocodeForComponent` — remove or make it return null (no pseudocode phase)

## Execution Order

1. **ArchitectureContract** — add `DafnyInterface` field
2. **Architect prompt** — ask for interface, add wiki search
3. **ArchitecturePhase** — write interface to disk, remove CheckCutOutTypes
4. **ContractScanner** — rewrite for interface validation
5. **Wiki search** — wire into DafnyFixer, WireFixer, C# phase
6. **StaticChecker** — interface preservation check
7. **Dead code cleanup** — pseudocode re-reduction, CheckCutOutTypes
8. **Build + test** — `dotnet build` clean, run T8

## Risk

- **Architect model (flash) may struggle with Dafny interface syntax.** Flash is fast but not a Dafny expert. The wiki search should help it see real interfaces. If flash can't write valid Dafny interfaces, we may need pro for architecture too — but that's slower and pro fails at JSON (architecture output is JSON).
- **JSON output size.** The architect already outputs large JSON. Adding a full Dafny interface per component increases payload. With 2-3 components this is manageable. With 10+ it could overflow.
- **Contract quality.** The architect writes `requires`/`ensures` — if these are wrong, Z3 passes (consistent) but the program is cotton candy. Same risk as before, but now the contracts are explicit Dafny, not implicit pattern contracts. Easier to audit.

## What Does NOT Change

- Z3 is the judge — always
- The .dfy file on disk is the carapace — now the architect writes it instead of the registry
- Pattern registry stays as a reference for names and examples (not for skeleton composition)
- I/O stub caps stay (C# templates for file-io, console-io, etc.)
- Per-phase model routing stays (flash=arch, pro=Dafny)
- Bot harness stays (Docker build + test)
- WiringGenerator stays (deterministic C# wiring from connector specs)