# Plan: C#-Direct Pipeline (Drop Dafny)

**Date:** Aug 24, 2026
**Status:** Proposed
**Replaces:** Dafny intermediate language, Z3 verification, all Dafny-related infrastructure

## Problem

The Dafny phase is the bottleneck. 1/4 hit rate on T8 after 15 commits of mitigation (AutoFixDafny, StaticChecker Dafny rules, error translation, 4-attempt correction loop, wiki search, reference card, pitfalls DON'T block, method-not-function ban, role framing, prompt split). Root causes are external: model fluency in Dafny, broken CoCo/R parser, fundamental contract-quality vs. difficulty tension. No end in sight.

The contracts being proven are decorative (`requires |lines| >= 0`). Z3 proves nothing meaningful. The translation gap (`ISequence<Rune>` ↔ `string`, `BigRational`, `dtor_` mapping) adds failure surface without value.

## Thesis

The C# interface IS the rut. The architect writes a C# interface (signatures, types, records). The model writes C# implementation against it. `dotnet build` is the compile gate. Docker harness + architect's test cases are the behavioral gate. No intermediate language, no translation gap, no Z3.

## What Changes

### Pipeline: before → after

```
BEFORE (5 phases):
  Architecture → DafnyContracts → DafnyImpl → C#Impl → QA

AFTER (3 phases):
  Architecture → C#Impl → QA
```

### Phase 1: Architecture (modified)

**What stays:**
- Architect decomposes spec into components
- Component classification (io-shell vs logic)
- Method signatures, connections, test cases, entry type, branch condition
- ContractScanner (retargeted — see below)
- TypeChainChecker (already checks C# types)
- Wiki search (repoint at C# docs instead of Dafny stdlib)
- FSM, session persistence, DB artifacts

**What changes:**
- `DafnyInterface` field → `CSharpInterface` field on Component
- Architect prompt: write a C# interface instead of a Dafny interface
  - `public interface I<Name> { ... }` with method signatures
  - `public record <Name>` for data types
  - `using` directives for namespaces
  - XML doc comments for contract intent (human-readable, not machine-proven)
  - Test cases as comments (same as today)
- `ComposeSkeletons` → write `.cs` interface files to staging instead of `.dfy`
- Stub selection: same concept (ReadLines, ReadFile), but stubs are C# methods not Dafny `{:extern}` portals
- `patternName` / `PatternRegistry` skeleton composition: dead code, remove

**Architect prompt rewrite (PromptBuilder.cs):**
- Remove all Dafny syntax guidance (modules, requires/ensures, method vs function, seq<T>, set comprehension)
- Add C# interface guidance: `interface`, `record`, `IReadOnlyList<T>` instead of `seq<T>`, `int` instead of `int`, `string` stays `string`
- Keep: component decomposition rules, connection chain, stub selection, test case format, JSON output schema
- The JSON output schema changes: `dafnyInterface` → `csharpInterface`, `returnDafnyType` → remove

### Phase 2: C# Implementation (modified — now the MODEL writes the bodies)

This is the biggest change. Today CSharpImplementationPhase is **deterministic** — it takes Z3-translated Dafny C# and wires it. Without Dafny, the model must **write the implementation**.

**What stays:**
- WiringGenerator (deterministic Wire.cs generation from signatures + connections)
- TranslatedCSharpScanner (rename to CSharpSignatureScanner — scans C# for method signatures)
- Stub file generation from registry (io-shell C# stubs)
- Carapace enforcement (filenames match components)
- SourceCodeBundle output format

**What changes:**
- NEW: Model generates C# implementation for each logic component against the interface
  - Input: C# interface file (from architect), responsibility text, test cases, wiki examples
  - Output: `<Name>.cs` with `class <Name> : I<Name> { ... }` implementing the interface
  - Correction loop: `dotnet build` errors → model fixes (same 4-attempt pattern as DafnyImpl, but errors are clear C# compiler messages, not opaque Z3 parser errors)
  - StaticChecker C# rules apply (BigRational gone, but null deref, missing using, type conversion checks stay)
- `ExtractVerificationResults` → remove (no DafnyVerification artifacts)
- Dependencies: `new PhaseId("dafny-implementation")` → `new PhaseId("architecture")` (C#Impl now depends directly on Architecture)

**New sub-step order in CSharpImplementationPhase:**
1. Write interface files to staging (from architect's `CSharpInterface`)
2. Model generates implementation files (NEW — replaces Dafny translation)
3. Generate stub files (same as today)
4. WiringGenerator generates Wire.cs (same as today)
5. StaticChecker.CheckCSharp on all files (same rules, add interface-preservation check)
6. `dotnet build` as compile gate (replaces Z3)
7. If build fails → correction loop (model sees compiler errors, fixes)

### Phase 3: QA (modified)

**What stays:**
- AI test data generation from spec + architect's test cases
- Docker harness execution
- Bot harness project scaffolding
- Test file format

**What changes:**
- Remove "Z3-verified (proof IS the test)" logic — no verified modules anymore
- All components get test data + bot harness tests
- `verifiedModules` tracking → remove
- `newCutoutCandidates` / DafnyDB flywheel → remove
- Module results: `IsVerified` field → remove or always false
- Summary text: remove Z3-verified count

### Deleted phases (entire files removed)

| File | Lines | What it did |
|------|-------|-------------|
| `DafnyContractsPhase.cs` | ~? | Z3-verified architect's Dafny interface |
| `DafnyImplementationPhase.cs` | 820 | Model fills Dafny bodies, Z3 correction loop |
| `DafnyFixer.cs` | 289 | Fixes Dafny after Z3 rejection |
| `PseudocodeReductionPhase.cs` | 368 | Reduces pseudocode (already disabled) |
| `Z3Runner.cs` | 393 | Runs Dafny + Z3 verification |
| `PatternRegistryComposer.cs` | 170 | Composed Dafny skeletons from registry |
| `ContractScanner.cs` | 250 | Validated Dafny interface structure — **rewrite, not delete** |

**Total: ~2,290 lines deleted**

### Deleted/modified infrastructure

| Component | Action |
|-----------|--------|
| `Z3Runner.cs` | Delete |
| `DafnyArtifacts.cs` | Delete (DafnyVerificationResult, etc.) |
| `Posit.DafnyRuntime` project | Delete (entire project — Dafny runtime C# helpers) |
| StaticChecker Dafny rules | Delete (rules 1-9 in `CheckDafny`, `AutoFixDafny`) |
| StaticChecker C# rules | Keep (rules 1-5 in `CheckCSharp`) |
| `WikiSearcher` | Keep, repoint wiki content from Dafny stdlib → C# patterns |
| Wiki content | Replace Dafny stdlib chunks with C# pattern examples |
| `patterns/dafny-reference-card.dfy` | Delete |
| `wiki/dafny-pitfalls.md` | Delete |
| `wiki/reference/dafny-stdlib.md` | Delete |
| `wiki/reference/dafny-runtime-cs.md` | Keep (C# runtime reference still useful) |
| `wiki/reference/dafny-runtime-system-cs.md` | Keep |
| `ModelWiringGenerator.cs` | Keep (fallback for deterministic wiring) |
| `TypeChainChecker.cs` | Keep (already checks C# types) |
| `PatternRegistry.cs` | Keep stub definitions, remove Dafny pattern skeletons |
| `FsmReducer` / `KnownPhases` | Remove dafny-contracts, dafny-implementation, dafny-fix phases |
| `GetModelForPhase` | Remove dafny-implementation, dafny-fix routes |
| `DesignContext` | Remove DafnyContracts snowballing |
| `BotHarness` | Keep (Docker build + test — unchanged) |
| `OllamaModelGateway` | Keep (model calling — unchanged) |

### Contract record changes (ArchitectureContract.cs)

```
Component:
  - DafnyInterface → CSharpInterface (string?)
  - DafnyContractPath → CSharpInterfacePath (string?)
  - ReturnDafnyType on MethodSignature → remove
  - DafnyType on MethodParam → remove
  - PatternName → remove (dead)
  - IsVerified → remove
  - ParametersJson → remove (dead)
```

### StaticChecker changes (StaticChecker.cs)

```
Delete:
  - AutoFixDafny() — 4 regex fixes for Dafny syntax
  - CheckDafny() — 9 Dafny pattern rules
  - ClassifyStaticIssue() Dafny mappings

Keep:
  - CheckCSharp() — 5 C# rules (BigRational.Parse, dtor_, using Dafny, ISequence, string conversion)
  - FormatIssues()
  - ExtractBlock(), GetLineNumber() helpers

Add (optional, Phase 2):
  - CheckInterfacePreservation() — verify implementation matches interface signatures
  - Remove BigRational.Parse and ISequence rules (Dafny runtime gone)
  - Add: null deref on Nullable<T>, missing IDisposable, async void
```

### ContractScanner changes (ContractScanner.cs)

Rewrite from Dafny interface validation to C# interface validation:
- Structure: `CSharpInterface` contains `interface I<Name>` matching component name
- Method coverage: every method in `MethodSignatures` appears in the interface
- No bodies: interface methods have no implementation (just signatures)
- Stub names: still validated against registry (C# stubs)
- Remove: Dafny module/extern/contract checks

### AGENTS.md changes

- Remove "spec compiler" framing → "spec-driven code generator"
- Remove locked decisions 3-5 (bodyless methods, extern portals, two-pass implementation)
- Remove decision 7 (determinism is target-specific — now irrelevant)
- Remove decision 12 (skeleton is carapace → now C# interface is carapace)
- Remove decision 13-15 (pattern registry, pre-cut planks, registry vector DB)
- Remove decision 16 (self-review harmful — still true but Dafny-specific context gone)
- Remove decision 17 (flush make-weight — Dafny substitution variants)
- Remove decision 18 (indexer needs Z3 verification)
- Update toolchain: remove Dafny, Z3
- Update models: remove per-Dafny-phase routing
- Update status: new phase list, new trial results

## Execution Order

### Step 1: Contract changes (ArchitectureContract.cs)
- Rename `DafnyInterface` → `CSharpInterface`, `DafnyContractPath` → `CSharpInterfacePath`
- Remove `ReturnDafnyType`, `DafnyType`, `PatternName`, `IsVerified`, `ParametersJson`
- Build should break in many places — that's the map of what to change next

### Step 2: Architect prompt rewrite (PromptBuilder.cs)
- Replace Dafny interface guidance with C# interface guidance
- Update JSON output schema (`dafnyInterface` → `csharpInterface`, remove dafny types)
- Keep stub selection, connections, test cases, component decomposition

### Step 3: ArchitecturePhase changes
- `ComposeSkeletons` writes `.cs` interface files instead of `.dfy`
- Remove PatternRegistry skeleton composition path
- Remove Dafny-specific wiki search, repoint to C# patterns

### Step 4: CSharpImplementationPhase rewrite
- Add model generation step (model writes implementation against interface)
- Add `dotnet build` correction loop (replaces Z3 correction loop)
- Remove `ExtractVerificationResults` (no Dafny artifacts)
- Dependencies: `architecture` (not `dafny-implementation`)
- Keep WiringGenerator, stub generation, carapace enforcement

### Step 5: Delete Dafny phases + infrastructure
- Delete: `DafnyContractsPhase.cs`, `DafnyImplementationPhase.cs`, `DafnyFixer.cs`, `PseudocodeReductionPhase.cs`, `Z3Runner.cs`, `PatternRegistryComposer.cs`
- Delete: `Posit.DafnyRuntime` project
- Delete: `DafnyArtifacts.cs`
- Remove registrations in `Program.cs`

### Step 6: StaticChecker cleanup
- Delete `AutoFixDafny`, `CheckDafny`, Dafny rule classifications
- Keep `CheckCSharp` (remove Dafny-runtime-specific rules: BigRational, ISequence)
- Add C#-specific rules: null deref, missing using, interface preservation

### Step 7: ContractScanner rewrite
- Validate C# interface structure instead of Dafny interface structure
- Remove Dafny module/extern/contract checks

### Step 8: QA phase cleanup
- Remove Z3-verified logic, all modules get tests
- Remove DafnyDB flywheel, cut-out candidates

### Step 9: Orchestrator + FSM cleanup
- Remove dafny phases from `KnownPhases`, `GetModelForPhase`, `FsmReducer`
- Remove post-Dafny type chain check in orchestrator
- Remove `DesignContext` DafnyContracts snowballing

### Step 10: Wiki cleanup
- Delete Dafny stdlib, reference card, pitfalls
- Keep C# runtime references
- Index C# pattern examples for wiki search

### Step 11: Build + test
- `dotnet build --nologo -v q` — 0 errors, 0 warnings
- Run T6 (temperature converter) — should pass (simple C#)
- Run T8 (log filter) — should pass (maps/strings, no Dafny syntax barrier)
- Run T12 (CSV parser) — should pass

## What We Lose

- **Z3 proof.** No mathematical guarantee that implementations satisfy contracts for all inputs. Tests prove behavior at tested points only.
- **The "spec compiler" identity.** Posit becomes a spec-driven code generator, not a verified spec compiler.
- **Contract enforcement at compile time.** C# interfaces enforce signatures and types, but not preconditions/postconditions. `System.Diagnostics.Contracts` can add runtime enforcement later if needed.

## What We Gain

- **Model writes in its strong language.** No more C#-isms in Dafny — the model was already writing C#.
- **No translation gap.** No `ISequence<Rune>` ↔ `string`, no `BigRational`, no `dtor_` mapping.
- **Clear compiler errors.** `dotnet build` produces actionable C# compiler messages. No opaque "invalid UnaryExpression" from a broken parser.
- **Simpler pipeline.** 3 phases instead of 5. ~2,290 lines deleted. Half the moving parts.
- **Higher hit rate.** The model is fluent in C#. The correction loop has clear errors to work with.
- **Meaningful test gate.** Tests were always the real quality gate. Now they're the only gate, and they're honest about what they prove.

## Future Options (not in this plan)

- **`System.Diagnostics.Contracts`** — `Contract.Requires()`, `Contract.Ensures()` in interfaces. Runtime enforcement of preconditions/postconditions. Same mental model as Dafny contracts, enforced at runtime instead of by Z3.
- **Property-based testing** (FsCheck/Hedgehog.NET) — generates random inputs against postconditions. Statistical proof, covers more than unit tests.
- **Roslyn analyzers** — static analysis for null deref, async void, disposable tracking. Compile-time enforcement beyond what `dotnet build` gives.
- **Re-add Dafny later** — if model fluency improves (Dafny 5.0 parser, better training data), the interface-as-carapace concept still works. The C# interface can be augmented with Dafny contracts alongside it.