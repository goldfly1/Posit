# Gauntlet Results & Road to Software Factory — Aug 28, 2026

## Part 1: Gauntlet Results (T6, T8, T12 — Aug 28)

All three trials run end-to-end on deepseek-v4-flash:cloud via the CLI pipeline
(`dotnet run --project src/Posit.Cli -- run --spec=...`). Sessions logged to Postgres.

| Trial | Harness verdict | True verdict | What actually happened |
|-------|----------------|--------------|------------------------|
| T6 Temp Converter | success=True, 4/4 | **FAIL** | WireFixer fixed parse crash but broke unit validation — `'0 C'` → `Error: unknown unit C`. Judge passed it (prose expected outputs). |
| T8 Log Analyzer | success=False | **FAIL** | `ERROR: 0` for everything. ImplFixer regenerated identical 675-char file twice (fixation). userLen=132 in impl-fix prompt = correction signal starvation. |
| T12 Config Merger | success=True, 1/1 | **FAIL (masked)** | WireFixer repaired broken Wire.cs (genuine win), but program printed `ConfigMerger.MergeResult` (a type name) instead of merged config. Structural layer passed it. Only 1 of 3 test cases ran. |

**Score: 0/3.** T1 remains the only true pass (n=1, easiest trial, linear flow).

### What worked
- Architecture phase: clean decompositions, valid interfaces, all 3 trials
- Build loop: impls compiled on attempt 1 (T6) / within retries
- WireFixer on compile errors: fixed T12's malformed Wire.cs
- Docker harness: built, ran, reported
- The FSM/orchestration: no crashes, no hangs, clean session records

### The five failure classes (ranked by damage)

**F1. The judge is a rubber stamp (CRITICAL — masks all other failures).**
Root cause chain: architect writes prose expected behavior (`"prints result"`,
`"prints 'ERROR: 2' and exits 0"`) → TestSuite expectedOutputs are prose →
QaJudge Layer 1 (exact match) has no comparable string → falls to structural
check → structural check only asks "non-empty stdout?" → type-name output
`ConfigMerger.MergeResult` PASSES. Three trials, three wrong programs, all
certified by QA. Fixing the pipeline without fixing the judge means flying blind.

**F2. Correction-signal starvation (ImplFixer is deaf).**
ImplFixer's feedback prompt was userLen=132 for T8 — the test failures were NOT
in the prompt. The model got a near-empty user message and regenerated the same
code. Two identical 675-char outputs = deterministic fixation, exactly as
predicted. The signal exists in Program.cs but doesn't reach the model.

**F3. WireFixer over-reach (fixes one thing, breaks another).**
T6: WireFixer told to fix "parse error" rewrote unit validation into a form
that rejects valid input. No regression check: fixer output replaces the file,
harness re-runs, but the judge (F1) can't detect the regression.

**F4. Deterministic WiringGenerator emits invalid C# (T12).**
`Wire.cs(18,69): error CS1026: ) expected` ×8. WiringGenerator handles simple
signatures but breaks on some argument shapes. Recoverable via WireFixer, but
it burns the fixer budget on a deterministic component's bug.

**F5. Test case selection/coverage is thin.**
T12 ran 1 test case of 3. Harness test-case extraction from the contract is
losing cases. Fewer tests = fewer chances to catch wrong programs.

### What did NOT fail (notably)
- Model fixation at the ARCHITECT level: each trial got a fresh, sane decomposition
- Restart variance: untested (no restarts fired — budget consumed by fixers instead)
- Exact-match judging where concrete expected output existed: worked (T6 tc1)

## Part 2: Road to Software Factory — Step-by-Step Plan

Sequencing principle: **fix the measurement before the manufacturing.**
The judge must tell the truth before any pipeline change can be evaluated.

### Phase A — Make the judge honest (F1) [~1 session]

**A1. Concrete expected output at architecture time.**
Change the architect prompt + ArchitectureContract: each test case MUST carry
`expectedOutput` (exact string) and `expectedExitCode`. Prose `expectedBehavior`
stays for human context only. The spec strings in trial-specs.md already contain
these (e.g. `ERROR: 2`) — the contract just needs to capture them.

**A2. PseudodataBot consumes concrete answers.**
Bot's `// test:` comment parsing becomes the FALLBACK, not the primary. Primary
answer key = architect's expectedOutput. If neither exists → test case is
skipped-with-warning, never structurally judged.

**A3. Structural check hardens.**
Reject outputs that are bare type names (`Foo.Bar` regex), exception dumps, or
stack traces. Cheap regex gate before structural PASS.

**A4. QaReport propagates real verdicts.**
BotHarness must carry the actual JudgeVerdict (layer + reason) into QaReport —
not rebuild a fake exact-match verdict at the end (current bug at BotHarness.cs
QaReport.Build call).

**Verify:** re-run T6 + T12. They must now FAIL LOUDLY (not silently pass).
This is success criteria for Phase A: truthful failure.

### Phase B — Feed the fixers (F2, F3) [~1 session]

**B1. ImplFixer prompt surgery.**
Include in the impl-fix user prompt: (a) original spec, (b) the component's
current source, (c) per-test-case table: input → expected → actual → exit code,
(d) the Wire.cs call sites. Target userLen ≥ 2000. The 132-char prompt is the bug.

**B2. Fixer regression gate.**
After a fixer rewrites a file, harness re-runs ALL test cases (already does) —
but now with the honest judge (Phase A), a regression auto-rejects the fix:
revert to previous file version before next retry. Fixers must never make the
score worse.

**B3. Retry diversity.**
Retry 2+ of any fixer: raise temperature (0.3→0.7) AND prepend "Previous
attempt produced: <old source>. It produced these wrong outputs: <table>.
Produce a DIFFERENT approach." Fixation needs a perturbation to break.

**Verify:** T8 re-run. Pass = correct `ERROR: 2` output, or at minimum
*different* code each retry (fixation broken).

### Phase C — Fix the deterministic layer (F4, F5) [~1 session]

**C1. WiringGenerator: add a unit test corpus.**
Collect every Wire.cs shape the trials generate; add T12's failing argument
shape. Fix the emitter until all corpus cases compile. Deterministic code
should never need an LLM fixer.

**C2. Harness test-case selection fix.**
T12 ran 1/3 test cases. Audit ExtractTestCases + BotHarness selection: all
contract test cases must map to harness runs. Match by ID, not index.

**C3. Pre-flight Wire.cs compile in-process.**
Before Docker: `dotnet build` the temp project locally (fast, no Docker cache
misses). Compile errors found in seconds, not minutes, and WireFixer gets the
error list before Docker build.

**Verify:** T12 re-run end-to-end: 3/3 test cases execute; Wire.cs compiles
first try; program output judged by exact match.

### Phase D — The honest gauntlet [1-2 sessions]

Re-run T1, T6, T8, T12 with the fixed pipeline. 3 attempts each. Record:
- End-to-end pass rate per trial (target: 4/4 trials pass ≥1 of 3 attempts)
- Failure class distribution (architecture vs impl vs wiring vs QA)
- Restart variance: does attempt 2 differ from attempt 1?
- Cost per successful program (model calls, tokens — from prompts_log)

**Gate:** If ≥3/4 trials pass: proceed to Phase E. If <3/4: the bottleneck
data will name the phase — loop back to its fix. Do NOT build TUI/GUI on top
of an unproven pipeline.

### Phase E — QA clean room + TUI (steps 4-5 of qa-phase-redesign)

**E1. Move QA fully into Docker** — pseudodata + judge run inside the container
alongside the test runner (completes qa-phase-redesign step 4).

**E2. TUI terminal** — key-mapping convention (Ctrl+F1 first field, Tab next),
carapace-to-page renderer, QA bot drives it with keystrokes in Docker
(completes step 5 + the "Next" terminal). Prove it on a T7-style interactive
trial (task queue via stdin).

### Phase F — Factory hardening [ongoing]

**F1. Carapace review bot** (approved in assessment): second model call after
architecture — "does this interface implement this spec? what's misread?" —
before any impl is written. Add after Phase D data shows architecture failure rate.

**F2. Per-phase model routing** (deferred by design until pipeline proven —
now proven or not by Phase D). Architect gets the strongest available model;
impl/fixers stay cheap.

**F3. Trials T2-T5, T7, T9-T11** — expand the gauntlet to the full Tier 0-1 set.

**F4. Cost/budget enforcement** — BudgetRemaining exists but isn't enforced in
the retry loops. Cap model calls per session from prompts_log data.

### Success definition (the factory works when...)

1. Tier 0-1 gauntlet: ≥80% of trials produce a correct, judge-verified program
   within 3 attempts
2. Zero silent failures: every FAIL is truthful, every PASS is exact-match
3. Median cost per program ≤ 20 model calls
4. One interactive (TUI) program built and bot-driven end-to-end
5. All of the above reproducible in the Docker clean room

## Appendix: Evidence

- T6 session XUKY2VvoVk2yP37ePvb82Q0000: tc2/tc3 `Error: unknown unit C` after fixer
- T8 session: ImplFixer ×2 identical 675-char regenerations, userLen=132
- T12 session: Wire.cs CS1026 ×8 → fixed by WireFixer → `ConfigMerger.MergeResult` PASSED
- T1: only true pass (handoff-2026-08-26, 3/3, first Docker attempt)