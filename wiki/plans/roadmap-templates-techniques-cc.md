# Posit Roadmap — Templates, Techniques, and Cyclomatic Complexity

## Vision

Posit is a factory for a specific category of programs (CLI data transformation tools).
The pipeline produces verified programs from natural-language specs through:
architecture → implementation → QA → Docker harness.

The goal: deterministic generation for the 70% of programs that follow known structural
patterns, with model-generated code for the 30% that need genuine creativity. Over time,
templates expand the deterministic share and the model is used less.

## Current State (2026-08-30)

- **Architecture: 11/12 pass** (only T1 fails — custom types)
- **Implementation: 3/12 pass** (T2, T3, T4)
- **Remaining 9 failures are all implementation bugs:**
  - Format bugs (T8, T9): model produces right data, wrong output shape
  - Build errors (T10, T11): type collisions, missing references
  - Logic bugs (T5, T6, T7, T12): merge sort, arithmetic, command loop, override

## Three Layers of Intelligence

### Layer 1: Templates (deterministic generation)

**What:** parameterized C# code patterns for the 70% structural shapes.
A template is a pattern with holes — the contract fills the holes (method names,
types, test cases), the template produces the implementation. No model call.

**Shapes to template first (by trial coverage):**
1. Linear transformer (T1, T2, T11): read → parse → transform → serialize → print
2. Filter + aggregate (T3, T8): read → filter by criteria → count/aggregate → format → print
3. Validator + report (T9): read → validate rows → count valid/invalid → format report
4. Multi-file merger (T5, T12): read two sources → validate compatibility → merge → output
5. Multi-step pipeline (T10): read → filter → convert → group → serialize
6. Aggregation + sort (T4): read → tokenize → aggregate → sort → print

**Source of templates:** derived from passing trial implementations (SourceCodeBundle
artifacts in posit_artifacts). The implementation code is parameterized — spec-specific
values become template variables. NOT stored as code in the wiki (that's cheating);
stored as parameterized templates in a template library.

**WireFixer replacement:** the WiringGenerator should produce correct Wire.cs. Each
compile error the WireFixer fixes is a generator bug. Log them, fix the generator,
remove the WireFixer when the log is empty.

### Layer 2: Techniques (self-healing memory)

**What:** short, abstract design principles extracted from passing trials.
"Return DATA from validation, not bool." "Keep everything as string[] in a filter chain."
These are NOT code — they're guidance the model reads alongside the contract.

**Storage:** same wiki.wiki_chunks table, type='technique'. No new table, no new schema.
Retrieved by WikiSearcher alongside interface patterns, proven contracts, and C# reference.

**Self-healing lifecycle (autonomous, no human, no LLM judgment):**
- Write gate: before storing a new technique, check similarity against existing techniques.
  If >0.85 cosine match, skip (duplicate). If no match, store with trust=1.
- Promotion: technique injected into prompt + trial passes → trust +1.
- Demotion: technique injected into prompt + trial fails → trust -1.
- Auto-delete: trust drops below floor (e.g., -2) → DELETE. No manual prune.
- Trust ceiling: trust caps at e.g. 10 to prevent dominance.
- Extraction: mechanical pattern matching on implementation code (regex/AST), NOT LLM
  summarization. "Uses string.Split(',')" is a pattern. "Uses Dictionary with override"
  is a pattern. If code matches no known pattern, no technique is written.

**Cross-language:** techniques tagged by language (tags='csharp,validation'). When Dafny
returns, Dafny techniques get tags='dafny,validation'. Retrieval filters by current
phase language. Language-agnostic design principles tagged with both.

### Layer 3: Dafny (proven correctness, multi-language output)

**What:** verified templates. A Dafny template is written once, proven correct by Z3 for
ALL inputs, then compiled to C# (or Go, Python, Java). The model never writes Dafny.

**When:** after C# templates are working and the template matcher is proven. Each C#
template gets a Dafny counterpart that proves it correct. The C# version is generated
from the Dafny version.

**Why Dafny compiles to multiple languages is key:** the template library becomes
language-agnostic at the core. Write the algorithm in Dafny once, prove it, compile to
whatever the user needs. C# today, Go tomorrow, Python next week.

## Cyclomatic Complexity (CC) — the quality metric

CC counts decision points (if, else, for, while, switch, &&, ||, catch). A method with
CC=1 is straight-line. CC=5 has moderate branching. CC=10+ is complex.

### Three gates, one metric, three pipeline points

| Gate | When | What it catches | Action |
|---|---|---|---|
| Contract CC limit | After architect | Over-complex method design | Force decomposition: "method X has implied CC=N, limit is M, split into smaller methods" |
| CC-based template matching | Template selection | Wrong template for spec's complexity | Select template whose method CC matches the spec's implied CC |
| Implementation CC check | After impl, before harness | Model overcomplicated the code | "Your Merge has actual CC=12 but contract implied CC=4. Simplify." |

### CC calculation

**Implied CC (from spec):** count decision-relevant keywords in the spec text per method.
Keywords: if, when, case, each, or, either, unless, except, while, for, switch.
Fully deterministic — no LLM call, no AST analysis.

**Actual CC (from code):** count decision points in the generated C# using Roslyn or
regex matching on if/for/while/switch/&&/||/catch. Post-implementation, pre-harness.

### Two-chance escalation

1. Attempt 1: CC limit = 5 per method. Architect must decompose into simple methods.
2. If all attempts fail at CC=5: bump to CC=8. Allow more complex methods.
3. If CC=8 also fails: bump to CC=12. The spec may genuinely need complex logic.
4. If CC=12 fails: let it through — the model tries its best with no complexity constraint.

### CC as positive guidance (not just rejection)

The CC gate doesn't just reject — it tells the architect WHERE to split:
"ProcessCommand has implied CC=8. Split into ParseCommand (CC=3) + ExecuteCommand (CC=5)."
This goes into the surgical-edit retry signal, giving the architect actionable decomposition
guidance instead of generic "try again."

## Implementation Order

1. **Contract CC gate** (highest leverage — forces clean decomposition, root cause of most
   impl failures). Add to ContractFidelityChecker. Implied CC from spec keywords. Two-chance
   escalation. Positive decomposition guidance in correction signal.

2. **Format bug fixes** (T8, T9 — lowest effort, highest trial count impact). The contract
   has OutputFormat and EmptyOutputText. The model's return type doesn't match what EmitPrint
   expects. Fix the implementation prompt or add a deterministic format-application step.

3. **Implementation CC check** (catches overcomplication before harness runs). Post-impl gate.
   Compare actual CC against contract implied CC. Reject if actual >> implied.

4. **C# templates** (the 70% deterministic layer). Template library + template matcher.
   Derived from passing trial implementations. Parameterized by contract.
   WireFixer → generator fixes (log compile errors, fix in generator, remove WireFixer).

5. **Technique store** (self-healing memory for the 30% creative cases). Same wiki table,
   type='technique'. Mechanical extraction (pattern matching). Trust scoring. Auto-delete.
   Post-harness hooks (extract on success, demote on failure).

6. **Throttle binary search** (find minimum wiki context between 5 and 15 for reliable arch).

7. **Dafny modular addition** (verified templates, multi-language output). After C# templates
   are proven. Each C# template gets a Dafny proof. Dafny compiles to C#, Go, Python, Java.

8. **Production readiness** (spec checker, Docker-compose, user-facing failure messages,
   session resumption, architecture checkpoint).

## Key Decisions

- C# templates first, Dafny modularly second. Build the factory, then upgrade the quality
  gate from "tested" to "proven."
- Dafny as template language, not generation target. Human writes Dafny template once, Z3
  verifies once, compiles to any target. Model never writes Dafny.
- No proven implementations in the wiki (cheating — answer key, not guidance). Templates
  replace them: same knowledge, parameterized instead of hardcoded.
- Temperature: implementation 0.0 (translation), WireFixer 0.0 (mechanical), ImplFixer 0.1
  (targeted fixes), architecture 0.3 (decomposition needs flexibility).
- Self-healing memory: harness verdicts are the only feedback signal. No LLM judgment in the
  maintenance path. Extraction is mechanical pattern matching, not summarization.
- CC is one metric applied at three gates. Build the calculation once, reuse three times.
- Techniques, templates, and C# reference all live in wiki.wiki_chunks — one table, one
  retrieval, one injection point. Language-tagged for cross-language support.

## What We Have Not Solved

- General programming. Posit is a factory for CLI data transformation tools, not a general
  programmer. It doesn't handle web servers, GUIs, databases, distributed systems, or
  persistent state.
- The 30% creative cases (regex, arithmetic, command loops). Templates handle structure;
  the model handles content. Dafny will prove the templates are correct for all inputs.
- T7 (command loop) — inherently CC=8+, the pipeline's connection model doesn't express
  branching stdin loops. This is a structural gap that needs connection-model expansion.
- T1 (custom types) — the architect invents types (ValidationResult, CsvData) that don't
  exist in Posit's native type set. Needs a type-constraint gate or a type whitelist.