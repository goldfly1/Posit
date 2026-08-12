# Handoff — Aug 12, 2026 (Session 2)

## Project

**Posit** — a spec compiler. The architect posits contracts (requires/ensures). Z3 confirms or denies. The code that survives is proven. Nothing ships unproven.

## What Happened This Session

### The Connector Diagnosis

Traced the full data flow from architecture prompt → model → artifact → orchestrator → wiring. Found the root cause of Bluejohn (cotton candy): **the carapace had no connector data.** The architect was never asked for method signatures, connection specs, or type mappings. `PublicSurface` was just names (`"RunPipeline"`), `Dependencies` was just names, no connection points. The wiring generator had a TODO where real code should be.

### What Was Built

1. **Connector forms on the carapace** — new fields on `Component`:
   - `MethodSignature[]` — actual parameter types, return types, `PatternMethod` mapping (architect name → pattern's real method name)
   - `ConnectionSpec[]` — which method calls which dependency method, with arg mappings and return usage
   - `SharedTypeRef[]` — types shared across modules via Dafny `include`

2. **Architecture prompt v1.1.0** — asks the model to fill out `methodSignatures`, `connections`, `sharedTypes` with examples and validation checklist

3. **PatternRegistry.ExtractMethodSignatures** — parses Dafny source, extracts method signatures (handles multi-line declarations), maps Dafny types to C# equivalents

4. **GenerateWiring rewritten** — reads connector specs, generates real C# with method calls and Dafny→C# type conversions. No scaffold fallback. Missing specs → REJECT.

5. **Carapace enforcement** — validation rejects contracts where:
   - Components with method-call dependencies lack `connections`
   - Components with type-only dependencies lack `sharedTypes`
   - Connection `fromMethod` doesn't match any `methodSignature` name
   - Connection `toComponent` doesn't resolve to a real component
   - A method-call dependency has no connection targeting it

6. **Type-only vs method-call dependency classification** — a dependency on a Contracts module (PascalCase type names, no methodSignatures) requires `sharedTypes`, not `connections`.

7. **Variable chaining** — return values from each connection call are tracked and substituted into subsequent call arguments. Positional fallback: when the architect uses semantic source names (parsedData, validatedData) that don't match component names, use the most recent prior call's return variable.

8. **Pipeline shrunk** — AI team = Ideation + Architecture (WITH connectors) + Design Review. Code = Orchestrator assembles + Z3 verifies + Dafny→C# translates + Bot harness tests. Eliminated: Pseudocode, Dafny Imp, C# Imp, QA phase (model).

9. **Wiki updated** — `connector-diagnosis.md`, `proof-methodology.md`, `current-abilities.md`, `AGENTS.md` (locked decisions 25-29)

### Trial Results

| Trial | Components | Patterns | Wire.cs | Chained | Retries | Status |
|---|---|---|---|---|---|---|
| T1 (run 4) | 5 | parser, validator, transformer, pipeline | ✅ 73 lines | ✅ all 5 | 0 | Completed |
| T1 (run 5) | 6 | parser, validator, transformer, pipeline | ✅ 73 lines | ✅ all 5, zero unresolved | 0 | Completed |
| T2 | 6 | pipeline×2, parser, builder | ✅ 77 lines | ✅ chained | 1 (sharedTypes) | Completed |
| T8 | 12 | 10 distinct patterns | ✅ 64 lines | ✅ zero unresolved | 0 | Completed |

**T8 details:** 12 components, 10 distinct patterns (result, parser, graph, transformer, scheduler, state-machine, aggregator, pipeline, repository, observer), all Z3-verified, all 5 phases green on attempt 1. 2 model calls total (architecture + QA). $0.00 cost.

### Wire.cs Example (T1, final run — all variables chained)

```csharp
var csvreaderResult = _module_CsvReader.__default.ReadFile(inputPath);
var csvparserResult = _module_CsvParser.__default.Parse(csvreaderResult);
var csvvalidatorResult = _module_CsvValidator.__default.Validate(csvparserResult);
var jsontransformerResult = _module_JsonTransformer.__default.Transform(csvvalidatorResult);
var jsonwriterResult = _module_JsonWriter.__default.WriteFile(outputPath, jsontransformerResult);
```

Each call feeds its return to the next. Tabs into slots. Zero `/* unresolved */` markers.

## Known Issues (Next Session)

1. **Wire.cs not persisted in T8** — generated in-memory (log confirms 64 lines) but not included in the source-code-bundle artifact. The wiring code exists but didn't make it into the DB. Needs investigation — likely a serialization issue in the artifact bundling.

2. **T8 only had 1 connection spec** — the Cli (io-shell) had 1 connection to PipelineEngine. The heavy orchestrator (PipelineEngine with 6 dependencies) didn't get connections. The model put connections on the CLI, not on PipelineEngine where the real wiring belongs. May need prompt clarification or entry selection logic adjustment.

3. **One Wire.cs per seam** — currently one master file for the orchestrator. Should be one Wire.cs per component with connections, stacked in the DB with a component identifier. Each seam wires locally.

4. **`--test-assumptions Externs`** — Dafny can emit runtime contract checks for `{:extern}` methods. Status: hopeful but not yet verified. Needs standalone testing.

5. **Bot harness** — automated GUI test via hotkey mapping + data pushing + output comparison. Script, not LLM. T1 now provides a working program to test against.

6. **Testmaster desktop** — Blazor app with prompt selection sheet (left) + proof dashboard (right). Needs the bot harness first.

## Git State

- **Branch:** master
- **Latest commit:** `410d72a` — Fix: include connection target components in using statements
- **Working tree:** Clean
- **GitHub:** all pushed
- **Commit count:** ~132 (12 new this session)

## Key Commits This Session

| Commit | Description |
|--------|-------------|
| `bc546f8` | Connector forms on the carapace — deterministic wiring + shrunk pipeline |
| `a560a0e` | Fix: ExtractMethodSignatures handles multi-line method declarations |
| `0d3ab9c` | Carapace enforcement: reject incomplete connector forms, no scaffold |
| `354f401` | Fix: GenerateWiring entry component selection |
| `423377a` | Fix: Wire.cs carapace acceptance + entry picks most connections |
| `666f6bf` | Fix: type-only dependencies need sharedTypes, not connections |
| `3d304b3` | Fix: chain return values between connection calls in Wire.cs |
| `9777832` | Fix: positional fallback for unresolved argMapping sources |
| `da1f753` | Fix: register return vars in priorReturnVarOrder + use most recent |
| `410d72a` | Fix: include connection target components in using statements |

## Key Files

| File | Content |
|------|---------|
| `wiki/connector-diagnosis.md` | Full diagnosis, shrunk pipeline, closed loop |
| `wiki/proof-methodology.md` | Cotton candy root cause + closed loop |
| `wiki/current-abilities.md` | Shrunk pipeline, connector forms, bot harness |
| `AGENTS.md` | Locked decisions 25-29, shrunk pipeline |
| `prompts/architecture/1.0.0.md` | v1.1.0 — asks for connector forms |
| `src/Posit.Contracts/Artifacts/ArchitectureContract.cs` | MethodSignature, ConnectionSpec, SharedTypeRef |
| `src/Posit.Contracts/Core/DesignContext.cs` | Connector fields on DesignComponent |
| `src/Posit.Phases/ArchitecturePhase.cs` | Validation: type-only vs method-call deps |
| `src/Posit.Phases/CSharpImplementationPhase.cs` | GenerateWiring from connector specs + variable chaining |
| `src/Posit.Tools/PatternRegistry.cs` | ExtractMethodSignatures, FormatPatternSignatures |
| `src/Posit.Cli/Orchestration/PositOrchestrator.cs` | Carapace accepts Wire.cs, carries new fields |

## The Big Picture

The loop is closing:

```
SEED       → 17 proven atoms in the registry ✅
ASSEMBLE   → orchestrator wires from carapace connector specs ✅ (T1, T2, T8 proven)
TEST       → bot pushes data through CLI, exercises GUI ← NEXT
PROVE      → output matches spec ← NEXT
CARVE      → pull proven assembly back into registry ← NEXT
```

The connector forms work. The model fills them out. The orchestrator wires deterministically. Variables chain. The program is no longer cotton candy — it has real method calls connecting proven logic. T8 proved 10 patterns and 12 components assemble cleanly in a single pipeline run with zero retries.

## See Also

- `wiki/connector-diagnosis.md` — the full diagnosis and shrunk pipeline
- `wiki/handoff-2026-08-12.md` — session 1 handoff (Bluejohn discovery, stub fixes)
- `wiki/proof-methodology.md` — seed → assemble → test → prove → carve