# QA Phase Redesign — Aug 27, 2026

## Status: DESIGN COMPLETE — ready for implementation

## Context

The current QaPhase is misnamed — it's an LLM test data generator, not a QA gate.
The actual verification happens in BotHarness + Program.cs retry loops (WireFixer 6
retries + ImplFixer 3 retries = 9 model calls trying to fix what the model already
got wrong). This redesign makes QA a deterministic gate with a clean pass/fail.

## Design

### Pipeline (simplified)

```
Build phase:
  C#Impl → 4 build retries → PASS or fail
  If build fails → WireFixer (2 retries) → PASS or hash (restart)

QA phase (Docker clean room):
  Pseudodata → Test runner → Judge (exact/invariant/heuristic) → Report
  FAIL (exact/invariant) → ImplFixer (2 retries) → PASS or hash (restart)
  FAIL (heuristic) → human review
```

WireFixer and ImplFixer stay but are capped at 2 retries each. A cheap fix
is a cheap fix — don't throw it away. The problem was unbounded retries
(6 + 3 = 9), not the concept. Total correction budget: 4 (build) + 2 (wire)
+ 2 (impl) = 8 model calls max before hash. Down from 13.

### QA Phase Components

#### 1. Pseudodata Bot (deterministic)

Reads carapace interfaces (C# interface method signatures) to determine input
types. Generates typed test data at scale (1K, 10K, 100K records).

- `IParser.ParseLines(string[] lines)` → generates CSV rows
- `IConverter.Convert(double temp, string unit)` → generates "32 F", "0 C", "20 X"
- `IAnalyzer.CountByLevel(string[] lines, string level)` → generates log lines

The interface IS the spec for the data shape. No LLM call. No guessing from
test case names. Deterministic, reproducible.

For computable transformations (Type 1: CSV→JSON, temperature conversion), the
bot also computes expected output. The transformation rule is in the spec — the
bot applies it.

#### 2. Test Runner Prefab (deterministic)

Feeds pseudodata through the program. CLI now (same as current Docker harness).
GUI later — the frontend-as-pipe insight: same carapace, different terminal.

Lives inside Docker. Clean environment. Every run measured against the same base.

#### 3. Judge (three layers)

**Layer 1: Exact match (deterministic)**
For computable transformations. Bot generated expected output. Judge compares
actual stdout (whitespace-trimmed) + exit code against expected. PASS/FAIL.

**Layer 2: Invariant check (deterministic)**
For complex outputs (trucking logistics, schedulers, CI/CD engines). The
architect defines validator methods on the carapace interface:

```csharp
interface ILogAnalyzer {
    Dictionary<string,int> CountByLevel(string[] lines, string level);
    bool ValidateResult(string[] input, Dictionary<string,int> output, string level);
}
```

The model implements both. The judge calls the method, then calls the validator
on the output. If validator returns false → FAIL. No separate spec format —
the invariant lives in the carapace.

**Layer 3: Heuristic check (one LLM call, low temp 0.1)**
Only fires when exact match AND invariants both pass. If those fail, we already
know it's broken.

```
Input: spec + sample output (truncated) + "All invariant checks passed"
Prompt: "Does anything about this output look wrong or unexpected given the spec?
         Output PASS or FAIL with one sentence explaining why."
Output: PASS or FAIL + reason
```

#### 4. Report

Pass/fail per layer + anomaly flags. Deterministic summary.

### Decision Tree

```
Exact match:  PASS → next layer
              FAIL → restart

Invariants:   PASS → next layer
              FAIL → restart

Heuristic:    PASS → ship
              FAIL → human reviews report → restart or accept
```

### Failure Handling

- **Build fails after 4 retries** → WireFixer (2 retries) → hash (restart)
- **QA exact/invariant fails** → ImplFixer (2 retries) → hash (restart)
- **QA heuristic fails** → human review (not restart — subjective call)
- **WireFixer capped at 2 retries** (was 6)
- **ImplFixer capped at 2 retries** (was 3)
- **Total correction budget: 8 model calls max** (was 13)

The pipeline is non-deterministic. The architect produces a different
decomposition each run. Restart is the exception handler, not human debugging.

### What's Removed

- WireFixer 6 retries → capped to 2 (cheap fix stays, unbounded retries go)
- ImplFixer 3 retries → capped to 2
- LLM test data generation in QaPhase — replaced by deterministic bot
- LLM failure analysis in BotHarness — removed (diagnostic only, never used)
- QaModuleResult vestigial records — removed
- Fuzzy CompareOutput rubber stamp — removed (exact match + invariants only)

### What Stays

- C#Impl build correction loop (4 retries) — stays, compiler is the teacher
- Docker harness build + run — stays, now the QA clean room
- ArchitectureContract, TestSuite, SourceCodeBundle artifacts — stays
- Carapace doctrine — stays, now with validator methods for invariants

### What's New

- Pseudodata bot (reads interfaces, generates typed data + expected output)
- Validator methods on carapace interfaces (architect defines invariants)
- Heuristic check (one LLM call, anomaly detection)
- QA report (structured pass/fail per layer)
- Docker as QA clean room (full phase, not just harness)

### What's Deferred (separate conversations)

- Frontend-as-pipe: GUI prefab as test runner terminal
- Per-phase model routing
- Heuristic check prompt design (refine when we have real outputs to test)

### Open Questions

1. How does the architect know which invariants to write? (Answer: the spec
   says "every package must be delivered" — the architect translates that to
   a validator method. Same job as writing test cases today, just structured.)
2. Can the pseudodata bot handle all Tier 0-1 trial types? (Answer: yes for
   computable transforms. For complex specs, bot generates input + invariants
   check properties. Heuristic covers the rest.)
3. Should the build correction loop also be 4 retries then hash? (Answer: yes,
   decided. 4 retries during build, then WireFixer 2 retries, then restart.
   Consistent with QA principle — bounded correction, not unbounded.)