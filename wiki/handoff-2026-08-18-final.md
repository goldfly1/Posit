# Handoff — Aug 18, 2026 (Tuesday Session, Final)

## Result: 6/6 TRIALS PASSING

| Trial | Status | Tests | Key Fix |
|-------|--------|-------|---------|
| T1 (CSV→JSON) | ✅ | 2/2 | — |
| T2 (JSON→CSV) | ✅ | 3/3 | — |
| T3 (Filtered CSV) | ✅ | 2/2 | — |
| T4 (Word Counter) | ✅ | 4/4 | temp-dir fix (WireFixer finds Wire.cs on build failure) |
| T5 (Multi-File Merge) | ✅ | 2/2 | lean prompt (13K→2.4K) + PreviousOutput in correction signal |
| T6 (Temperature) | ✅ | 4/4 | DafnyFixer (ref card + Z3 loop + JSON extraction) + CompareOutput keywords |

## Session Arc

Started from Part 2 handoff (3/6 passing, T4/T5/T6 blocked).

### Part 3: T4 fix
- Temp-dir loss: BotHarness.Fail() was nulling tempDir on build failure. WireFixer
  couldn't find Wire.cs. One-line fix.
- DafnyFixer specialist created: gets test failures + Dafny source + spec, Z3 re-verifies.

### Part 4: T5 fix — lean prompt
- Architecture prompt had grown to 13K chars / 17 sections. Model drowned in
  gobbledygook. Rewrote to 2.4K (82% smaller). Point at resources, don't inline.
- PreviousOutput injection: model sees its own JSON on retry, does targeted fix
  instead of rewriting from scratch.
- Multi-file test data: === separator for multi-input specs.
- Shape-based test expectations: "prints result" not "prints '0 C'".

### Part 5: T6 fix — DafnyFixer + CompareOutput
- DafnyFixer: added Dafny Reference Card (was missing — produced invalid Dafny).
- DafnyFixer: Z3 correction loop (3 attempts — feed Z3 errors back, let the dog
  chew on it). Same pattern as all other specialists.
- DafnyFixer: structural constraints (don't add new methods/externs/Main).
- Universal JSON extraction: all 3 code-producing specialists now scan for
  code-like string properties instead of enumerating field names. No whack-a-mole.
- CompareOutput: added "result"/"prints" as pass keywords. T6 was actually working
  (32°F = 0°C is correct!) but the comparison didn't recognize "prints result".

## Specialist Framework (complete)

Every blocker TYPE has a specialist that gets ONLY what it needs:

| # | Blocker Type | Specialist | Signal | Correction Loop | Status |
|---|---|---|---|---|---|
| 1 | C# wiring errors | WireFixer | Docker build fails (CS errors) | 3 retries, sees Wire.cs + errors | ✅ |
| 2 | Dafny logic errors | DafnyFixer | Tests fail after WireFixer tried | 3 Z3 retries, sees Dafny + errors + ref card | ✅ |
| 3 | Architecture errors | Architect retry | ContractScanner/TypeChainChecker | PreviousOutput + CorrectionSignal | ✅ |
| 4 | Multi-input architecture | (prompt) | — | Lean prompt allows multi-input chains | ✅ |
| 5 | Test expectation errors | (prompt) | — | Shape-based expectations ("prints result") | ✅ |
| 6 | Z3 verification failures | DafnyFixer (Z3 loop) | Z3 rejects | 3 attempts, feed errors back | ✅ |

## Universal JSON Extraction (no whack-a-mole)

All 3 code-producing specialists use the same pattern:
- If model output starts with `{`, parse as JSON
- Scan ALL string properties for code-like content
  - C#: contains 'class'/'static'/'void'
  - Dafny: contains 'method'/'module'
- The model's field name doesn't matter — no field-name lists

## Open Questions for Next Session

1. **Architect understanding** — the architect picks cut-outs by name-matching,
   not by understanding what the spec needs. T5 took 3 retries because it kept
   forcing csv-parser for everything. At scale (hundreds of cut-outs, thousands
   of trials), need capability matching or requirement-driven composition.
   User: "it would be better to have *some understanding*"

2. **AIQA at the end** — user mentioned "a test QA for producing Pseudodata and
   analyzing the results of the db work — does this add up to what you expected?
   A req list should be provided (by the customer) but no other resource should
   be needed."

3. **Scale** — 6 trials passing. Next: T7-T12 (Tier 0), T13-T16 (Tier 1),
   T17-T20 (Tier 2), T21-T24 (Tier 3). Each tier has more components.

## Git State

- Branch: master, pushed to origin
- Latest commit: 561944a — Handoff part 5
- Working tree: clean

## Build Status

**0 errors, 1 pre-existing warning (BotHarness.cs:394 CS8625).**