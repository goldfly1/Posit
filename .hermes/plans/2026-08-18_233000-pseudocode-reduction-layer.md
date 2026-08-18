# Pseudocode Reduction Layer Implementation Plan

> **For Hermes:** Use subagent-driven-development skill to implement this plan task-by-task.

**Goal:** Add a pseudocode reduction layer between Architecture and Dafny Implementation that recursively reduces spec-level descriptions into Dafny-statement-level fragments, which the Dafny writer glues together into verified modules.

**Architecture:** The reducer takes method signatures + responsibility from the architect and produces increasingly concrete pseudocode passes. Each pass replaces English-language concepts with Dafny language elements from the reference card vocabulary. The reduction stops when every line uses only Dafny tokens. The Dafny writer takes the final reduced pseudocode + method signatures and produces a complete verified module (fragments + contracts + scaffolding). Fixers see only Dafny — never pseudocode.

**Tech Stack:** .NET 10, C#, Dafny 4.11, Z3 4.12, Ollama (deepseek-v4-flash:cloud)

---

## Communication Flows

```
Architect ← "how do I connect this?" ← Reducer
Reducer   ← "did you leave something out?" ← Dafny writer
Fixer     → "I can't get there from here" → fixes Dafny directly
```

- The reducer and Dafny writer have a conversation (upstream).
- The fixers are standalone — they see only Dafny code + test failures + reference card.
- Dafny code is the single source of truth for fixers. No pseudocode in fixer context.

## The Reference Card as Dictionary

The reference card (`patterns/dafny-reference-card.dfy`) defines the Dafny vocabulary:

**Language primitives:** `while`, `if`, `match`, `:=`, `var`, `|s|`, `s[i]`, `s[a..b]`, `+`, `[]`, `requires`, `ensures`, `invariant`, `decreases`, `method`, `function`, `datatype`, `{:extern}`, `module`, `return`

**Stdlib functions:** `Seq.Map`, `Seq.Filter`, `Seq.Sort`, `Seq.Reverse`, `Seq.Flatten`, `Seq.Range`, `Seq.IndexOf`, `Seq.Contains`, `Set`, `Map`, `Multiset`, `Ord`

The reduction is complete when every line of pseudocode uses only these tokens — no English verbs remain.

## Crystallization Detection

**Deterministic check:** Scan each line for Dafny keywords/tokens. If a line has NO Dafny token from the vocabulary, it's still English prose — reduce again.

**Model self-assessment:** "If all lines are Dafny syntax, output STOP."

Both together: the model says STOP, the deterministic check confirms. If they disagree, reduce again.

## Reduction Example

```
Method: Convert(temp: int, unit: string) returns (result: int, outUnit: string)
Responsibility: Convert temperature between C, F, K

Pass 0 (from spec):
  "Convert temperature between C, F, K. If F, convert to C. If C, convert to F or K."

Pass 1:
  "if unit[0] == 'F' then result := (temp - 32) * 5 / 9; outUnit := 'C'
   if unit[0] == 'C' then result := temp * 9 / 5 + 32; outUnit := 'F'
   if unit[0] == 'K' then result := temp - 273; outUnit := 'C'"
  → Deterministic check: every line has `if`, `:=`, `[` → Dafny tokens present
  → Model: STOP
  → Crystallized.
```

---

## Pipeline Change

Current:
```
Architecture → DafnyImplementation → CSharpImplementation → QA → Harness
```

New:
```
Architecture → PseudocodeReduction → DafnyImplementation → CSharpImplementation → QA → Harness
```

The PseudocodeReduction phase:
- **Input:** ArchitectureContract (method signatures, responsibility, test cases)
- **Output:** PseudocodeReduction artifact (per-method reduction chains)
- **Model:** deepseek-v4-flash:cloud (same as DafnyImplementation)
- **No Z3** — pseudocode isn't verified, it's reduced

The DafnyImplementationPhase changes:
- When patternName is null (now always), it reads the pseudocode artifact
- The prompt includes the final reduced pseudocode as the "algorithm spec"
- The model writes complete Dafny: pseudocode fragments + contracts + scaffolding

---

## Files to Change

### New files:
- `src/Posit.Phases/PseudocodeReductionPhase.cs` — the reduction phase
- `src/Posit.Contracts/Artifacts/PseudocodeArtifacts.cs` — artifact types

### Modified files:
- `src/Posit.Cli/Orchestration/PromptBuilder.cs` — add pseudocode phase prompt
- `src/Posit.Cli/Orchestration/PositOrchestrator.cs` — register new phase
- `src/Posit.Cli/Program.cs` — register phase in BuildOrchestrator
- `src/Posit.Core/State/KnownPhases.cs` — add pseudocode phase
- `src/Posit.Core/Graph/DependencyGraphEngine.cs` — add dependency edge
- `src/Posit.Phases/DafnyImplementationPhase.cs` — read pseudocode artifact, include in prompt
- `src/Posit.Contracts/Artifacts/ArchitectureContract.cs` — add PseudocodeKind to ArtifactKind enum

---

## Tasks

### Task 1: Define Pseudocode Artifacts

**Files:**
- Create: `src/Posit.Contracts/Artifacts/PseudocodeArtifacts.cs`
- Modify: `src/Posit.Contracts/Artifacts/ArchitectureContract.cs` (add ArtifactKind.PseudocodeReduction)

**Content:**
```csharp
namespace Posit.Contracts.Artifacts;

/// <summary>
/// Per-method pseudocode reduction chain. Each method gets a list of passes,
/// each more concrete than the last. The final pass is Dafny-statement-level.
/// </summary>
public record PseudocodeReductionResult
{
    public string ModuleName { get; init; } = "";
    public Dictionary<string, string[]> MethodReductions { get; init; } = new();
    public bool IsComplete { get; init; }  // true when all methods crystallized
}

/// <summary>
/// Full pseudocode artifact for a session. One per component.
/// </summary>
public record PseudocodeReductionBundle
{
    public PseudocodeReductionResult[] Results { get; init; } = [];
}
```

Add `PseudocodeReduction` to the `ArtifactKind` enum.

### Task 2: Add Phase Registration

**Files:**
- Modify: `src/Posit.Core/State/KnownPhases.cs` — add `PseudocodeReduction`
- Modify: `src/Posit.Core/Graph/DependencyGraphEngine.cs` — add edge
- Modify: `src/Posit.Cli/Program.cs` — register in BuildOrchestrator

The pseudocode phase depends on Architecture and is a dependency of DafnyImplementation.

### Task 3: Build PseudocodeReductionPhase

**Files:**
- Create: `src/Posit.Phases/PseudocodeReductionPhase.cs`

**Core logic:**

1. Read ArchitectureContract from input artifacts
2. For each non-io-shell component:
   a. Start with responsibility + method signatures as pass 0
   b. Call model to reduce: "Here is pass N. Reduce to Dafny tokens. If already Dafny, output STOP."
   c. Check crystallization: scan output for Dafny vocabulary tokens
   d. If STOP + crystallized → done. If not → another pass.
   e. Max 5 passes (safety limit)
3. Store reduction chains in PseudocodeReductionResult
4. Stage as ArtifactKind.PseudocodeReduction

**The reduction prompt:**
```
You are reducing pseudocode to Dafny language elements.

Method signature: method Convert(temp: int, unit: string) returns (result: int, outUnit: string)
Responsibility: Convert temperature between C, F, K

Current pseudocode (pass N):
  [previous pass content]

Dafny vocabulary (use ONLY these tokens):
  while, if, match, :=, var, |s|, s[i], s[a..b], +, [], requires, ensures, 
  invariant, decreases, method, function, datatype, {:extern}, module, return,
  Seq.Map, Seq.Filter, Seq.Sort, Seq.Reverse, Seq.Flatten, Set, Map, Multiset

Reduce every line to use ONLY Dafny tokens. Replace English verbs with Dafny syntax.
If ALL lines already use only Dafny tokens, output STOP.
Otherwise output the reduced pseudocode.
```

**Crystallization check (deterministic):**
```csharp
private static readonly HashSet<string> DafnyTokens = new() {
    "while", "if", "match", ":=", "var", "|", "[]", "+", "requires", "ensures",
    "invariant", "decreases", "method", "function", "datatype", "extern",
    "module", "return", "Seq.", "Set.", "Map.", "Multiset", "Ord"
};

private static bool IsCrystallized(string pseudocode)
{
    foreach (var line in pseudocode.Split('\n'))
    {
        var trimmed = line.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) continue;
        if (trimmed.StartsWith("//")) continue;
        var hasDafnyToken = DafnyTokens.Any(t => trimmed.Contains(t));
        if (!hasDafnyToken) return false; // still English
    }
    return true;
}
```

### Task 4: Wire Pseudocode into DafnyImplementationPhase

**Files:**
- Modify: `src/Posit.Phases/DafnyImplementationPhase.cs`

The `GenerateDafnyAsync` method changes:
1. Read PseudocodeReduction artifact from input artifacts
2. Find the reduction for this component
3. Get the final pass (last element of each method's reduction chain)
4. Include it in the prompt:

```
Method signatures to implement:
  method Convert(temp: int, unit: string) returns (result: int, outUnit: string)

Reduced pseudocode (implement this logic in Dafny):
  if unit[0] == 'F' then result := (temp - 32) * 5 / 9; outUnit := 'C'
  if unit[0] == 'C' then result := temp * 9 / 5 + 32; outUnit := 'F'
  ...

Dafny Reference Card:
  [reference card content]

Rules:
  1. Write a COMPLETE Dafny module with method, requires/ensures, module wrapper.
  2. The pseudocode is the algorithm — translate it into proper Dafny.
  3. Add contracts (requires/ensures) that match the method signatures.
  4. Add invariants and decreases for loops.
  5. Keep {:extern} declarations for I/O portals.
  6. Z3 must verify the code.
```

### Task 5: Build and Verify

**Steps:**
1. `dotnet build src/Posit.Cli/Posit.Cli.csproj` — 0 errors
2. Run T6 (temperature) — the simplest pure-logic spec
3. Check: architecture passes, pseudocode reduces, Dafny writes, Z3 verifies, tests pass
4. Run T4 (word counter) — more complex (aggregation)
5. Run T5 (multi-file merge) — multi-input

### Task 6: Handle Reduction Failures

If the reducer can't crystallize in 5 passes:
- The pseudocode is stored as-is (best effort)
- The Dafny writer gets whatever we have
- The DafnyFixer handles wrong logic downstream
- Log a warning: "Pseudocode for X did not fully crystallize after 5 passes"

If the Dafny writer says "did you leave something out?":
- This is a correction signal from DafnyImplementation back to PseudocodeReduction
- Route via ForceRollback (same as type chain errors route to Architecture)
- The reducer gets the Dafny writer's complaint as a correction signal

---

## What the Fixers See

| Fixer | Sees pseudocode? | Sees Dafny? | Routes back to pseudocode? |
|---|---|---|---|
| WireFixer | No | Yes (Wire.cs) | No |
| DafnyFixer | No | Yes (Dafny source) | No — fixes Dafny directly |
| Architect retry | No | No | No |
| DafnyImpl → Pseudocode | Yes (its input) | No | Yes — via ForceRollback |

**The fixers never see pseudocode. Dafny is the single source of truth for fixers.**

---

## Risks

1. **Reducer paraphrases instead of reducing** — pass N is not more concrete than pass N-1. Mitigation: deterministic crystallization check detects this and stops.

2. **Extra model calls for simple specs** — T1 (CSV→JSON) doesn't need 3 passes. Mitigation: pass 0 might already be crystallized (method signatures + responsibility in Dafny tokens). Skip immediately.

3. **Reducer produces wrong logic** — the formula is incorrect. Mitigation: DafnyFixer + test harness catch it. The Dafny is the source of truth.

4. **Dafny writer ignores pseudocode** — writes its own logic. Mitigation: this is actually fine — Z3 verifies regardless. The pseudocode is a hint, not a constraint.

5. **Model wraps pseudocode in JSON** — same as Dafny/wiring. Mitigation: recursive JSON extraction (already built).

---

## Open Questions

1. **Should the reducer see test cases?** Yes — test cases constrain the logic. "test: Convert(32, 'F') returns (0, 'C')" tells the reducer what the formula should produce.

2. **Should pseudocode be stored in DB?** Yes — as ArtifactKind.PseudocodeReduction. Every reduction pass is captured. The full chain is in the artifact payload. The prompt log captures every model call. The audit events capture every phase transition.

3. **How many passes typically?** Unknown — let the trials tell us. T6 (temperature) might be 1-2. T4 (word counter) might be 2-3. Complex specs might be 4-5.

4. **Does the Dafny writer need the full reduction chain or just the final pass?** Just the final pass. The chain is for debugging. The Dafny writer gets the crystallized pseudocode + method signatures + reference card.

## DB Capture (Everything)

Every element of the reduction is persisted. Nothing is ephemeral.

### Artifacts (ArtifactRepository)
| Artifact | Kind | What's stored |
|---|---|---|
| Architecture contract | ArchitectureContract | Components, signatures, connections, test cases |
| Pseudocode reduction | PseudocodeReduction | Per-method reduction chains (all passes), crystallization status |
| Dafny verification | DafnyVerification | Verified Dafny source, Z3 output, translated C# path |
| Source code bundle | SourceCodeBundle | All C# files (translated Dafny + stubs + Wire.cs) |
| Test suite | TestSuite | Test data files, module results |

### Prompt log (PromptLogger)
Every model call is logged:
- Architecture prompt + response
- Each pseudocode reduction pass (pass 0, 1, 2, ...) prompt + response
- Dafny implementation prompt + response
- C# wiring prompt + response
- QA test data prompt + response
- WireFixer each attempt prompt + response
- DafnyFixer each attempt prompt + response
- LLM failure analysis prompt + response

Each log entry: session_id, phase_id, attempt_number, model_id, input_tokens, output_tokens, system_prompt, user_prompt, response_text, timestamp.

### Audit events (AuditRepository)
Every phase transition and correction signal:
- Phase started, phase succeeded, phase failed
- Correction signal dispatched (what errors, routing where)
- Retry requested (attempt N)
- ForceRollback (routing back to pseudocode or architecture)
- Crystallization detected (pass N, method name)
- Crystallization failed (max passes reached)

### Sessions (StateStore)
Session state at every checkpoint:
- Current phase, current attempt, completed phases
- CorrectionSignal (errors from last failure)
- PreviousOutput (model's last output for retry)
- LoopbackCount (architecture rollbacks)
- DesignContext (snowballed context across phases)

### What the PseudocodeReduction artifact stores

```json
{
  "results": [
    {
      "moduleName": "TempConverterLogic",
      "methodReductions": {
        "Convert": [
          "Convert temperature between C, F, K. If F, convert to C...",
          "if unit[0] == 'F' then result := (temp - 32) * 5 / 9; outUnit := 'C'...",
          "STOP"
        ]
      },
      "isComplete": true
    }
  ]
}
```

Every pass is in the array. The last element is either "STOP" (crystallized) or the best-effort final pass (if max passes reached). The Dafny writer reads the last non-STOP element. The DB has the full chain for debugging.

### What the fixer attempts store

WireFixer and DafnyFixer don't create new artifacts — they update existing ones (SourceCodeBundle for Wire.cs, DafnyVerification for Dafny). But every model call is in the prompt log with the attempt number, so the full correction history is reconstructable from the prompt log + audit events.