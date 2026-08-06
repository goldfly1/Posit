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
  - `class` with `var` fields and `predicate Valid()` (predicate HAS a body — it's a definition)
  - `method` signatures with `requires`/`ensures` — **bodyless, no `{ }`**
  - `function` signatures with `requires`/`ensures` — **bodyless, no `{ }`**
  - `constructor` signatures with `ensures` — **bodyless, no `{ }`**
  - `include "OtherModule.dfy"` if cross-file types are needed
- Contracts written to disk as `.dfy` files (same pattern as type shells)
- Mixed modules: architect outputs two Component records (one dafny, one io-shell)

### Dafny Contracts Phase (NEW — between Design Review and Implementation)
- Materializes `.dfy` skeleton files to staging
- Runs `dafny verify` on skeletons (bodyless methods — Z3 checks contract well-formedness)
- If skeleton verification fails, correction signal loops **back to Architecture** (max 2 loopbacks)
- After exhausting loopbacks, module is downgraded to `io-shell`
- Produces `DafnyContractResult` with contract source + verification status per module
- Verified skeletons preserved; only failed modules loop back

### Implementation Phase — Two Passes

Implementation is split into two passes. One spec, two passes. The architect's Dafny sidewalk runs until it can't, stubs the I/O with `{:extern}` portals, and keeps walking. Pass 1 fills the Dafny bodies. Pass 2 plugs C# into the extern portals.

#### Pass 1: Dafny Implementation (deepseek-v4-pro:cloud)
- Imp gets `.dfy` skeleton files on disk (already verified by Dafny Contracts)
- Imp fills in method/function bodies in Dafny (not C#)
- Z3 verifies complete program (skeleton contracts + Imp's bodies)
- On Z3 failure: correction signal with exact proof error (retry within phase)
- On Z3 success: `dafny translate cs` produces C# with `partial class` + extern holes
- `{:extern}` methods stay bodyless — they become holes in the partial class
- Translated C# drops into staging alongside I/O shell modules

#### Pass 2: C# Implementation (glm-5.2:cloud)
- Imp gets translated C# (from Pass 1) + extern holes + type shells
- Imp writes C# that plugs into the `partial class` extern holes
- Imp wires I/O shells to call translated Dafny methods
- Build judge checks compilation (no Z3 — this is unverified I/O)
- On build failure: correction signal with compiler errors (existing Shepherd pattern)

### QA Phase (modified)
- For verified modules: compile translated C# only (no test generation)
- For unverified modules: full test generation (existing behavior)
- `IsVerified` flag already wired — QA already skips stubs/edges

### Imp Appeal Process (NEW)
- When QA fails an io-shell module, Imp can appeal with a reason
- Appeal goes to kimi-2.7-code:cloud (independent — hasn't touched code or tests)
- Kimi reads architect spec + Imp code + QA test + appeal reason
- Decision: test wrong (QA rewrites) OR code wrong (Imp fixes) OR both
- Max 1 appeal per module, then REVIEW_GATE (human)
- Only io-shell modules — Dafny modules have Z3 as judge, no appeal possible
- Tracking: monitor if appeal improves workflow, mitigates tokens, both, or neither

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
| Design Review | kimi-2.7-code:cloud | Independent review — separation from architect |
| Dafny body writing (Pass 1) | deepseek-v4-pro:cloud | Proven 2/5, understands architect's contract intent |
| C# shell writing (Pass 2) | glm-5.2:cloud | Plugs into extern portals, wires I/O to translated Dafny |
| File operations, wiki search | local ollama | Fast, no reasoning needed |
| QA test generation | glm-5.2:cloud | Tests unprovable modules, knows module intent |
| Imp appeal reviewer | kimi-2.7-code:cloud | Independent arbiter — hasn't touched io-shell code or QA tests |

## Implementation Steps

### Step 1: Dafny Contracts Phase (NEW)
- [ ] Create `DafnyContractsPhase` — architect writes .dfy skeletons
- [ ] Create `DafnyContractArtifact` type
- [ ] Wire into pipeline between Design Review and Implementation
- [ ] Add `dafny` / `io-shell` flag to `Component` record

### Step 2: Dafny Implementation Phase (Pass 1)
- [ ] For `dafny` modules: write .dfy bodies instead of .cs files
- [ ] Run Z3 verify on completed .dfy files
- [ ] On success: `dafny translate cs` → C# with partial class + extern holes
- [ ] `{:extern}` methods stay bodyless — they become holes for Pass 2

### Step 2b: C# Implementation Phase (Pass 2)
- [ ] Imp gets translated C# from Pass 1
- [ ] Write C# that plugs into `partial class` extern holes
- [ ] Wire I/O shells to call translated Dafny methods
- [ ] Build judge checks compilation (existing Shepherd pattern)

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
3. **deepseek-v4-pro:cloud for Dafny contracts and bodies** — proven better than glm-5.2:cloud for Dafny. Design Review uses kimi-2.7-code:cloud for separation.
4. **Translated C# may need post-processing** — strip Dafny runtime wrappers, add using statements. Acceptable for now.
5. **Module decomposition is enforced by line count** — 200 lines max forces small, focused modules.
6. **Skeletons use bodyless methods** — no `{ }` after method/constructor signatures. Empty bodies create proof obligations that fail. Bodyless methods are abstract specs; Z3 checks contract well-formedness only.
7. **Cross-file dependencies use `include`** — each .dfy file is independent. Z3 verifies as a batch.
8. **Mixed modules split at Architecture** — the architect outputs two Component records (one dafny, one io-shell). The pipeline does not split modules.
9. **Skeleton correction loops back to Architecture** — max 2 loopbacks, then downgrade to io-shell.
10. **FSM inherits Shepherd's states** — same escalation chain, new transitions for Dafny skeleton/body verification.
11. **C# only** — multi-target is future. No target-language abstraction now.
12. **Determinism is target-specific** — not a core property. Only relevant if Rust target is added.
13. **Imp appeal process for io-shell modules** — kimi-2.7-code:cloud reviews appeals. Max 1 per module. Architect spec is the source of truth. Under observation: does it improve workflow, mitigate tokens, or neither?