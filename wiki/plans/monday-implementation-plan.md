# Monday Implementation Plan — Aug 17, 2026

> **Status after Aug 14 session:** Steps 1-3 done. Step 4 (DafnyDB) partially done.
> Docker compiles generated code. 3 Z3-verified cut-outs written and loaded.
> ContractScanner catches invented method names and dead declarations.
> The remaining problems are identified and enumerated below.

## Where We Are

- Solution builds clean: 0 errors, 0 warnings
- 3 cut-outs Z3-verified: csv-parser (3 verified), row-validator (1), json-serializer (2)
- ContractScanner: three-way check (real cut-out methods, declared in contract, used in connections)
- Wiring: deterministic linear chaining (output of step N → first param of step N+1)
- Docker: generated code compiles, tests run, fail on semantic issues (cotton candy)

## Three Problems to Fix Monday

### Problem 1: Cut-outs Don't Chain (Type Chain Break)

**What:** `ValidateRows` returns `_IValidationResult` (a verdict). `SerializeToJson` expects
`ISeq<ISeq<ISeq<Rune>>>` (the rows). The chain breaks because the cut-outs were written
independently without a shared type contract.

**The Wire.cs today:**
```
ret0 = FileIO.ReadFile(...)          → string
ret1 = ParseLines(FromElements(ret0), default)  → ISeq<ISeq<ISeq<Rune>>>  (wrong: wraps, doesn't split)
ret2 = ValidateRows(ret1)            → _IValidationResult  (verdict, not data)
ret3 = SerializeToJson(default(...)) → ISeq<Rune>  (wrong: gets default, not ret2)
PrintLine(ret3)                      → void
```

**Fix: Redesign cut-outs to pass data through.**

Every cut-out in a pipeline must accept the data type the previous cut-out produces
and return the data type the next cut-out expects. Verdicts/flags ride alongside, not
instead of, the data.

Specifically:
- `ValidateRows(rows: seq<seq<string>>) → (rows: seq<seq<string>>, isValid: bool)`
  Returns the rows AND the verdict. The chain continues with the rows.
- `SerializeToJson(rows: seq<seq<string>>) → string`
  Takes the rows (from ValidateRows, not a verdict).
- `ParseLines(lines: seq<string>, delimiter: string) → seq<seq<string>>`
  Takes lines (already split), returns parsed rows.

The shared type vocabulary for tabular data pipelines:
- `string` = file content or CLI input
- `seq<string>` = lines (split by newlines)
- `seq<seq<string>>` = rows of fields (parsed CSV)
- `string` = JSON output (serialized)

**Files to change:**
- `patterns/cut-outs/row-validator.dfy` — return rows alongside verdict
- `patterns/cut-outs/json-serializer.dfy` — verify it takes rows directly
- `patterns/cut-outs/csv-parser.dfy` — verify ParseLines takes seq<string> (lines)
- Re-verify all 3 with Z3 after changes
- Re-translate all 3 to C#

### Problem 2: Two Dimensionality Conversions (Semantic Mismatch)

**What:** The wiring's `ConvertType` wraps instead of splits when upgrading dimensionality.

| Conversion | Wiring does | Should do |
|---|---|---|
| `string → ISeq<ISeq<Rune>>` | FromElements(UnicodeFromString(x)) — wraps as 1-element | Split by newlines |
| `ISeq<Rune> → ISeq<ISeq<Rune>>` | FromElements(x) — wraps as 1-element | Split by delimiter |

These are the ONLY 2 semantic compatibility issues. Everything else is either correct
or a safe default.

**Fix options (pick one Monday):**

**Option A: Fix in the wiring (ConvertType)**
- `string → ISeq<ISeq<Rune>>`: split by `\n` using a helper that splits a C# string
  into a Dafny seq of seqs by newline
- `ISeq<Rune> → ISeq<ISeq<Rune>>`: split by a delimiter char (default comma?)
  This is less clear — the wiring doesn't know what delimiter to use
- Pro: no cut-out changes needed
- Con: the wiring makes semantic decisions (what to split by)

**Option B: Fix in the stubs (preferred per user's insight)**
- The file-io stub already has `ReadLines(path) → string[]` (C#) which maps to
  `seq<string>` (Dafny). If the architect selects `ReadLines` instead of `ReadFile`,
  the stub returns lines, not the whole file. No dimensionality upgrade needed.
- The Dafny stub declaration for `ReadLines` returns `seq<string>`, which maps to
  `ISeq<ISeq<Rune>>`. The C# stub returns `string[]`. The conversion is
  `string[] → ISeq<ISeq<Rune>>` which is a natural mapping (array to seq of seqs),
  not a wrap.
- Pro: stubs are the I/O boundary — line splitting IS file I/O work, not domain logic
- Con: need to make sure the Dafny stub for ReadLines matches the C# stub

**Option C: Cut-outs handle it**
- `csv-parser.dfy` already has `ParseLines(lines: seq<string>, delimiter: string)`
  which takes lines (not the whole file). If the wiring calls `ReadLines` (stub)
  → `ParseLines` (cut-out), the types chain: `string[] → ISeq<ISeq<Rune>>` →
  `ISeq<ISeq<ISeq<Rune>>>`. No dimensionality upgrade needed.
- Pro: cleanest — each piece does its job
- Con: the architect must select `ReadLines` not `ReadFile` (prompt can enforce this)

**Recommended: Option B + C together.** Stubs return the right shape, cut-outs
consume it. The wiring never needs to upgrade dimensionality.

**Files to change (if Option B+C):**
- `src/Posit.Cli/Orchestration/PromptBuilder.cs` — tell architect to use ReadLines
  for file input, not ReadFile
- `src/Posit.Phases/WiringGenerator.cs` — remove the 2 dimensionality wrap
  conversions from ConvertType (or make them throw — they shouldn't be needed)
- Verify the Dafny stub for ReadLines matches the C# stub signature

### Problem 3: Type Chain Check (Post-Dafny Validation)

**What:** The ContractScanner runs during Architecture, before Dafny translation.
It can check the model's declared types, but those are fiction. The real types
only exist after Dafny Implementation. We need a type chain check AFTER translation.

**The flow:**
```
Architecture → Dafny Contracts → Dafny Implementation → TYPE CHAIN CHECK → C# Implementation
```

**What the check does:**
After Dafny Implementation, the real C# types exist (from TranslatedCSharpScanner).
For each consecutive pair of connections on the CLI component:
1. Get step N's return type (from scanned C# signatures)
2. Get step N+1's first parameter type (from scanned C# signatures)
3. Check: are they compatible? (same type, or ConvertType handles it)
4. If not: kick back to Architecture with "your chain breaks at step N:
   X returns _IValidationResult but Y expects ISeq<ISeq<ISeq<Rune>>>"

**What's hiding in the weeds (already identified):**
1. The model's declared types are fiction — can only check REAL types after translation
2. The chain isn't always linear — a step might need data from an earlier step,
   not the immediately preceding one. The linear chaining assumption breaks here.
   Fix: the architect's argMappings should specify which previous return to use.
   The deterministic chaining uses the immediately previous return by default.
3. Type compatibility isn't type equality — `string` is assignable to `ISeq<Rune>`
   via UnicodeFromString. The check needs to know about ConvertType's mappings.
4. Only 2 semantic issues exist (the dimensionality upgrades from Problem 2).
   Once those are fixed, the type chain check is straightforward.

**Retry loop:** If the type chain breaks, kick back to Architecture. The model
retries with the error message. 2-3 tries is expected and acceptable.

**Files to create/change:**
- New: `src/Posit.Phases/TypeChainChecker.cs` — post-Dafny type chain validation
- `src/Posit.Cli/Orchestration/PositOrchestrator.cs` — call TypeChainChecker
  after Dafny Implementation, before C# Implementation
- `src/Posit.Core/State/FsmReducer.cs` — route type chain failures back to Architecture

## Monday Execution Order

1. **Fix Problem 1** — redesign cut-outs to chain (return data + verdict)
   - Rewrite row-validator.dfy: `ValidateRows → (rows, isValid)`
   - Verify all 3 cut-outs with Z3
   - Re-translate to C#
   - Build clean

2. **Fix Problem 2** — stubs return the right shape
   - Update prompt: use ReadLines for file input (returns lines, not whole file)
   - Remove dimensionality wrap from ConvertType (or make it throw)
   - The chain becomes: ReadLines → ParseLines → ValidateRows → SerializeToJson → PrintLine
   - Types: string[] → ISeq<ISeq<Rune>> → ISeq<ISeq<ISeq<Rune>>> → ISeq<ISeq<ISeq<Rune>>> → ISeq<Rune> → string

3. **Run T1 through Docker** — expect build + tests pass (or at least run correctly)

4. **Fix Problem 3** — add TypeChainChecker
   - Post-Dafny type chain validation
   - Kick back to Architecture on mismatch
   - Run T1 again — expect type chain check passes on first or second try

5. **Run T2, T5** — verify the pattern works for other data processing trials

6. **Commit and update wiki**

## Key Decisions Made This Session

- **DafnyDB cut-outs** are the path, not parameter substitution or model-generated Dafny
- **ContractScanner** checks three-way: real cut-out methods, declared, used. Kicks back on mismatch.
- **Deterministic linear chaining** in wiring — no trusting the model for parameter names
- **Stubs are the I/O boundary** — line splitting, type shaping belongs in stubs, not wiring
- **Only 2 semantic compatibility issues exist** — both are dimensionality upgrades (wrap vs split)
- **Type chain check belongs after Dafny translation**, not during Architecture
- **2-3 retries is acceptable** — the FSM handles it, the model learns from correction signals
- **The contract KNOWS the chain** — it's not a mystery, we just haven't used that information yet
- **Orchestrator is a routing table, not a worker** — it has connections, no pattern needed.
  The wiring IS its implementation. The pipeline pattern body on the orchestrator is dead code.
  Scanner rule: a component with connections doesn't require patternName. The connections are the spec.