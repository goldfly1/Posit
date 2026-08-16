# Posit Trial Specs — Revamped Aug 16, 2026

## What's Wrong With the Old Trials

The old T1-T24 trials have two fundamental problems:

### Problem 1: They don't make anything real

The Tier 0 trials (T1-T12) are described in one sentence each — "CSV-to-JSON CLI," "document processing," "task scheduler." There's no spec of what the output should look like, no acceptance criteria, no test data. The pipeline generates code that compiles and Z3-verifies, but as the Aug 14 session proved, it's **cotton candy** — the code is proven correct against generic contracts but doesn't do what the spec asked. T5 built and ran in Docker but tests failed because the patterns are generic, not spec-specific.

The Tier 1-3 trials (T13-T24) are the opposite — massive prose descriptions ("a modular ERP with procurement, inventory, manufacturing, sales order management, CRM, human resources, payroll...") with no cut-outs to support them. They're north-star fantasies, not testable trials.

### Problem 2: They don't have enough spikes to gather what we need

A trial needs to spike specific capabilities:

| Spike | What it tests | Old trials |
|-------|--------------|------------|
| Linear data flow | Read → parse → validate → transform → write | T1 (barely) |
| Branching data flow | Conditional routing based on validation result | None |
| Multiple I/O boundaries | File + console, file + database | None |
| Error propagation | Validation failure → user-facing error message | None |
| Type chain | Output of step N feeds step N+1 with correct type | T1 (broken, now fixed) |
| Cut-out coverage | Trial needs cut-outs that exist | T1 only (3 cut-outs) |
| Spec-specific behavior | Output matches the spec, not just the pattern | None (cotton candy) |
| Edge case survival | Program handles bad input without crashing | None |
| Multi-component wiring | 3+ components wired together | T8 (9 components) |
| Orchestrator pattern | Component with connections, no patternName | None (was blocked, now fixed) |

The old trials test "does the pipeline run" but not "does the program work."

## What the New Trials Look Like

Each trial has:
1. **A precise spec** — what the program does, input/output format, acceptance criteria
2. **Test data** — concrete input files and expected output
3. **Cut-out mapping** — which existing cut-outs cover which parts
4. **Spike targets** — which capabilities this trial exercises
5. **Edge case hooks** — which edge case patterns apply

## Tier 0: Spike Trials (T1-T8)

Each trial targets ONE spike. Minimal — 2-4 components. Fast to run and verify.

### T1 — CSV to JSON Transformer

**Spike:** Linear data flow, type chain, cut-out coverage
**Spec:** A CLI tool that reads a CSV file, parses each line into fields, validates that all rows have the same number of fields, transforms each row into a JSON object with field names from the header row, and prints the JSON array to stdout. The tool takes a file path as input.
**Test data:**
```
Input (test.csv):
name,age,city
Alice,30,NYC
Bob,25,LA
Carol,35,SF

Expected output:
[{"name":"Alice","age":"30","city":"NYC"},{"name":"Bob","age":"25","city":"LA"},{"name":"Carol","age":"35","city":"SF"}]
```
**Cut-outs:** csv-parser (ParseLines), row-validator (ValidateRows), json-serializer (SerializeToJson)
**Chain:** ReadLines → ParseLines → ValidateRows → SerializeToJson → PrintLine
**Edge cases:** EmptyString, NullInput, VeryLongString10K, BoundaryAtMin (1 row), DuplicateEntriesInUniqueCollection (duplicate field names)
**Components:** 3 dafny (parser, validator, serializer) + 2 io-shell (file-reader, console-output) + 1 orchestrator = 6

### T2 — JSON to CSV Transformer

**Spike:** Reverse data flow, type chain in opposite direction
**Spec:** A CLI tool that reads a JSON array of objects, extracts field names from the first object as headers, converts each object to a CSV row, and writes the CSV to stdout. Takes a file path as input.
**Test data:**
```
Input (test.json):
[{"name":"Alice","age":"30"},{"name":"Bob","age":"25"}]

Expected output:
name,age
Alice,30
Bob,25
```
**Cut-outs needed:** json-parser (NEW — parse JSON into rows), csv-serializer (NEW — serialize rows to CSV)
**Chain:** ReadFile → ParseJson → SerializeToCsv → PrintLine
**Edge cases:** EmptyString, MalformedJson, UnicodeEmoji, VeryLongString10K
**Components:** 2 dafny (parser, serializer) + 2 io-shell + 1 orchestrator = 5

### T3 — Filtered CSV Export

**Spike:** Branching data flow (conditional output based on validation)
**Spec:** A CLI tool that reads a CSV file, validates all rows have the same field count, writes valid rows to stdout as JSON. If any row has a mismatched field count, print an error to stderr listing the row number and exit with code 1. Takes a file path as input.
**Test data:**
```
Input (valid.csv):
name,age
Alice,30
Bob,25

Expected: JSON array on stdout, exit 0

Input (invalid.csv):
name,age
Alice,30
Bob,25,extra

Expected: "Error: row 3 has 3 fields, expected 2" on stderr, exit 1
```
**Cut-outs:** csv-parser, row-validator (returns rows + isValid — branching on isValid)
**Chain:** ReadLines → ParseLines → ValidateRows → (if isValid: SerializeToJson → PrintLine | else: PrintError → Exit1)
**Edge cases:** BoundaryAtMin (1 row, trivially valid), BoundaryBelowMin (0 rows), NullInput
**Components:** 3 dafny + 2 io-shell + 1 orchestrator = 6

### T4 — Word Frequency Counter

**Spike:** Aggregation (not just transform — fold/reduce)
**Spec:** A CLI tool that reads a text file, splits into words by whitespace, counts frequency of each word, and prints results as "count word" lines sorted by count descending. Takes a file path as input.
**Test data:**
```
Input (test.txt):
the cat sat on the mat the cat

Expected output:
3 the
2 cat
1 sat
1 on
1 mat
```
**Cut-outs needed:** word-tokenizer (NEW — split text into words), frequency-aggregator (NEW — count occurrences, sort by count)
**Chain:** ReadFile → Tokenize → Aggregate → Sort → PrintLines
**Edge cases:** EmptyString, WhitespaceOnly, UnicodeEmoji, VeryLongString10K, NullInput
**Components:** 2 dafny (tokenizer, aggregator) + 2 io-shell + 1 orchestrator = 5

### T5 — Multi-File CSV Merger

**Spike:** Multiple I/O boundaries (read 2 files, write 1)
**Spec:** A CLI tool that takes two CSV file paths as arguments, reads both, validates they have the same column count, merges them (all rows from file1 then all rows from file2), and writes the merged CSV to stdout. If column counts differ, print error and exit 1.
**Test data:**
```
Input 1 (a.csv):       Input 2 (b.csv):
name,age               name,age
Alice,30               Carol,35

Expected output:
name,age
Alice,30
Carol,35
```
**Cut-outs:** csv-parser, row-validator
**Chain:** ReadLines(file1) → ParseLines → ReadLines(file2) → ParseLines → ValidateRows(merged) → SerializeToCsv → PrintLine
**Edge cases:** EmptyString (one file empty), NullInput (missing file path), BoundaryAtMin (1 file has 1 row)
**Components:** 2 dafny + 3 io-shell (2 file readers + 1 console) + 1 orchestrator = 6

### T6 — Temperature Converter

**Spike:** Pure computation, no file I/O (console in/out only)
**Spec:** A CLI tool that reads a temperature and unit from stdin (format: "32 F" or "0 C"), converts it to the other unit, and prints the result. Supports C↔F and C↔K. Invalid units print an error and exit 1.
**Test data:**
```
Input: "32 F"    → Output: "0 C"
Input: "0 C"     → Output: "32 F"
Input: "100 C"   → Output: "373 K"
Input: "20 X"    → Output: "Error: unknown unit 'X'" exit 1
```
**Cut-outs needed:** temperature-converter (NEW — pure math, no I/O)
**Chain:** ReadLine → ParseInput → Convert → PrintLine
**Edge cases:** FloatNaN, FloatInfinity, FloatNegativeZero, FloatPrecisionLoss, NegativeNumber (below absolute zero), BoundaryAtMin
**Components:** 1 dafny (converter) + 1 io-shell (console) + 1 orchestrator = 3

### T7 — Task Queue with Priority

**Spike:** State machine + scheduler (non-data-pipeline pattern)
**Spec:** A CLI tool that manages a priority queue. Commands: "add <priority> <task>" adds a task, "pop" removes and prints the highest priority task, "list" prints all tasks sorted by priority, "exit" quits. Takes commands from stdin.
**Test data:**
```
Input:
add 3 write report
add 1 check email
add 5 fix bug
pop
list
exit

Expected output:
fix bug
check email
write report
```
**Cut-outs needed:** priority-queue (NEW — enqueue with priority, dequeue highest)
**Chain:** ReadLine → ParseCommand → (add: Enqueue | pop: Dequeue → Print | list: ListAll → PrintLines)
**Edge cases:** NullInput, EmptyString, VeryLargeCollection (100K tasks), DuplicateEntriesInUniqueCollection (same priority)
**Components:** 1 dafny (priority-queue) + 1 io-shell (console) + 1 orchestrator = 3

### T8 — Log File Analyzer

**Spike:** Filter + aggregate on real-world data shape
**Spec:** A CLI tool that reads a log file (format: "TIMESTAMP LEVEL message"), filters by log level (second CLI arg), counts entries per level, and prints the count summary. If the log file is empty, print "No entries" and exit 0.
**Test data:**
```
Input (app.log):
2024-01-01 INFO Starting up
2024-01-01 ERROR Connection failed
2024-01-01 INFO Retrying
2024-01-01 WARN Slow query
2024-01-01 ERROR Timeout

Command: analyzer app.log ERROR

Expected output:
ERROR: 2
```
**Cut-outs needed:** log-parser (NEW — parse log lines into timestamp/level/message), log-filter (NEW — filter by level), log-aggregator (NEW — count by level)
**Chain:** ReadLines → ParseLogLines → FilterByLevel → CountByLevel → PrintLine
**Edge cases:** EmptyString (empty file), NullInput, UnicodeRtl (message contains RTL), VeryLongString10K
**Components:** 3 dafny (parser, filter, aggregator) + 2 io-shell + 1 orchestrator = 6

## Tier 1: Composition Trials (T9-T12)

Each trial combines 2+ spike patterns. 5-10 components. Tests wiring complexity.

### T9 — CSV Validator with Report

**Spike:** Data validation + error report generation (branching + multi-output)
**Spec:** A CLI tool that reads a CSV file, validates each row for: (1) correct field count, (2) no empty fields, (3) numeric fields contain numbers. Generates a validation report on stdout: valid rows count, invalid rows count, and a list of errors with row numbers. Writes the valid rows as JSON to a second output file (second CLI arg).
**Cut-outs:** csv-parser, row-validator, json-serializer
**Chain:** ReadLines → ParseLines → ValidateRows → (valid: SerializeToJson → WriteFile | invalid: collect errors) → PrintReport
**Components:** 3 dafny + 3 io-shell (file reader, file writer, console) + 1 orchestrator = 7

### T10 — Data Transformer Pipeline

**Spike:** Multi-step transform chain (3+ transforms in sequence)
**Spec:** A CLI tool that reads a CSV of products (name,price,category), filters out products under $10, converts prices from USD to EUR (fixed rate 0.92), groups by category, and outputs JSON with categories as keys and product arrays as values.
**Cut-outs:** csv-parser, json-serializer + NEW: price-converter, category-grouper
**Components:** 4 dafny + 2 io-shell + 1 orchestrator = 7

### T11 — Markdown Link Extractor

**Spike:** Regex-like pattern matching (non-trivial parsing)
**Spec:** A CLI tool that reads a Markdown file, extracts all links (format: [text](url)), and outputs them as a JSON array of {text, url} objects. Handles nested brackets and escaped characters.
**Cut-outs needed:** link-extractor (NEW — parse markdown for [text](url) patterns)
**Components:** 1 dafny + 2 io-shell + 1 orchestrator = 4

### T12 — Config File Merger

**Spike:** Multiple input formats, conflict resolution
**Spec:** A CLI tool that reads two INI-style config files, merges them (file2 overrides file1), detects conflicts (same key, different values), and writes the merged config to stdout. Conflicts are listed on stderr.
**Cut-outs needed:** ini-parser (NEW), config-merger (NEW)
**Components:** 2 dafny + 3 io-shell + 1 orchestrator = 6

## Tier 2: North-Star Trials (T13-T16)

Design-only — no cut-outs yet. These define what cut-outs need to be built.

### T13 — Simple REST API Server
CRUD endpoints for a resource. Tests: HTTP stub, JSON parsing, routing, persistence.
**Cut-outs needed:** http-router, json-serializer, crud-repository

### T14 — Key-Value Store with TTL
Get/set/delete with time-based expiry. Tests: time stub, TTL logic, concurrent access.
**Cut-outs needed:** kv-store, ttl-manager, time-aware-eviction

### T15 — Rate Limiter
Token bucket or sliding window. Tests: time stub, counter, threshold logic.
**Cut-outs needed:** token-bucket, sliding-window-counter

### T16 — Event Sourcing System
Append-only event log + state reconstruction. Tests: event store, state reducer, snapshot.
**Cut-outs needed:** event-store, state-reconstructor, snapshot-manager

## Cut-Out Roadmap

| Cut-out | Trial | Status |
|---------|-------|--------|
| csv-parser | T1, T3, T5, T9, T10 | ✅ Exists (3 VC) |
| row-validator | T1, T3, T5, T9 | ✅ Exists (1 VC) — returns (rows, isValid) |
| json-serializer | T1, T9, T10, T13 | ✅ Exists (2 VC) |
| json-parser | T2, T13 | ❌ Needed |
| csv-serializer | T2, T5, T12 | ❌ Needed |
| word-tokenizer | T4 | ❌ Needed |
| frequency-aggregator | T4 | ❌ Needed |
| temperature-converter | T6 | ❌ Needed |
| priority-queue | T7 | ❌ Needed |
| log-parser | T8 | ❌ Needed |
| log-filter | T8 | ❌ Needed |
| log-aggregator | T8 | ❌ Needed |
| price-converter | T10 | ❌ Needed |
| category-grouper | T10 | ❌ Needed |
| link-extractor | T11 | ❌ Needed |
| ini-parser | T12 | ❌ Needed |
| config-merger | T12 | ❌ Needed |

**3 cut-outs exist. 14 needed for Tier 0-1 trials.** Each cut-out: write Dafny → Z3 verify → translate to C# → add to registry. ~15 min each.

## Trial Execution Priority

1. **T1** — exercises existing cut-outs end-to-end. THE proof-of-concept.
2. **T3** — tests branching (if validation fails, error path). Same cut-outs as T1.
3. **T5** — tests multi-file I/O. Same cut-outs as T1.
4. **T2** — needs 2 new cut-outs (json-parser, csv-serializer). First reverse-direction trial.
5. **T6** — needs 1 cut-out (temperature-converter). Tests pure computation, no file I/O.
6. **T7** — needs 1 cut-out (priority-queue). Tests non-pipeline pattern (scheduler).
7. **T4, T8** — need 3 cut-outs each. More complex.
8. **T9-T12** — composition trials. Run after Tier 0 is green.

## What Each Trial Proves

| Trial | Spike | Pass criteria |
|-------|-------|--------------|
| T1 | Linear data flow | Input CSV → correct JSON output |
| T2 | Reverse direction | Input JSON → correct CSV output |
| T3 | Branching | Valid input → JSON, invalid input → error + exit 1 |
| T4 | Aggregation | Text → sorted word counts |
| T5 | Multi-file I/O | Two CSVs → merged CSV |
| T6 | Pure computation | Temperature string → converted temperature |
| T7 | State/scheduler | Commands → correct queue behavior |
| T8 | Filter + aggregate | Log file → filtered count summary |
| T9 | Multi-output | CSV → JSON file + console report |
| T10 | Multi-step transform | CSV → filtered, converted, grouped JSON |
| T11 | Pattern matching | Markdown → JSON of links |
| T12 | Conflict resolution | Two configs → merged config + conflict list |

## Old Trial Mapping

| Old trial | New equivalent | Notes |
|-----------|---------------|-------|
| T1 (CSV-to-JSON) | T1 (same) | Now has test data + cut-out mapping |
| T2 (document processing) | T11 (markdown) | More specific, testable |
| T5 (document pipeline) | T9 (CSV validator) | More specific, branching spike |
| T8 (CI/CD pipeline) | T7 (task queue) | Scheduler spike, simpler |
| T12 (task scheduler) | T7 (task queue) | Same concept, test data |
| T13-T16 (enterprise) | T13-T16 (north-star) | Redesigned with cut-out roadmap |
| T17-T24 (mega) | Removed | Not useful until cut-out catalog is large |