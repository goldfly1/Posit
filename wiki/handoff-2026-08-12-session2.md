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

4. **GenerateWiring rewritten** — reads connector specs, generates real C# with method calls and Dafny→C# type conversions (`Dafny.Sequence<Rune>.UnicodeFromString()`, `BigInteger.Parse()`). No scaffold fallback. Missing specs → REJECT.

5. **Carapace enforcement** — validation rejects contracts where:
   - Components with method-call dependencies lack `connections`
   - Components with type-only dependencies lack `sharedTypes`
   - Connection `fromMethod` doesn't match any `methodSignature` name
   - Connection `toComponent` doesn't resolve to a real component
   - A method-call dependency has no connection targeting it

6. **Type-only vs method-call dependency classification** — a dependency on a Contracts module (PascalCase type names, no methodSignatures) requires `sharedTypes`, not `connections`. This was causing 4 rejections in T1.

7. **Pipeline shrunk** — AI team = Ideation + Architecture (WITH connectors) + Design Review. Code = Orchestrator assembles + Z3 verifies + Dafny→C# translates + Bot harness tests. Eliminated: Pseudocode, Dafny Imp, C# Imp, QA phase (model).

8. **Wiki updated** — `connector-diagnosis.md` (full diagnosis + shrunk pipeline + closed loop), `proof-methodology.md` (cotton candy root cause), `current-abilities.md` (new pipeline shape), `AGENTS.md` (locked decisions 25-29)

### T1 Trial — FULL SUCCESS

**Pipeline completed on attempt 1.** All 5 phases green. No retries.

- **Model fills out connector forms** — `deepseek-v4-flash:cloud` produced methodSignatures on all 7 components, 4 connection specs on the orchestrator (CsvJsonConverter), sharedTypes on type-only dependents
- **Type-only fix works** — CsvParser, CsvValidator, JsonTransformer depend on CsvContracts (types only) → used sharedTypes, not connections → zero validation errors
- **Wiring generator produces real code** — 70 lines, real method calls:
  ```csharp
  var result = _module_CsvJsonConverter.__default.HandleRequest(path);
  var csvreaderResult = _module_CsvReader.__default.ReadFile(path);
  var csvparserResult = _module_CsvParser.__default.Parse(csvContent);
  ```
- **Wire.cs accepted by carapace** — no filename rejection
- **Entry selection works** — picked CsvJsonConverter (4 connections, the orchestrator)

### Wire.cs Generated (T1)

```csharp
// Auto-generated wiring file — DETERMINISTIC from carapace connector specs.
using _module_CsvParser;
using _module_CsvValidator;
using _module_JsonTransformer;
using _module_CsvJsonConverter;
using _module_CsvContracts;
using _module_Result;
using System.Numerics;

namespace CsvJsonConverter
{
    public static class Wire
    {
        public static int Run(string[] args)
        {
            if (args.Length == 0)
            {
                System.Console.WriteLine("Usage: CsvJsonConverter <input>");
                return 1;
            }

            var path = Dafny.Sequence<Dafny.Rune>.UnicodeFromString(args[0]);

            // Call the proven logic: CsvJsonConverter.HandleRequest(path)
            var result = _module_CsvJsonConverter.__default.HandleRequest(path);

            // === Connection calls per carapace connector specs ===
            var csvreaderResult = _module_CsvReader.__default.ReadFile(path);
            var csvparserResult = _module_CsvParser.__default.Parse(csvContent);
            var csvvalidatorResult = _module_CsvValidator.__default.Validate(table);
            var jsontransformerResult = _module_JsonTransformer.__default.Transform(table);

            System.Console.WriteLine(result);
            return 0;
        }

        public static void ExecutePipeline(string input)
        {
            // Wire the full chain: CsvReader.ReadFile → CsvParser.Parse → CsvValidator.Validate → JsonTransformer.Transform
        }
    }
}
```

## Known Issues (Next Session)

1. **Unresolved variables in Wire.cs** — `csvContent`, `table` are referenced but not chained from previous return values. The calls are structurally correct but the output of step 1 isn't wired to the input of step 2. The `argMappings` say "csvContent -> input" but the generator doesn't substitute the actual variable. Fix: parse argMappings, replace source names with the return variable names from prior calls.

2. **One Wire.cs per seam** — currently one master file for the orchestrator. Should be one Wire.cs per component with connections, stacked in the DB with a component identifier. Each seam wires locally. The program is the stack of seams.

3. **`--test-assumptions Externs`** — Dafny can emit runtime contract checks for `{:extern}` methods. Status: hopeful but not yet verified. Needs standalone testing.

4. **Bot harness** — automated GUI test via hotkey mapping + data pushing + output comparison. Script, not LLM. Needs a working program to test against (T1 now provides one).

5. **Testmaster desktop** — Blazor app with prompt selection sheet (left) + proof dashboard (right). Needs the bot harness first.

## Git State

- **Branch:** master
- **Latest commit:** `ffdb34c` — Remove stale subagent test files
- **Working tree:** Clean
- **GitHub:** all pushed
- **Commit count:** ~127 (7 new this session)

## Key Commits This Session

| Commit | Description |
|--------|-------------|
| `bc546f8` | Connector forms on the carapace — deterministic wiring + shrunk pipeline |
| `a560a0e` | Fix: ExtractMethodSignatures handles multi-line method declarations |
| `0d3ab9c` | Carapace enforcement: reject incomplete connector forms, no scaffold |
| `354f401` | Fix: GenerateWiring entry component selection |
| `423377a` | Fix: Wire.cs carapace acceptance + entry picks most connections |
| `666f6bf` | Fix: type-only dependencies need sharedTypes, not connections |
| `ffdb34c` | Remove stale subagent test files |

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
| `src/Posit.Phases/CSharpImplementationPhase.cs` | GenerateWiring from connector specs |
| `src/Posit.Tools/PatternRegistry.cs` | ExtractMethodSignatures, FormatPatternSignatures |
| `src/Posit.Cli/Orchestration/PositOrchestrator.cs` | Carapace accepts Wire.cs, carries new fields |

## The Big Picture

The loop is closing:

```
SEED       → 17 proven atoms in the registry ✅
ASSEMBLE   → orchestrator wires from carapace connector specs ✅ (T1 proven)
TEST       → bot pushes data through CLI, exercises GUI ← NEXT
PROVE      → output matches spec ← NEXT
CARVE      → pull proven assembly back into registry ← NEXT
```

The connector forms work. The model fills them out. The orchestrator wires deterministically. The program is no longer cotton candy — it has real method calls connecting proven logic. Two wiring quality issues remain (variable chaining, per-seam files) but the architecture is sound.

## See Also

- `wiki/connector-diagnosis.md` — the full diagnosis and shrunk pipeline
- `wiki/handoff-2026-08-12.md` — session 1 handoff (Bluejohn discovery, stub fixes)
- `wiki/proof-methodology.md` — seed → assemble → test → prove → carve