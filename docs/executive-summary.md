# Posit Pipeline — Executive Summary

## What Posit Does

Posit is a spec compiler. You give it a natural language spec ("A CLI tool that reads a CSV file, parses each line into fields, validates that all rows have the same number of fields, transforms each row into a JSON object with field names from the header row, and prints the JSON array to stdout"). Posit produces a working, tested, Docker-compiled program.

## Pipeline Layers (Top to Bottom)

### Layer 1: Architecture (AI — Model-Generated)

The architect reads the spec and decomposes it into components. Each component has:
- Name, responsibility, classification (Dafny logic vs io-shell stub)
- Method signatures (input types → output types)
- Connections (which method calls which)
- Test cases (acceptance criteria)

**Output:** ArchitectureContract (JSON)

**Quality gate:** ContractScanner — checks method names exist, connections resolve, no dead declarations. Kicks back to architect on mismatch.

### Layer 2: Data Flow Spec (DETERMINISTIC — Type-Checked)

The connections form a data flow graph. This layer type-checks the graph BEFORE any code is written.

- Entry: file path (args[0]) or stdin (Console.ReadLine)
- Steps: linear chain with branching (if isValid → error path)
- Exit: print to stdout

**Quality gate:** TypeChainChecker — checks output type of step N is compatible with input type of step N+1. Uses finite type vocabulary: `string`, `seq<string>`, `seq<seq<string>>`, `int`, `bool`, `void`. Conversion table is enumerable and deterministic.

**Status:** 80% built. Needs clean data flow artifact + correction loop.

### Layer 3: Dafny Implementation (AI + Z3 — Hybrid)

Two paths:

- **Cut-out path (deterministic):** Pre-written, Z3-verified Dafny modules exist on disk. Translate directly to C#. No model call. Fast.
- **Custom path (AI-generated):** No cut-out exists. Model writes Dafny from spec + reference card. Z3 verifies. If Z3 fails, correction signal routes back.

**Quality gate:** Z3 (Dafny program verifier). Proves all proof obligations. If verification fails, kick back with errors.

**Output:** DafnyVerificationResult (verified Dafny + translated C# path)

### Layer 4: C# Translation (DETERMINISTIC)

Z3Runner translates verified Dafny to C#. The translation is deterministic — same Dafny always produces same C#.

- PostProcessTranslation extracts the module namespace from the raw Dafny output
- The translated C# uses DafnyRuntime types (ISequence<Rune>, BigInteger)
- The C# is written to staging as `{ComponentName}.cs`

**Quality gate:** Docker build (compiles the C#)

### Layer 5: Stub Caps (DETERMINISTIC — Snap-On)

I/O stubs are C# implementations of Dafny `{:extern}` portals. They snap onto the translated Dafny:
- FileIO (ReadFile, WriteFile, ReadLines, WriteLines)
- ConsoleIO (ReadLine, PrintLine, Print)
- NetworkIO, DatabaseIO, StreamIO, TimeRandom

The stub caps are pre-written, compile-clean C# templates. They're selected by the architect and materialized by the pipeline.

### Layer 6: Wiring (AI — Model-Generated, Docker-Verified)

The wiring (Wire.cs) connects the components. It's the entry point (Main method) that:
1. Reads input (file path arg or stdin)
2. Calls method A, passes result to method B, passes to method C
3. Applies type conversions at Dafny/io-shell boundaries (finite table)
4. Branches on validation (if isValid is false, print error, exit 1)
5. Prints the result

**Two generators:**
- **ModelWiringGenerator (primary):** Model sees connections + signatures + type conversion table, writes Wire.cs. Handles console, multi-input, branching.
- **WiringGenerator (fallback):** Rule-based, handles linear file→process→print only.

**Quality gate:** Docker build (compiles Wire.cs) + bot harness (runs program, compares output)

### Layer 7: QA (AI + DETERMINISTIC — Hybrid)

Two parts:

- **Test data generation (AI):** QaPhase calls LLM to generate spec-specific test data with edge cases. Falls back to deterministic stopgap data if AI fails.
- **Bot harness (DETERMINISTIC):** Builds Docker image, runs program with test data, captures output, compares to expected behavior.

**LLM failure analysis:** If tests fail, LLM classifies the failure (wrong output format, crash on empty input, type mismatch) and suggests a fix.

**Quality gate:** Bot harness (output matches expected behavior)

### Layer 8: DafnyDB Flywheel (FUTURE — Growth System)

Successful trials feed the catalog. Only Z3-verified + Docker-passing solutions go in the DafnyDB. The architect sees proven solutions in the prompt and uses them. The catalog grows. More trials pass. The flywheel accelerates.

**Quality gate:** Z3 verification + Docker test pass = add to DafnyDB

## Correction Loops

Each layer has a quality gate. If the gate fails, a correction signal routes back:

```
Architecture → ContractScanner → fix → retry
Data Flow → TypeChainChecker → fix → retry
Dafny → Z3 → fix → retry
C# → Docker build → fix → retry
Wiring → Docker build → fix → retry
QA → Bot harness → fix → retry
```

**Status:** ContractScanner correction loop works. TypeChainChecker correction loop works (but needs richer feedback). Z3 correction loop needs wiring. Docker/Wiring retry loop needs wiring. QA retry loop needs wiring.

## Trial Results (Aug 17, 2026)

| Trial | Spec | Tests | Status |
|-------|------|-------|--------|
| T1 | CSV→JSON | 3/3 | ✅ PASS |
| T2 | JSON→CSV | 2/2 | ✅ PASS |
| T4 | Word Counter | 1/1 | ✅ PASS |
| T3 | Filtered CSV (branching) | — | ❌ Blocked (scanner rejects unused error-path methods) |
| T5 | Multi-File Merge | — | ❌ Blocked (linear chain can't express two-input merge) |
| T6 | Temperature Converter | 5/5 run | ❌ Close (model over-decomposes, prints unit not value) |

## Technology Stack

- **.NET 10** — target framework
- **Dafny 4.11** — specification language, Z3-verified
- **Z3 4.12** — theorem prover (verifies Dafny, translates to C#)
- **Ollama** — model gateway (localhost:11434)
- **deepseek-v4-flash:cloud** — architecture, Dafny implementation, wiring, QA
- **Docker** — build + run generated programs
- **PostgreSQL 18 + pgvector** — artifact storage, DafnyDB registry
- **Blazor Server** — dashboard (localhost:5123)

## Cut-Out Catalog (15 modules, 32 VC, 0 errors)

| Cut-out | Responsibility | Methods |
|---------|---------------|---------|
| csv-parser | Parse CSV lines into fields | ParseLine, ParseLines, CountFields |
| row-validator | Validate row field counts | ValidateRows |
| json-serializer | Serialize rows to JSON | SerializeToJson, SerializeRow |
| json-parser | Parse JSON array to rows | ParseJsonToArray |
| csv-serializer | Serialize rows to CSV | SerializeToCsv |
| word-tokenizer | Tokenize text into words | Tokenize, CountWords |
| frequency-aggregator | Count word frequencies | CountFrequency |
| temperature-converter | Convert temperatures | Convert |
| priority-queue | Priority queue operations | Enqueue, Dequeue, ListAll |
| log-parser | Parse log lines | ParseLogLine, FilterByLevel, CountByLevel |
| price-converter | Convert prices | ConvertPrice |
| category-grouper | Group by category | GroupByCategory |
| link-extractor | Extract markdown links | ExtractLinks |
| ini-parser | Parse INI config | ParseIni |
| config-merger | Merge configs | MergeConfigs |

## Edge Case Catalog (81 patterns)

| Category | Count | Examples |
|----------|-------|---------|
| InputValidation | 33 | EmptyString, NullInput, UnicodeEmoji, MaxInt, FloatNaN |
| SqlInjection | 15 | ClassicOrOneEqualsOne, DropTableComment, UnionSelect |
| Concurrency | 12 | RaceCondition, Deadlock, RetryStorm, CancellationToken |
| ApiError | 21 | 400MalformedJson, 401ExpiredToken, 429RateLimitHit |

## Known Issues

1. Model over-decomposes when freed from registry — needs "keep it simple" prompt (2-3 components)
2. Wiring retry loop not wired — Docker fails, no retry
3. T3 branching — scanner rejects unused error-path methods
4. T5 multi-input — model-based wiring should handle, needs simpler architecture
5. QA test data JSON parse — model returns wrong shape for TestDataFile[]
6. MaxRetriesPerPhase=3 may be too low for complex specs

## Next Steps

1. Add "keep it simple" prompt (2-3 components: one I/O, one logic)
2. Wire correction loops (TypeChainChecker → fix → retry, Docker → fix → retry)
3. Raise MaxRetriesPerPhase to 5
4. Re-run T1-T6
5. Build data flow spec layer (type-check before code)
6. Connect DafnyDB flywheel (successful trials → catalog)
7. Build composition (Approach 5 — pre-verified module assemblies)