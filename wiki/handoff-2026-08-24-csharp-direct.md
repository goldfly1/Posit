# Handoff — Aug 24, 2026 (C#-Direct Pivot)

## Result: Dafny dropped. C#-direct pipeline built, compiles clean, runs end-to-end but doesn't complete trials yet.

## Session Arc

### Decision: Drop Dafny
- Root causes: model fluency external, Dafny 5.0 parser broken, contract quality vs difficulty fundamental tension
- 1/4 hit rate after 15 commits of mitigation
- User: "ok flush dafny. C# it is."

### Pivot Executed (commit `6a5055e`)
- Pipeline: 5 phases → 3 phases (Architecture → C#Impl → QA)
- Deleted 7 Dafny-only source files + DafnyRuntime project + 27 .dfy pattern files
- Rewrote: ArchitecturePhase, CSharpImplementationPhase, PromptBuilder, StaticChecker, WiringGenerator, ContractScanner, QaPhase, BotHarness/Docker/Projects, PositOrchestrator, Program.cs, KnownPhases, DesignContext, DesignContextSnowballer, IExternalAbstractions, ArchitectureContract, ArtifactEnums, ModuleClassificationConverter, PositJson
- Build: 0 errors, 0 warnings

### Wiki Restore (commit `d4bb20d`)
- Initially deleted too aggressively — user corrected: "The wiki was not exclusively dafny"
- Restored: dafny-contract-templates, dafny-pitfalls, proof-methodology, dafny-first-pipeline-plan, interface-as-carapace, all 6 reference/ files
- User noted "a c# language reference was added just today" — `dafny-cs-compilation.md` (commit `41fa2e0`, Aug 24 14:40) is the closest match. Already restored. User may be referring to something else not found in git history.

### T1 Trial Run (partial)
- Architecture phase: passes after 1-2 retries (model still classifies as "dafny" → converter maps to Logic)
- C# Implementation phase: fails — carapace checker rejects `I<Name>.cs` interface files (fixed but not yet rebuilt/tested)
- Three fixes applied after the trial run but NOT yet committed:
  1. Carapace checker accepts `I<Name>.cs` patterns
  2. `ModuleClassification.Dafny` → `Logic` (enum + converter)
  3. `NullToArrayConverter<T>` in PositJson — handles model sending null arrays and object-typed argMappings

## Pipeline (C#-Direct, Aug 24)

```
Architecture (deepseek-v4-flash:cloud)
  → decomposes spec, writes C# interfaces, defines test cases
  → ContractScanner validates interface structure, stub names, connections
  → TypeChainChecker validates type compatibility across connection chain

C# Implementation (deepseek-v4-flash:cloud)
  → model reads C# interface, writes class implementing it
  → StaticChecker pre-flight: no Dafny runtime types, no markdown fences
  → TODO: dotnet build correction loop (currently single-shot)

QA (deepseek-v4-flash:cloud)
  → model generates test input data from test case descriptions
  → Docker harness: dotnet build + run + compare output
  → WireFixer correction loop on build/test failures (6 retries max)
```

## What Changed (Files)

### Deleted (source)
- `src/Posit.Phases/DafnyContractsPhase.cs`
- `src/Posit.Phases/DafnyImplementationPhase.cs`
- `src/Posit.Phases/DafnyFixer.cs`
- `src/Posit.Phases/PseudocodeReductionPhase.cs`
- `src/Posit.Tools/Z3Runner.cs`
- `src/Posit.Tools/PatternRegistryComposer.cs`
- `src/Posit.Contracts/Artifacts/DafnyArtifacts.cs`
- `src/Posit.DafnyRuntime/` (entire project + DLL)

### Deleted (patterns)
- All `patterns/*.dfy` (21 files)
- All `patterns/stubs/*.dfy` (6 files)

### Rewritten
- `ArchitecturePhase.cs` — writes .cs interfaces, not .dfy
- `CSharpImplementationPhase.cs` — model generates C# class bodies
- `PromptBuilder.cs` — C# interface prompts, no Dafny syntax
- `StaticChecker.cs` — 4 C# rules (Dafny type detection, namespace, class decl, markdown fences), 0 Dafny rules
- `WiringGenerator.cs` — native C# types, no ISequence/BigRational/UnicodeFromString
- `ContractScanner.cs` — validates C# interface structure
- `QaPhase.cs` — removed Z3-verified logic
- `PositOrchestrator.cs` — 3-phase FSM, no Dafny routing
- `Program.cs` — removed Z3Runner, DafnyFixer, ExtractDafnySourceAsync, UpdateDafnyInDbAsync
- `BotHarness.cs` — no DafnyRuntime.dll copy
- `BotHarnessDocker.cs` — no DafnyRuntime.dll in Dockerfile
- `BotHarnessProjects.cs` — no DafnyRuntime reference in generated .csproj
- `ArchitectureContract.cs` — DafnyInterface→CSharpInterface, removed Dafny fields
- `ArtifactEnums.cs` — ModuleClassification.Dafny → Logic
- `ModuleClassificationConverter.cs` — "dafny"→Logic, "logic"→Logic
- `PositJson.cs` — NullToArrayConverter<T> for resilient deserialization
- `KnownPhases.cs` — Architecture, CSharpImplementation, Qa
- `DesignContext.cs` — removed Dafny fields
- `DesignContextSnowballer.cs` — simplified
- `IExternalAbstractions.cs` — simplified to 2 methods
- `PatternRegistry.cs` — added ComposeIoShellSkeleton (was in deleted PatternRegistryComposer)

### Wiki
- `pipeline-spec.md` — rewritten for C#-direct
- `carapace-doctrine.md` — C# interface is the carapace
- Restored: all Dafny historical docs (plans, reference, pitfalls, proof methodology)

## Uncommitted Changes

Three fixes from the T1 trial run are in the working tree but NOT committed:
1. **Carapace checker** (`PositOrchestrator.cs`): accepts `I<Name>.cs` and `Wire.cs` patterns
2. **ModuleClassification**: `Dafny` → `Logic` in enum + converter
3. **PositJson**: `NullToArrayConverter<T>` handles null arrays + object-typed argMappings

Build passes with these changes (0 errors, 0 warnings).

## Known Issues for Next Session

### 1. CSharpImplementationPhase has no build correction loop
The model gets ONE shot at generating compilable C#. No compiler feedback.
This is the biggest gap. The TODO is at line 230:
```
// TODO: add dotnet build correction loop (compile temp project, feed errors back)
```
Need: compile generated C# against the interface in a temp project, feed `dotnet build` errors back to model, retry (max 4).

### 2. Architecture prompt still says "dafny" in stub section
PromptBuilder line 91-96 mentions "Dafny method's input type" and "Dafny method" in connection chain examples. Should say "logic method." The model still classifies components as "dafny" (legacy habit) — converter maps to Logic, but prompt should be clean.

### 3. Carapace checker was just fixed but not trial-verified
The fix accepts `I<Name>.cs` files. Need to rebuild and rerun T1 to confirm the C# implementation phase passes.

### 4. WiringGenerator hasn't been exercised
WiringGenerator was rewritten for native C# types but no trial has reached the wiring step yet. The type conversion logic (string→int, string[]→string, etc.) is untested.

### 5. Docker harness needs end-to-end run
BotHarness/Docker/Projects had DafnyRuntime.dll references removed. Generated .csproj files no longer reference DafnyRuntime. Needs a full Docker build to verify.

### 6. User mentioned "a c# language reference was added just today"
`dafny-cs-compilation.md` (commit `41fa2e0`) is the only C# reference found in git history. It's a Dafny→C# type mapping, not a general C# language reference. User may be referring to something else — needs clarification.

## Git State

- Branch: master
- Latest commit: `d4bb20d` — Restore wiki historical docs
- Working tree: uncommitted changes (3 fixes from T1 trial run)
- Build: 0 errors, 0 warnings

## Trial Status

| Trial | Status |
|-------|--------|
| T1 (CSV→JSON) | Architecture passes, C#Impl fails (carapace — fixed, not retried) |
| T6 | Not run |
| T8 | Not run |
| T12 | Not run |

## Key Decisions (This Session)

1. **Dafny dropped.** Three root causes all external/unfixable: model fluency, parser, contract quality tension.
2. **C# interfaces are the carapace.** Architect writes `I<Name>.cs`, model writes `class <Name> : I<Name>`, dotnet build is the compile gate.
3. **No formal verification replacement.** Clousot/Code Contracts dead. Nullable reference types + Roslyn analyzers + tests are the gates.
4. **Native C# types only.** No ISequence, no BigRational, no UnicodeFromString. string, int, bool, string[], double, long.
5. **Wiki historical docs preserved.** Dafny reference, plans, pitfalls, proof methodology kept as derivation chain history.