# Handoff — Aug 18, 2026 (Tuesday Session, Part 3)

## What Happened This Session

Picked up from Part 2's handoff. The critical realization from the previous session:
**"Maybe that is the answer to each problem — Specialize."** Each blocker TYPE gets a
dedicated specialist that gets exactly the context it needs — no more, no less.

Two T4 blockers were identified in Part 2. Both fixed this session. T4 now PASSES.

## Trial Scoreboard

| Trial | Pipeline | Docker | Tests | Status |
|-------|----------|--------|-------|--------|
| T1 (CSV→JSON) | ✅ | ✅ | 2/2 PASS | ✅ PASS |
| T2 (JSON→CSV) | ✅ | ✅ | 3/3 PASS | ✅ PASS |
| T3 (Filtered CSV) | ✅ | ✅ | 2/2 PASS | ✅ PASS |
| T4 (Word Counter) | ✅ | ✅ (retry) | 4/4 PASS | ✅ PASS (was ❌ — fixed!) |
| T5 (Multi-File) | ❌ arch | — | — | Multi-input can't express in linear chain |
| T6 (Temperature) | ✅ | ✅ | 1/3 | stdin works, test expectations wrong |

**4/6 fully passing.**

## T4 Fix — What Happened

The T4 run failed on Docker build with `error CS1061: 'ISequence<Rune>' does not contain
'definition for 'Value'`. Wire.cs used `.Value` instead of the correct ISequence API.

**The temp-dir fix was the critical unblocker.** WireFixer already had the right ISequence
API reference in its prompt — it just could never *find* Wire.cs before because `Fail()`
was throwing away the tempDir path. One-line fix in BotHarness.cs.

Full trace:
1. Docker build failed (2 compile errors on Wire.cs lines 37-38)
2. WireFixer fired — found Wire.cs via tempDir (the fix!)
3. Model fixed it in 4.1s (467 output tokens)
4. Re-run: all 4 tests PASS — correct word frequency output:
   `4 the / 2 cat / 1 sat / 1 on / 1 mat / 1 dog / 1 ran / 1 fast`

## Key Fixes This Session

### 1. Temp-dir loss (commit d34f95d)
- **Root cause**: `BotHarness.Fail()` returned `null` for `TempDir` on Docker build failures,
  even though the temp dir existed and contained Wire.cs.
- **Fix**: Pass `tempDir` through on build failure: `new BotHarnessResult(false, [], tempDir, ...)`
- **Also**: `ExtractPreviousWireCsAsync` — async version with DB `SourceCodeBundle` fallback
  for when the temp dir gets cleaned up on re-run.

### 2. DafnyFixer specialist (commit d34f95d)
- New file: `src/Posit.Phases/DafnyFixer.cs`
- Handles "cotton candy" failures: Dafny compiles + Z3 verifies, but produces wrong output.
- Gets ONLY: failing test cases + Dafny source + component spec/responsibility.
- Z3 re-verifies the fix, translates to C#, returns both.
- Retry loop escalates: WireFixer first (type conversion), then DafnyFixer (logic).
- Not yet exercised — T4's issue was purely C# wiring, not Dafny logic.

## Blocker-Type Framework

Principle: **"Each blocker TYPE gets the specialist it needs — not whack a mole,
pushing toward Production."** Systematic correction framework.

| # | Blocker Type | Specialist | Signal | Status |
|---|---|---|---|---|
| 1 | C# wiring errors | WireFixer | Docker build fails (error CS) | ✅ Working |
| 2 | Dafny logic errors (cotton candy) | DafnyFixer | Tests fail after WireFixer tried | ✅ Built, not exercised |
| 3 | Architecture decomposition errors | — (prompt eng) | Over-decomposition, wrong connections | ⚠️ No correction loop |
| 4 | Multi-input architecture (T5) | — | Linear chain can't express | ❌ Structural limitation |
| 5 | Test expectation errors (T6) | — | Program correct, test wrong | ❌ No specialist |
| 6 | Z3 verification failures | — (retry loop) | Dafny doesn't verify | ⚠️ No targeted fixer |
| 7 | Type chain incompatibilities | — (rollback) | Declared ≠ actual types | ⚠️ Force rollback only |

## Known Blockers for Next Session

### T5 — Multi-input architecture
Linear chain can't express "read file1, read file2, merge." Needs richer connection
format (multiple entry points) or model-based merge logic in wiring.

### T6 — Test expectations wrong
Architect says "Prints '32 F'" for input "32 F" but conversion gives "0 C". The program
is correct, the test expectation is wrong. Needs better architect test case generation
or a TestFixer specialist (blocker type 5).

## Git State

- Branch: master
- Latest commit: d34f95d — T4 blockers fixed
- Pushed to origin
- Working tree: clean

## Build Status

**0 errors, 0 warnings.**