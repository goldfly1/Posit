---
title: Posit Architecture Evolution Map
type: design
tags: [architecture, evolution, registry, vector-db, vision]
component: posit
version: 0.1
last_updated: 2026-08-08
---

# Posit Architecture Evolution Map

## The 7 Approaches

Ordered from least to most power. The practical path is 3 → 4 → 6, with 7 as the north star.

---

## Approach 1: Line-by-Line from Registry

**Concept:** Each line of code is a pre-cut snippet. Assembly is sentence construction from word cards.

**Reality:** Dead end. Dafny lines don't stand alone. A `requires` without a method is meaningless. A `while` without its invariant is unprovable. There's no unit smaller than a complete method body that Z3 can verify.

**Verdict:** Rejected. Not a real approach — Dafny's verification granularity is the method, not the line.

---

## Approach 2: Outlined Blanks with Built-In Pieces

**Concept:** The skeleton has method signatures, contracts, return types, maybe a loop frame, but the body is blank. Various return shapes are pre-built (return Result.Success, return seq<string>, etc.).

**What we have now.** The 9 patterns + 6 stubs are bodyless. Structure is fixed, logic is invented each time.

### Architecture changes

| Component | Current (Approach 2) |
|-----------|---------------------|
| Architect | Selects pattern + stubs from registry. Customizes names/types. Writes test cases. |
| Dafny Contracts | Verifies the bodyless skeleton (contracts are sound). |
| Imp (Dafny) | Reads skeleton from disk, fills in method bodies from scratch, Z3 verifies, retries on failure. |
| Imp (C#) | Reads translated C#, writes portal implementations from scratch. |
| QA | Tests against architect's test cases. |
| Registry | 9 bodyless patterns + 6 stub files. Flat list. |
| Vector DB | Not used. |

**Problems:**
- Imp writes the same iteration loop 10,000 times across 10,000 projects
- Each write is a chance for error, each error triggers Z3 retry, each retry burns tokens
- The pattern provides shape but no logic — Imp still creates from scratch
- No accumulation of proven solutions

**Verdict:** Current state. Works but doesn't scale. The model does too much.

---

## Approach 3: Pre-Written Bodies (3-5 Functions per Pattern)

**Concept:** The pattern ships with working, Z3-proven method bodies. A parser pattern has `Parse`, `SplitOnDelimiter`, `BuildResult`, `HandleQuotes`, `ValidateField` — all verified. The architect customizes parameters (delimiter, types, error conditions). Imp's job shrinks to parameterization or is empty.

**This is where the light gets bright.**

### Architecture changes

| Component | Approach 2 → Approach 3 | What changes |
|-----------|------------------------|--------------|
| Architect | Selects pattern + stubs + **sets parameters** | Adds parameter fields to Component. Pattern selection becomes configuration, not just shape selection. |
| Dafny Contracts | Verifies the **parameterized** body, not just the skeleton | Now verifies complete programs, not bodyless stubs. If pattern was pre-proven, this should pass on first try for most customizations. |
| Imp (Dafny) | **Empty or near-empty.** Customizes parameters. May need to adjust a line or two for novel cases. | Job shrinks from "write the algorithm" to "adjust the delimiter constant." Z3 re-verifies. Most runs: no retry needed. |
| Imp (C#) | Selects **stub caps** from `patterns/csharp-stubs/` | No model generation. `ReadFile` → `File.ReadAllText`. Snap-on. Already started in the other session's commits. |
| QA | Tests against architect's test cases. **Pattern body already proven by Z3** — QA compiles only. | If the pattern body is Z3-proven and the customization is a parameter change that Z3 re-verified, QA's job is compilation check. No test generation for verified modules. |
| Registry | 9 patterns **with bodies** + 6 stub files + C# stub caps | Patterns are complete, verified Dafny programs with parameters. Not bodyless. |
| Vector DB | Not yet. | Still flat list, but patterns are complete programs now. |

### What the architect prompt changes to

Instead of: "Select a pattern and write .dfy source"
It becomes: "Select a pattern and set its parameters: delimiter=\",\", quoteChar=\"\"\", hasHeader=true"

The architect's output for a dafny module:
```json
{
  "patternName": "parser",
  "parameters": { "delimiter": ",", "quoteChar": "\"", "hasHeader": true },
  "stubNames": ["file-io"]
}
```

### What the registry looks like

```
patterns/
  parser.dfy              # has body, has parameters as consts/globals
  validator.dfy           # has body
  repository.dfy          # has body
  ...
  stubs/
    file-io.dfy           # {:extern} portals (unchanged)
  csharp-stubs/
    file-io.cs.template   # snap-on C# cap for ReadFile, WriteFile, etc.
```

### What Z3 verifies

The complete program: pattern body + parameter values + stub contracts. If the pattern was pre-proven with generic parameters, the customized version should verify immediately for most parameter sets. Novel parameters (e.g., a multi-char delimiter) might trigger retry.

### Effort to build

- Write 3-5 method bodies per pattern × 9 patterns = 27-45 methods
- Verify each with Z3
- Add parameter fields to Component record
- Update architect prompt for parameterization
- Write C# stub caps for 6 stub files

**One session with deepseek-v4-flash:cloud generating bodies, Z3 verifying.**

---

## Approach 4: Parameterized Templates with Combinatorial Generation

**Concept:** The pattern has parameters as variables. The system generates combinations: delimiter=comma+quote=double+header=true, delimiter=tab+quote=none+header=false, etc. Each combination is Z3-verified. The ones that pass go into the vector DB.

**This is the MASSIVE vision — not one parser, but 50 verified parser variants.**

### Architecture changes

| Component | Approach 3 → Approach 4 | What changes |
|-----------|------------------------|--------------|
| Architect | Selects from **vector DB** instead of flat registry | Search replaces selection. "I need a CSV parser with quoted fields and type validation" → vector search returns the closest proven variant. |
| Dafny Contracts | **Pre-verified.** Every variant in the DB already passed Z3. | Verification happens at generation time, not pipeline time. The pipeline just looks up the proven variant. |
| Imp (Dafny) | **Empty.** The variant is already complete and proven. | No model call. The variant is pulled from the DB with its parameters already set and Z3 verification already recorded. |
| Imp (C#) | **Empty or snap-on.** C# caps are pre-matched to stubs in the DB. | The DB stores the Dafny variant + the matching C# cap as a unit. |
| QA | **Compile only.** The variant is Z3-proven. | No test generation for any variant from the DB. |
| Registry | **Vector DB of proven variants.** Hundreds/thousands of parameterized, verified programs. | The flat registry becomes a search index. Each entry is: pattern + parameter values + Z3 proof + C# cap + metadata. |
| Vector DB | **Core component.** Stores proven variants with embeddings for semantic search. | Entries are searchable by problem description, parameter values, pattern type, verification status. |

### What the generation loop looks like

```
for each pattern:
  for each parameter combination:
    1. Instantiate the pattern with these parameters
    2. Run Z3 verify
    3. If verified:
       a. Translate to C# (--include-runtime)
       b. Match C# stub caps
       c. Embed the variant (Dafny source + C# output + parameters + metadata)
       d. Insert into vector DB
    4. If failed: log failure mode, skip
```

### What the architect does

```json
{
  "patternName": "parser",
  "parameters": { "delimiter": ",", "quoteChar": "\"", "hasHeader": true },
  "stubNames": ["file-io"]
}
```

The pipeline searches the vector DB for the closest match. If found → use it directly. If not found → generate from the base pattern, verify, add to DB.

### What the vector DB looks like

```
posit_variants table:
  id: ULID
  pattern: "parser"
  parameters: {"delimiter": ",", "quoteChar": "\"", "hasHeader": true}
  dafny_source: text
  csharp_source: text
  z3_verified: true
  z3_output: text
  embedding: vector(768)
  metadata: {"stubNames": ["file-io"], "testCases": [...]}
```

### Effort to build

- Approach 3 must exist first (bodies in patterns)
- Parameter extraction from patterns (identify what varies)
- Combinatorial generation script
- Vector DB schema + embedding pipeline
- Search integration in Architecture phase

**The generation engine is where deepseek-v4-flash:cloud earns its keep.** Generate thousands of variants, Z3 filters them, the DB accumulates the survivors.

---

## Approach 5: Composition Graph

**Concept:** Patterns compose. Parser + Validator + Repository = "CSV ingest pipeline." The system pre-generates common compositions, verifies the assembly holds (output type of parser feeds input type of validator), and indexes them.

### Architecture changes

| Component | Approach 4 → Approach 5 | What changes |
|-----------|------------------------|--------------|
| Architect | Selects a **composition** instead of individual patterns | "I need a CSV ingest pipeline" → vector search returns a pre-verified composition of parser + validator + repository + file-io + database-io. |
| Dafny Contracts | Verifies the **composition assembly** — type compatibility at boundaries | Not just individual modules, but that parser's output type feeds validator's input type. |
| Imp | Empty. Composition is pre-assembled and pre-proven. | |
| Registry | **Graph of compatible assemblies.** Not a flat list of patterns. | Nodes are patterns. Edges are type-compatible compositions. Pre-verified subgraphs are indexed. |
| Vector DB | Stores proven compositions with their module graph. | Searchable by composition shape: "parser → validator → repository with file-io and database-io." |

### What the composition graph looks like

```
parser ──(seq<string>)──→ validator ──(ValidationResult)──→ repository
   │                                                        │
   └──(string)──→ file-io                           database-io
```

Each edge is a type compatibility constraint. Pre-verified subgraphs are indexed in the vector DB. The architect selects a subgraph; the pipeline instantiates it.

### Effort to build

- Approach 4 must exist first (variant DB)
- Type compatibility matrix between pattern outputs and inputs
- Composition verification (Z3 proves the assembly, not just individual modules)
- Composition indexing in vector DB

---

## Approach 6: Generative + Classification Loop

**Concept:** The system generates code variants, Z3 verifies, QA tests against architect's test cases, and results feed back. What passes goes into the vector DB. What fails is classified by failure mode. Over time, the DB accumulates proven solutions tagged by problem shape.

**The catalog grows itself.**

### Architecture changes

| Component | Approach 5 → Approach 6 | What changes |
|-----------|------------------------|--------------|
| Architect | Describes the problem. **Does not select patterns.** | "I need something that reads CSV, validates types, and inserts into Postgres." The system finds or builds the solution. |
| Dafny Contracts | **Pre-verified or generated + verified on the fly.** | If a proven variant exists in the DB, skip. If not, generate from pattern + parameters, verify, add to DB. |
| Imp | **Generation engine.** Uses deepseek-v4-flash:cloud to generate variant bodies. Z3 filters. | The model generates many candidates. Z3 is the filter. The DB is the accumulation. |
| QA | **Classification.** Tests against architect's test cases. Pass → DB entry. Fail → failure mode logged. | QA isn't just testing — it's classifying what works and what doesn't, feeding back into the generation loop. |
| Registry | **Living catalog.** Grows with every successful generation. | The registry is no longer a static directory — it's a growing DB of proven solutions. |
| Vector DB | **Core.** Every proven variant, every composition, every successful generation is indexed. | Search by problem description, not by pattern name. The system finds the closest proven solution. |

### What the loop looks like

```
1. Architect describes problem + test cases
2. System searches vector DB for closest proven match
3. If match found:
   a. Instantiate with architect's parameters
   b. Z3 re-verify (should pass — was pre-proven)
   c. Ship
4. If no match:
   a. Generate variants from base patterns (deepseek-v4-flash:cloud)
   b. Z3 verify each
   c. QA test each that passes Z3
   d. Successful variants → insert into vector DB
   e. Ship the best one
   f. Failed variants → classify failure mode, log for future avoidance
5. The DB grows. Next time the same problem is described, step 2 finds it.
```

### Effort to build

- Approaches 3-5 must exist first
- Generation engine (deepseek-v4-flash:cloud + Z3 filter)
- Classification system (failure mode taxonomy)
- Feedback loop (failed generations inform future generation)
- DB growth management (dedup, versioning, pruning)

---

## Approach 7: Full Synthesis from Specification

**Concept:** The architect writes only the requirements. The system searches the vector DB for the closest proven match, parameterizes it, verifies, and ships. If no match, generates from pattern + stubs, verifies, tests, and adds to DB.

**The north star. The dream.**

### Architecture changes

| Component | Approach 6 → Approach 7 | What changes |
|-----------|------------------------|--------------|
| Architect | **Writes requirements only.** Natural language. No pattern selection, no parameter setting. | "Build a CSV to SQL CLI tool with these modules: ..." The system does the rest. |
| Everything else | Automated. | The system decomposes, selects, generates, verifies, assembles, tests, and ships. |

### What this requires

- Approaches 3-6 fully operational
- Vector DB with massive coverage (thousands of proven variants + compositions)
- Semantic search accurate enough to find the right solution from a natural language description
- Generation engine reliable enough to fill gaps
- Self-healing: when generation fails, the system learns and improves

### Why it's the north star

The architect stops being a technical role. It becomes a requirements role. "I need X" → X arrives, proven. The system is the architect, the implementer, and the verifier. The human describes; the system delivers.

This is where the light is brightest. But you can't get here without building 3 → 4 → 6 first.

---

## The Practical Path

```
NOW (Approach 2)
  ↓
3: Pre-write pattern bodies (one session with flash model + Z3)
  ↓
4: Parameterize + generate variants + vector DB
  ↓
6: Generative loop — DB grows itself
  ↓
7: Full synthesis (north star)
```

## What Doesn't Change

Across all approaches:
- **Z3 is the judge.** Always. No matter who generates the code, Z3 verifies it.
- **The skeleton is the carapace.** The file on disk is the authority.
- **The architect writes test cases.** These are the acceptance criteria. Always.
- **Pass 2 plugs oar holes.** C# stub caps snap onto `{:extern}` portals.
- **Nothing ships unproven.**

---

## Future Build-Out: Layered Architectural Decomposition

**Concept:** For large projects (50+ modules), the architect doesn't design everything at once. It works in two passes:

1. **Wide-area map:** The architect maps data pools and edges — which pools exist, which edges connect them, which direction data flows. This is the high-level design. Each edge is a type boundary (what crosses between locales). Each pool is a data cluster. The wide-area map is a contract, not an implementation.

2. **Locale design:** Each locale (a cluster of modules around a data pool) is designed independently. The locale's skeleton only needs the edges — the `include` directives and the types that cross the boundary. The internal modules are the locale's own problem.

```
Wide-area map (first pass):
  CSV input → [Parse locale] → [Validate locale] → validated data → [Transform locale] → SQL → [Write locale] → DB

Each arrow = an edge (type boundary).
Each box = a locale (designed independently).
```

**Why this works:** The skeleton is the carapace. The edges are tattooed on the wide-area map as `include` directives and type declarations. Locale A doesn't need locale B's internals — just its public surface. The wide-area map carries the public surfaces. Each locale design fills in the internal modules with its own pattern selection, skeleton composition, and Z3 verification.

**How it fits the pipeline:**
- Architecture phase (pass 1): produce wide-area contract — pools, edges, types
- Architecture phase (pass 2): for each locale, select patterns + compose skeletons + Z3 verify
- DesignContext snowball: carries the wide-area map + each locale's modules
- The registry: searches can be scoped to a locale — "find a parser for the CSV input locale"

**When this matters:** Projects with 50+ modules where the architect can't hold the whole design in one context. The wide-area map is the decomposition that makes it tractable. Each locale is a small problem. The edges are the contracts between them.

**Not needed now.** Current projects (CSV-to-SQL, config validator) are 2-5 modules. Layered decomposition is for when the project is too big for one architecture pass. Add to the pipeline when the first 50-module project arrives.