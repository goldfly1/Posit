# Session Report — Aug 14, 2026 (T1/T2/T5/T8/T12 + Docker Harness)

## What Was Done

Full pipeline rebuild verification. T1 and T2 passed, then T5/T8/T12 passed
after fixing root causes. Docker harness ran against all three — builds,
runs, but code generation bugs prevent compilation in Docker.

## Fixes Applied (Universal, Not Band-Aids)

### 1. Architecture Prompt — Pattern/Stub Catalog Injection
**File:** `src/Posit.Cli/Orchestration/PromptBuilder.cs` (new, 92 lines)
- The architecture prompt previously told the model to "select patterns from the registry" but never listed what existed. Model defaulted everything to io-shell with empty stubs → zero files generated.
- Now injects all 20 patterns with responsibilities + all 16 C# stubs with keywords.
- `pipeline` elevated as UNIVERSAL STARTER (decision 19). Specialist patterns listed separately.
- PascalCase enforced. "Do NOT invent pattern names" + "pick the CLOSEST one."
- `argMappings` format specified: `["source->target"]` strings, not objects.

### 2. ContractScanner — io-shell Empty Stubs Validation
**File:** `src/Posit.Phases/ContractScanner.cs`
- io-shell components with empty `stubNames` are now rejected with available stubs listed.
- Previously only validated `patternName` for dafny components — io-shell with zero stubs passed silently.

### 3. DafnyImplementationPhase — Hybrid with Z3 Correction Loop
**File:** `src/Posit.Phases/DafnyImplementationPhase.cs`
- Cut-out components: skeleton exists → Z3 verify → translate (deterministic, no model)
- Custom components: no skeleton → model writes Dafny → Z3 verify → 4-attempt correction loop (feed previous Dafny + Z3 errors back) → translate
- BAN `function` — always use `method` (eliminates invalid UnaryExpression class of errors)
- Reframe as refactor: pseudocode IS the algorithm, wrap it don't redesign it
- "Not Dafny" detection: when Z3 says "this symbol not expected", tells model to output raw Dafny
- Broadened JSON extraction: detects var/:=/if/return markers, not just method/module
- `IModelGateway` dependency removed from constructor.

### 4. Z3Runner — `--allow-warnings` on Verify
**File:** `src/Posit.Tools/Z3Runner.cs`
- `VerifyAsync` didn't have `--allow-warnings` but `TranslateAsync` did.
- `graph.dfy` has a quantifier trigger warning. Z3 says "13 verified, 0 errors" but Dafny compiler rejected it.
- Z3 is the judge. Compiler warnings (performance hints) don't override Z3's verdict.
- This was blocking T12 (and any pattern with quantifier warnings).

### 5. FsmReducer — Infinite Loop Fix
**File:** `src/Posit.Core/State/FsmReducer.cs`
- `ApplyRollbackToPhase` reset attempt to 1 and went back to the SAME phase with the SAME inputs — infinite loop.
- Now aborts the session after exhausting retries.
- Rollback to Architecture (for skeleton correction) is separate, with LoopbackCount cap.

### 6. PositOrchestrator — Warning Output + Circuit Breaker + Trim
**File:** `src/Posit.Cli/Orchestration/PositOrchestrator.cs` (300 → 176 lines)
- Warnings now printed on failure (previously silent — failures showed "failed" with no reason).
- Circuit breaker: max 10 same-phase failures → abort (safety net for FSM bugs).
- Extracted `PromptBuilder.cs` (78 lines) and `DesignContextSnowballer.cs` (69 lines) to get under 200-line cap.

### 7. BotHarnessDocker — Lowercase Tags
**File:** `src/Posit.Tools/BotHarnessDocker.cs`
- Docker tags must be lowercase. Session IDs have uppercase letters.
- `tag.ToLowerInvariant()` on both build and run.

### 8. ArchitecturePhase — Parse Error Logging
**File:** `src/Posit.Phases/ArchitecturePhase.cs`
- `ParseContract` catch block was `catch { return null; }` — swallowed the error.
- Now prints the JSON parse exception and raw output length for debugging.

## Trial Results

### Pipeline (all phases green = session completed)

| Trial | Spec | Components | C# Files | Retries | Result |
|-------|------|------------|----------|---------|--------|
| T1 | CSV-to-JSON CLI | 5 | 7 | 1 (arch) | ✅ Completed |
| T2 | Document processing | 5-6 | 7 | 2 (arch) | ✅ Completed |
| T5 | Document pipeline | 6 | 7 | 0 | ✅ Completed |
| T8 | CI/CD pipeline engine | 9 | 12 | 0 | ✅ Completed |
| T12 | Task scheduler | 10 | 14 | 0 | ✅ Completed |

All passes verified: real C# files in DB (7K-18K chars each), Z3 verified, Dafny translated.
One model call per trial (architecture only, ~7-28s). Everything else deterministic.

### Docker Harness

| Trial | Docker Build | Test Results | Errors |
|-------|-------------|-------------|--------|
| T5 | ✅ Built | 2 tests, both FAIL | Output doesn't match spec (generic patterns, not spec-specific) |
| T8 | ❌ Build failed | 0 tests | `__default` not found in Wire.cs + Wire.cs double-included in .csproj |
| T12 | ❌ Build failed | 0 tests | `__default` not found in Wire.cs + `_IEntry` type missing + Wire.cs double-included |

## Docker Harness Bugs (To Fix Next)

### Bug 1: `__default` not found in Wire.cs
Every Wire.cs file references `__default` (the Dafny runtime default value) but the DafnyRuntime
DLL isn't being referenced properly, or the Wire.cs code uses `__default` without the right using.
This was a known issue from Aug 13 (references/aug13-t1-io-shell-default-missing.md).

### Bug 2: Wire.cs specified multiple times in .csproj
`BotHarnessProjects.GenerateCsproj` includes `*.cs` glob AND explicitly lists Wire.cs.
Fix: either exclude Wire.cs from the glob or don't add it explicitly.

### Bug 3: `_IEntry` type not found (T12 only)
Dafny runtime type reference missing. Likely a DafnyRuntime DLL version mismatch or
missing using directive.

### Observation: Cotton Candy
T5 built and ran in Docker but the test cases failed because the patterns are generic.
`pipeline` + `strategy` produce proven-but-generic code that doesn't implement document
classification. The test cases describe spec-specific behavior ("classify invoice → returns
DocumentType.Invoice") that the generic pattern bodies don't implement. This is the
"cotton candy" problem — compiles, Z3 green, but doesn't do what the spec asked.

## Module Inventory (Final)

### Posit.Cli/Orchestration (3 files, 323 lines)
- `PositOrchestrator.cs` (176) — phase loop, circuit breaker, warning output, carapace enforcement
- `PromptBuilder.cs` (78) — architecture prompt with pattern/stub catalog
- `DesignContextSnowballer.cs` (69) — design context snowball across phases

### Posit.Phases (12 files)
- `DafnyImplementationPhase.cs` (544) — hybrid: cut-outs deterministic, custom Dafny with 4-attempt Z3 correction loop

## Next Steps
1. Fix `__default` in WiringGenerator (root cause, not band-aid)
2. Fix Wire.cs double-inclusion in BotHarnessProjects.GenerateCsproj
3. Fix `_IEntry` type reference (T12)
4. Address cotton candy: patterns need spec-specific parameters or the architect needs to
   set parameters that specialize the pattern bodies
5. Run Docker harness again after fixes
6. Update wiki, commit, push