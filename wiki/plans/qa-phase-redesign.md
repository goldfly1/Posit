# QA Phase Redesign — Aug 27, 2026

## Status: ALL 5 STEPS COMPLETE (dead code + fixer caps, pseudodata bot, three-layer judge, Docker clean room, TUI terminal)

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

The interface IS the spec for the data shape. The bot reads interface signatures
for type shapes AND reads the architect's test case categories (valid input, edge
case, invalid input, empty) to generate appropriate data for each category. Still
deterministic, but spec-aware, not just type-aware.

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
- Fuzzy CompareOutput keyword rubber stamp — replaced with structural check
  (valid format? right fields? right structure?) instead of keyword matching

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

- Frontend-as-pipe: GUI prefab as test runner terminal (see Terminal Architecture below)
- Per-phase model routing
- Heuristic check prompt design (refine when we have real outputs to test)

### Terminal Architecture

The carapace defines methods. Terminals render those methods to users. One pipe,
four terminals. All call the same carapace methods — the carapace doesn't care
who called it.

| Terminal | Where | QA bot drives it? | Key-mapping? | Priority |
|----------|-------|-------------------|--------------|----------|
| **CLI** (pipe) | Data transformers, scripts | ✅ Yes (stdin/args) | No — no fields | ✅ Built |
| **TUI** (interactive terminal) | Docker QA, servers, SSH | ✅ Yes (keystrokes) | ✅ Yes | **Next** |
| **GUI** (desktop app) | End-user business software | ❌ Not in Docker | ✅ Yes | Later |
| **AAI** (AI assist interface) | Human conversational access | ❌ Non-deterministic | No | Later |

#### Key-Mapping Convention (for TUI and GUI)

Standardized so the QA bot always knows where every field is without discovery,
and every program behaves the same way everywhere:

- **Ctrl+F1** = first page, focus on first field
- **Tab** = next field (standard accessibility)
- Every field has a known position in the tab order
- Pages are standardized — same layout conventions across all programs

This is the carapace doctrine applied to the user interface. The carapace
standardizes the code interface (method signatures). The key-mapping
standardizes the user interface (field positions, navigation). Both serve
the same purpose: the consumer knows where things are without discovery.

#### TUI is the QA Terminal

The QA bot runs in Docker. Docker containers don't have display servers.
The TUI is what the QA bot actually drives — deterministic keystrokes
(Ctrl+F1, Tab N times, type data, Enter). No computer vision, no DOM
inspection, no headless browser. Just keystrokes in a terminal.

The TUI is the first GUI terminal, not a later addition. It's the one QA
uses. The desktop GUI is a second terminal for humans who want windows.

#### CLI Always Available

Even when a TUI or GUI exists. The carapace methods are always there.
A hot-shot dev can always write to the command line. Data transformers
(CSV→JSON, temperature converter) are CLI-only — no GUI needed.

#### AAI is Human-Facing Only

The AAI translates natural language ("show me overdue shipments") into
carapace method calls (IShipmentTracker.GetOverdue()). Thin wrapper over
existing interfaces — no new carapace fields, no new infrastructure. But
it's non-deterministic, so the QA bot never uses it. Build after TUI.

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