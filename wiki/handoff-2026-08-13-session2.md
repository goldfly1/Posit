# Handoff — Aug 13, 2026 (Session 2)

## Project

**Posit** — a spec compiler. The architect posits contracts (requires/ensures). Z3 confirms or denies. The code that survives is proven. Nothing ships unproven.

## What Happened This Session

### Monolith Refactor (Split)

Split `CSharpImplementationPhase.cs` (1503 lines) into:
- `CSharpImplementationPhase.cs` (720 lines) — phase orchestrator + model I/O + file parsing
- `WiringGenerator.cs` (718 lines) — wiring logic (was inline, now its own class)
- `TranslatedCSharpScanner.cs` (264 lines) — reads actual translated C#, extracts real signatures

Also added `PatternsDirectory` accessor to `PatternRegistry` so `WiringGenerator` can find io-shell stub templates.

### Read-Then-Wire Approach (Steps 1-2 of 5-step path)

**Step 1: TranslatedCSharpScanner** (commit a0380b0)
- Reads actual translated Dafny C# files from disk
- Parses `namespace _module_X { public partial class __default { public static ... } }`
- Extracts: method name, return type, param types/names, generic params
- Also scans io-shell stub templates (console-io, file-io, etc.)
- Verified: 26/26 checks against real files (CsvParser, CsvValidator, ConsoleIO)

**Step 2: WiringGenerator rewrite** (commits 3e7e327, 39e99f8, e475d20, 8b8ee67, 0c94ae5, db7dc59)
- Uses scanner output instead of guessing from pattern files
- `ResolveToMethod`: searches scanned methods first, fuzzy match for name variants, skips generic utility methods
- `ResolveTargetSignature`: builds signatures from real C# types, converts via `CsTypeToDafnyType`
- `ResolveEntryMethod`: uses scanner first for entry method resolution
- Emission fixes: strip generic params from calls, unique variable names per connection, escape C# reserved keywords, qualify ambiguous Dafny interface types, io-shell CLI skips entry call
- `DefaultForDafnyType`: handles generic type params (T, __T) → null!
- `CsTypeToDafnyType`: counts ISequence nesting depth (seq<string> vs seq<seq<string>>)

### 5-Trial Stress Test

Ran 5 different ad-hoc specs to test against different model architectures:

| # | Spec | Docker Build | Wiring Errors | Issue |
|---|------|-------------|---------------|-------|
| 1 | Calculator | ❌ | 1 | Fuzzy match: Parse→ParseLine (scanner found method but fuzzy match didn't fire) |
| 2 | Inventory tracker | ✅ SUCCEEDED | 0 | — |
| 3 | Note-taking | ❌ | 0 | Missing NuGet (SqlClient) — FIXED (commit 923efbe) |
| 4 | Temperature converter | ✅ SUCCEEDED | 0 | — |
| 5 | String utility | ❌ | 6 | `args` name collision + Rune→ISequence type mismatch |

**Wiring: 2 clean builds out of 5.** Scanner fixed the big structural bugs (method-not-found, generic leaking, ambiguous types, duplicate vars). Remaining issues are in the emission layer.

### Also Done This Session

- Io-shell cycle auto-repair in `ArchitecturePhase.cs` (strip io-shell→non-io-shell deps before cycle detection)
- `fromMethod` validation accepts `publicSurface` entries for io-shell components
- `BotHarness`: `InjectIoShellUsings` for test files, `Program.cs` generation for Exe projects
- `InferPackagesAndFrameworks`: added SqlClient + Npgsql package inference
- 12 wiring fixes from the earlier bandaid session (all committed in 76faeb6, still needed)

## Remaining Fixes (Small, Targeted)

1. **`args` name collision** — `EscapeReservedKeyword` is applied to non-CLI wiring params but NOT the CLI `EmitCliWiring` path. The CLI entry method uses `args` (from `Run(string[] args)`) and the scanner-resolved params may also include a param named `args`. Fix: apply `EscapeReservedKeyword` in `EmitCliWiring` param init.

2. **`Rune` vs `ISequence<Rune>`** — `IsTypeCompatible` doesn't handle scalar-vs-sequence. A method returning `Dafny.Rune` (single char) gets passed to a method expecting `ISequence<Rune>` (string). Fix: add `Rune`→`string` in `CsTypeToDafnyType`, and check scalar-vs-sequence in `IsTypeCompatible`.

3. **`Parse`→`ParseLine` fuzzy match** — `ResolveToMethod` fuzzy match checks `m.Name.Contains(connToMethod)` but the scanner may not have the module in `_scannedMethods` if the component name doesn't match the module name. Fix: also check `toComp.PatternName` and component aliases in the scanner lookup.

## The 5-Step Path

1. ✅ Read Translated C# — TranslatedCSharpScanner (done, verified)
2. ✅ Wire Against Reality — WiringGenerator uses scanner (done, 2/5 clean builds)
3. ⬜ Test the Harness — Push data through CLI, compare output to spec (NEXT)
4. ⬜ Verify `--test-assumptions Externs` — Runtime contract checking
5. ⬜ Testmaster Desktop Blazor UI — Prompt selection + proof dashboard

## Git State

- **Branch:** master
- **Latest commit:** 923efbe — InferPackagesAndFrameworks: add SqlClient + Npgsql
- **Working tree:** Clean
- **Commits this session:** 10 (76faeb6 through 923efbe)

## Key Files

| File | Lines | Responsibility |
|------|-------|---------------|
| `src/Posit.Phases/TranslatedCSharpScanner.cs` | 264 | Reads translated C#, extracts real method signatures |
| `src/Posit.Phases/WiringGenerator.cs` | 718 | Generates Wire.cs using scanner output |
| `src/Posit.Phases/CSharpImplementationPhase.cs` | 720 | Phase orchestrator (was 1503, wiring extracted) |
| `src/Posit.Tools/BotHarness.cs` | 899 | Docker build, Program.cs, NuGet inference, using injection |
| `src/Posit.Phases/ArchitecturePhase.cs` | 671 | Cycle auto-repair, fromMethod validation |
| `src/Posit.Tools/PatternRegistry.cs` | 807 | Added PatternsDirectory accessor |

## See Also

- `wiki/handoff-2026-08-13.md` — earlier session handoff (bandaid fixes, 253→1 trajectory)
- `wiki/handoff-2026-08-12-session3.md` — per-seam wiring, harness built
- `wiki/connector-diagnosis.md` — shrunk pipeline, carapace doctrine
- `wiki/proof-methodology.md` — seed → assemble → test → prove → carve
- `wiki/desktop-port-plan.md` — Blazor UI plan for Testmaster