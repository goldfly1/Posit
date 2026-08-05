# Posit — Dafny-First Pipeline Plan

## Name

**Posit** — a spec compiler. The architect posits contracts (requires/ensures). Z3 confirms or denies. The code that survives is proven. Nothing ships unproven.

## Vision

Wall-to-wall Dafny: everything provable is proven, everything else is a thin I/O shell.

Architect writes Dafny contracts (exoskeleton). Imp writes Dafny bodies (meat). Z3 verifies. `dafny translate cs` produces deployable C#. QA compiles translated code only — no test generation for verified modules.

## Pipeline Order

```
Ideation → Architecture → API Definition → Pseudocode → Design Review
  → Dafny Contracts (NEW — architect writes .dfy skeletons with requires/ensures)
  → Implementation (Imp fills in Dafny bodies, Z3 verifies)
  → QA (compiles translated C#, runs tests for unverified modules only)
  → Deployment → Observability → Documentation
```

## Phase Changes

### Architecture Phase
- Architect writes Dafny contract files (`.dfy`) instead of C# type signatures
- Each module gets a `.dfy` file with:
  - `datatype` for enums/records
  - `class` with `var` fields and `predicate Valid()`
  - `method` signatures with `requires`/`ensures` (no bodies — just `{ }`)
  - `function` signatures with `requires`/`ensures` (no bodies)
- Contracts written to disk as `.dfy` files (same pattern as type shells)

### Dafny Contracts Phase (NEW — between Design Review and Implementation)
- Materializes `.dfy` skeleton files to staging
- Runs `dafny verify` on skeletons (contracts only, no bodies) — verifies the spec is sound
- If skeleton verification fails, correction signal back to architect
- Produces `DafnyContractArtifact` with contract source per module

### Implementation Phase (modified)
- Imp gets `.dfy` skeleton files on disk (already exist with contracts)
- Imp fills in method/function bodies in Dafny (not C#)
- Multi-pass: skeleton verify → body fill → Z3 verify
- On Z3 failure: correction signal with exact proof error
- On Z3 success: `dafny translate cs` produces C#
- Translated C# drops into project alongside I/O shell modules

### QA Phase (modified)
- For verified modules: compile translated C# only (no test generation)
- For unverified modules: full test generation (existing behavior)
- `IsVerified` flag already wired — QA already skips stubs/edges

## Module Classification

| Type | Dafny? | Example |
|------|--------|---------|
| Pure logic | YES | Parser, validator, SQL generator, schema mapper |
| I/O shell | NO | File reader, CLI wrapper, database writer, HTTP client |
| Mixed | Partial | Config loader (file I/O + parsing) — Dafny for parsing, C# for I/O |

Architect marks each module as `dafny` or `io-shell` in the architecture contract.
Only `dafny` modules go through the Dafny pipeline.
`io-shell` modules go through the existing C# implementation path.

## Limits and Boundaries

### Dafny Source Limits (per module)
- Max 200 lines
- Max 10 methods/functions
- Max 5 classes/datatypes
- Max 3 requires/ensures clauses per method (keep proofs tractable)

### Prompt Budget
- Dafny system prompt: hard cap 16K chars
- Wiki results: 4K cap (already set)
- Compiled API / module signatures: 8K cap
- Syntax quick reference: ~2K (already in prompt)

### Z3 Verification
- `--verification-time-limit 30` (default)
- `--standard-libraries` (already set)
- `--resource-limit` configurable via env var

### Output Tokens
- 16K max for Dafny generation (already set in template)

### Module Decomposition
- If a module needs >200 lines of Dafny, split it
- Each module should prove one clear responsibility
- I/O boundaries are explicit — pure functions only in Dafny

## I/O Shell Pattern

```csharp
// C# I/O shell — NOT verified, just compiles
public class CsvFileReader
{
    public string ReadFile(string path)
    {
        return File.ReadAllText(path); // side effect — outside proof boundary
    }
}

// Dafny verified module — translated to C# by dafny translate cs
// class CsvParser { method Parse(line: string) returns (rows: seq<seq<string>>)
//   requires |line| > 0
//   ensures |rows| >= 1 ... }
```

The I/O shell calls into the translated Dafny code. The proof covers the logic.
The I/O is outside the proof boundary.

## Agent Model Assignments

| Task | Model | Why |
|------|-------|-----|
| Dafny contract writing (architect) | deepseek-v4-pro:cloud | Better at formal reasoning |
| Dafny body writing (imp) | deepseek-v4-pro:cloud | Proven 2/5 on first run |
| C# I/O shell writing (imp) | glm-5.2:cloud | Existing behavior, works fine |
| File operations, wiki search | local ollama | Fast, no reasoning needed |
| QA test generation | glm-5.2:cloud | Existing behavior |

## Implementation Steps

### Step 1: Dafny Contracts Phase (NEW)
- [ ] Create `DafnyContractsPhase` — architect writes .dfy skeletons
- [ ] Create `DafnyContractArtifact` type
- [ ] Wire into pipeline between Design Review and Implementation
- [ ] Add `dafny` / `io-shell` flag to `Component` record

### Step 2: Modify Implementation Phase
- [ ] For `dafny` modules: write .dfy bodies instead of .cs files
- [ ] Run Z3 verify on completed .dfy files
- [ ] On success: `dafny translate cs` → drop C# into project
- [ ] For `io-shell` modules: existing C# path unchanged

### Step 3: Modify Architecture Phase
- [ ] Architect prompt: write Dafny contracts for pure-logic modules
- [ ] Architect prompt: mark modules as `dafny` or `io-shell`
- [ ] Output Dafny contract source in architecture artifact

### Step 4: Limits and Budgets
- [ ] 200-line cap on Dafny source per module
- [ ] 16K system prompt cap for Dafny phases
- [ ] 10 method / 5 class limits per module

### Step 5: Testing
- [ ] Run trial with Dafny-first pipeline
- [ ] Verify: architect writes contracts → Z3 verifies skeleton → Imp fills bodies → Z3 verifies → translate cs → QA compiles
- [ ] Compare success rate vs current pipeline

## Key Decisions

1. **Partial verification is success** — already implemented. Verified modules skip QA, unverified fall through.
2. **Dafny for pure logic only** — I/O shells stay in C#. The proof boundary is the function signature.
3. **deepseek-v4-pro:cloud for all Dafny work** — proven better than glm-5.2:cloud for Dafny.
4. **Translated C# may need post-processing** — strip Dafny runtime wrappers, add using statements. Acceptable for now.
5. **Module decomposition is enforced by line count** — 200 lines max forces small, focused modules.