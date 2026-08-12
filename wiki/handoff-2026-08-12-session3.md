# Handoff — Aug 12, 2026 (Session 3)

## Project

**Posit** — a spec compiler. The architect posits contracts (requires/ensures). Z3 confirms or denies. The code that survives is proven. Nothing ships unproven.

## What Happened This Session

### Per-Seam Wiring (Issue #3) — DONE ✅

Rewrote `GenerateWiring` to emit one Wire.cs per component with connections. Each lives in its own namespace. CLI components get `Run(string[] args)`, non-CLI get `Wire_{Name}()`. Verified with T1 (1 Wire.cs), T8 (8 Wire.cs), T14 (5 Wire.cs). Committed `559cc3b`.

### Bot Harness (Issue #5) — BUILT, ITERATING

Built `BotHarness.cs` — deterministic CLI test runner. Materializes C# from DB, generates .csproj, builds in Docker, runs CLI with test data, captures output, compares to spec. Added `posit harness <session-id>` CLI command.

### Fixes Made During Harness Iteration

1. **Dafny namespace collision** (`Z3Runner.cs`) — every Dafny module translated to `namespace _module` (collision). Post-process renames to `namespace _module_{moduleName}` + updates internal `_module.X` references. Committed `e804753`.

2. **Test project circular deps** (`BotHarness.cs`) — test projects were referencing each other. Fixed: test projects only reference non-test projects.

3. **DafnyRuntime.dll reference** (`BotHarness.cs`) — translated C# needs DafnyRuntime.dll. Harness copies it into build context and adds `<Reference>` to .csproj.

4. **io-shell namespace** (`CSharpImplementationPhase.cs`) — io-shell components use `namespace {ComponentName}` with classes like `FileIO`, not `_module_X.__default`. Wire.cs now uses correct namespace + `ResolveStubClass` maps method names to stub classes. Committed `d7f32da`.

5. **PatternMethod lookup** (`CSharpImplementationPhase.cs`) — Wire.cs now uses target component's `PatternMethod` (actual Dafny method name like `HandleRequest`) instead of `conn.ToMethod` (architect's semantic name like `Parse`). Committed `8be8257`.

6. **Connection-only usings** (`CSharpImplementationPhase.cs`) — Wire.cs only imports namespaces for actual connection targets, not ALL translated modules. Prevents unnecessary references to shared type modules like `Result`.

7. **Carapace enforcement: toMethod validation** (`ArchitecturePhase.cs`) — `ValidateContract` now checks that `conn.ToMethod` matches a real method on the target component's pattern via `PatternRegistry.GetPatternSignatures`. `ValidateContract` changed from static to instance to access `_registry`. Committed `e68a49b`.

### Progress on Docker Build Errors

| Iteration | Errors | Fix |
|-----------|--------|-----|
| 1 | 253 | Namespace collision (every Dafny module = `namespace _module`) |
| 2 | 253 | DafnyRuntime.dll missing |
| 3 | ~6 | Test project circular deps |
| 4 | 3 | io-shell namespace mismatch |
| 5 | 1 | Unnecessary `_module_Result` using |
| 6 | 5 | Method name mismatch (Parse vs HandleRequest) — PatternMethod fix |
| 7 | 5 | HandleRequest signature mismatch (6 params vs 2 passed) |

The 5 remaining errors are **method signature mismatches**: `HandleRequest` on the pipeline pattern takes 6 parameters but the connection spec only provides 1-2. The Wire.cs generator needs to read the actual pattern method signatures and fill in defaults for unspecified parameters.

### Key Insight

The error count isn't monotonically decreasing because each pipeline run produces a different architecture — the model makes different choices. But each fix addresses a real structural issue. The harness is doing its job: catching real wiring bugs that compilation against Dafny output reveals.

## Known Issues (Next Session)

1. **HandleRequest signature mismatch** — the pipeline pattern's `HandleRequest` takes 6 params (`input, output, minFields, maxFields, ...`) but the connection spec only provides 1-2. Fix: Wire.cs generator should read the actual pattern method signature from `PatternRegistry.GetPatternSignatures` and fill in default values for unspecified parameters.

2. **Some patterns don't have HandleRequest** — CsvValidator and JsonTransformer's translated C# doesn't have `HandleRequest` on `__default`. Need to check what methods the `validator` and `transformer` patterns actually expose.

3. **`--test-assumptions Externs`** (issue #4) — not yet tested. Dafny can emit runtime contract checks for `{:extern}` methods.

4. **Testmaster desktop** (issue #6) — Blazor app with prompt selection + proof dashboard. Needs harness first.

## Git State

- **Branch:** master
- **Latest commit:** `e68a49b` — Carapace enforcement: validate toMethod against pattern methods
- **Working tree:** Clean
- **GitHub:** all pushed
- **Commits this session:** 5 new (`559cc3b`, `e804753`, `8be8257`, `d7f32da`, `e68a49b`)

## Key Files Changed

| File | Changes |
|------|---------|
| `src/Posit.Phases/CSharpImplementationPhase.cs` | Per-seam wiring, io-shell namespace, PatternMethod lookup, connection-only usings, ResolveStubClass |
| `src/Posit.Tools/Z3Runner.cs` | Dafny namespace renaming post-process |
| `src/Posit.Tools/BotHarness.cs` | NEW — deterministic CLI test runner |
| `src/Posit.Cli/Program.cs` | Added `harness` command |
| `src/Posit.Phases/ArchitecturePhase.cs` | toMethod validation against pattern methods |

## The Big Picture

```
SEED       → 17 proven atoms ✅
ASSEMBLE   → per-seam wiring ✅ (T1, T8, T14)
TEST       → bot harness built ✅, Docker build at 5 errors ← ITERATING
PROVE      → output matches spec ← NEXT (after build passes)
CARVE      → pull proven assembly back into registry ← NEXT
```

The harness proved its value: it caught real bugs (namespace collision, method name mismatches, signature mismatches) that were invisible without actually compiling the generated code against the Dafny output. Each iteration fix is a real structural fix, not a bandaid.

## See Also

- `wiki/handoff-2026-08-12-session2.md` — per-seam wiring, connector forms
- `wiki/connector-diagnosis.md` — the data flow trace, shrunk pipeline
- `wiki/proof-methodology.md` — seed → assemble → test → prove → carve
- `wiki/current-abilities.md` — 17 patterns, 6 stubs, trial scorecard