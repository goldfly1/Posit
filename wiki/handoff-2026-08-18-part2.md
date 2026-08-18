# Handoff — Aug 18, 2026 (Tuesday Session, Part 2)

## What Happened This Session

Ran trials T1-T6 iteratively, fixing pipeline issues as they surfaced. Started from
the refactor commit (data flow spec, correction loops, T3/T6 fixes). Made 10+ commits
fixing architecture prompt, JSON extraction, ISequence API, WireFixer specialist, stdin
support, and test-failure correction loop.

## Trial Scoreboard

| Trial | Pipeline | Docker | Tests | Status |
|-------|----------|--------|-------|--------|
| T1 (CSV→JSON) | ✅ | ✅ | 2/2 PASS | ✅ PASS |
| T2 (JSON→CSV) | ✅ | ✅ | 3/3 PASS | ✅ PASS |
| T3 (Filtered CSV) | ✅ | ✅ | 2/2 PASS | ✅ PASS (branching works!) |
| T4 (Word Counter) | ✅ | ❌ build | — | Wire.cs ISeq 2D→1D mismatch + cotton candy |
| T5 (Multi-File) | ❌ arch | — | — | Multi-input can't express in linear chain |
| T6 (Temperature) | ✅ | ✅ | 1/3 | stdin works, test expectations wrong |

**3/6 fully passing. T6 functionally works (program converts correctly).**

## Key Fixes This Session

### 1. Architecture Prompt Connection Example (commit af9c41a)
The model was confusing fromMethod (should be orchestrator's own method) with target
method names, and toComponent (should be component Name) with pattern/stub names. Added
explicit field-by-field rules + full worked CSV→JSON example. This was THE fix that
unblocked T1-T3.

### 2. JSON Field Extraction (commit 22280bd, 4c7629b)
Model wraps C# code in JSON with varying field names: "code", "wireCode", "wire",
"file", "fixed_file", "output", "result", etc. Added 15+ known names + fallback that
finds any string property containing "class"/"static"/"void". Also: find { anywhere
in text, not just StartsWith. Root cause of CS1002 errors (raw JSON written as Wire.cs).

### 3. ISequence API Reference (commit 22280bd)
Real API from DafnyRuntime source: .Count (property), .Select(i) (indexer),
.CloneAsArray(). LINQ also works since ISequence implements IEnumerable<T>.

### 4. WireFixer Specialist (commit 5050b73, 4c7629b)
Dedicated agent that gets ONLY: compile errors/test failures + Wire.cs + ISequence API.
Like a plumber — doesn't redesign, just fixes the leak. Fires on both Docker build
failures AND test failures. Writes fixed Wire.cs back to DB (UpdateWireCsInDbAsync).

### 5. Wiring Retry Loop (commit a42ce1f, 4c7629b)
Docker build/test failure → extract compile errors → call WireFixer → update DB →
re-run harness. Up to 3 retries.

### 6. Cut-out Type Cross-Check (commit 4d38d41)
Compares architect's declared return types against cut-out ACTUAL return types from
the registry. Catches "declared string, actually seq<seq<string>>" before Dafny runs.
StripDafnyReturnName handles "name: type" prefix from Dafny returns clause.

### 7. Post-Dafny ISeq→string Compatibility (commit 4d38d41)
ISequence (any depth) → string is now compatible in TypeChainChecker. The
WiringGenerator's ConvertType can convert any depth to string. This unblocked T4's
type chain.

### 8. Stdin Support (commit 4c7629b)
BotHarness pipes stdin for stdin-type programs: -i flag + RedirectStandardInput.
GenerateTestData has temperature format ("32 F", "20 X"). WiringGenerator doesn't
check args.Length for stdin programs.

### 9. TypeChainChecker Actionable Guidance (commit 4e6a794)
FormatErrors now says HOW to fix: "serialize output to string" for ISeq→string,
"use ReadLines for CSV" for string→ISeq. Handles both Dafny (seq<) and C# (ISequence)
notation.

## Known Blockers for Next Session

### T4 — Two issues:
1. **WireFixer can't find previous Wire.cs**: harness deletes temp dir on re-run.
   Need to save Wire.cs before harness cleans up, or read from DB bundle.
2. **Cotton candy**: Dafny tokenizes chars not words. Z3-verified but wrong logic.
   Needs a DafnyFixer specialist (same pattern as WireFixer): gets test failure +
   Dafny code + spec, fixes Dafny, Z3 re-verifies.

### T5 — Multi-input architecture:
Linear chain can't express "read file1, read file2, merge." Needs richer connection
format (multiple entry points) or model-based merge logic in wiring.

### T6 — Test expectations wrong:
Architect says "Prints '32 F'" for input "32 F" but conversion gives "0 C". The
program is correct, the test expectation is wrong. Needs better architect test case
generation or QA test data fix (JSON parse bug: TestDataFile[] deserialization fails).

## Git State

- Branch: master
- Latest commit: 4c7629b — T6 stdin support + WireFixer test-failure loop
- Working tree: clean
- 10+ commits this session

## Build Status

**0 errors, 1 pre-existing warning (BotHarness.cs:329 CS8625).**