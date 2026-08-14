# Plan: Real Software — From Pipeline Artifacts to Compiling, Running Code

> **Goal:** The pipeline completes and produces C# files, but the Docker harness
> can't compile them. Six root-cause bugs stand between "pipeline green" and
> "real software that compiles and runs." This plan fixes them in dependency
> order, verifies each fix against a real Docker build, and doesn't declare
> victory until generated code passes its own test cases.

## Current State (Aug 14, Session 2)

- Pipeline builds clean: 0 errors, 0 warnings
- T1, T2, T5, T8, T12 pass all pipeline phases (Architecture → Dafny → C#)
- C# files are generated (7K-18K chars each, stored in DB)
- Docker harness FAILS on every trial — generated code does not compile
- Two categories of bugs: **structural** (wrong file paths, wrong class names)
  and **semantic** (patterns are generic, don't implement the spec)

## The Six Bugs (in fix order)

### Bug 1: File Placement Mismatch (STRUCTURAL — root cause)

**What:** CSharpImplementationPhase creates files with paths like `CsvParser.cs`
and `CsvParser/Wire.cs`. BotHarness materializes them at `tempDir/{path}`.
But BotHarness creates project directories at `tempDir/{comp.Name}/` with
.csproj files inside. The translated Dafny C# (`CsvParser.cs`) lands at
`tempDir/` root — OUTSIDE the project directory. The .csproj globs don't find it.

**Fix:** Change CSharpImplementationPhase to prefix all file paths with
`{comp.Name}/`. Every file for a component goes into that component's
project directory:
- `{comp.Name}.cs` → `{comp.Name}/{comp.Name}.cs`
- `{comp.Name}Extern.{stub}.cs` → `{comp.Name}/{comp.Name}Extern.{stub}.cs`
- `{comp.Name}.{stubName}.cs` → `{comp.Name}/{comp.Name}.{stubName}.cs`
- `{comp.Name}/Wire.cs` → stays as-is (already correct)

**Also update:** `EnforceCarapace` in PositOrchestrator — its path matching
logic checks `f.Path == "{comp.Name}.cs"` which must change to
`f.Path == "{comp.Name}/{comp.Name}.cs"`.

**Files touched:**
- `src/Posit.Phases/CSharpImplementationPhase.cs` (path generation)
- `src/Posit.Cli/Orchestration/PositOrchestrator.cs` (carapace check)

**Verify:** Run T1, check that BotHarness temp dir has all files inside
project directories. `dotnet build` in Docker finds all .cs files.

---

### Bug 2: Wire.cs Double Inclusion (STRUCTURAL)

**What:** The generated .csproj has both `<Compile Include="*.cs" />` and
`<Compile Include="**\*.cs" />`. Wire.cs matches both globs → CS1503 duplicate
definition. Also, with Bug 1 fixed, all .cs files are in the project root,
so `*.cs` and `**\*.cs` overlap entirely.

**Fix:** Remove the `*.cs` glob. Keep only `<Compile Include="**\*.cs" />`.
This catches all .cs files in the project directory and subdirectories
without duplication.

**Files touched:**
- `src/Posit.Tools/BotHarnessProjects.cs` (GenerateCsproj method)

**Verify:** `dotnet build` in Docker — no CS1503 errors.

---

### Bug 3: __default Class Resolution (STRUCTURAL)

**What:** WiringGenerator.ResolveDafnyClass returns `methods[0].ClassName`
from the scanner. Dafny-translated C# uses `_module.__default` as the
default class. The scanner's `ExtractClassName` finds `__default` (token
after "class"). Wire.cs then generates `__default.Method()` — but the
actual fully-qualified name is `_module.__default`. Without the `_module.`
prefix, the compiler can't find the class.

**Fix:** ResolveDafnyClass should return the fully-qualified name:
`{Namespace}.{ClassName}` (e.g. `_module.__default`). The scanner already
captures the namespace — just use it.

**Files touched:**
- `src/Posit.Phases/WiringGenerator.cs` (ResolveDafnyClass method)

**Verify:** Run T1, check Wire.cs output for `_module.__default.Method()`
instead of `__default.Method()`. Docker build succeeds.

---

### Bug 4: DafnyRuntime DLL Reference (STRUCTURAL — verify only)

**What:** Translated C# references types from DafnyRuntime (e.g. `_IEntry`).
The .csproj references `..\DafnyRuntime\DafnyRuntime.dll` via HintPath.
BotHarness copies the DLL to `tempDir/DafnyRuntime/`. With Bug 1 fixed,
project dirs are at `tempDir/{comp.Name}/`, so `..\DafnyRuntime\` resolves
to `tempDir/DafnyRuntime/` — correct.

**Action:** No code change expected. Verify after Bugs 1-3 are fixed that
Docker build finds DafnyRuntime types. If `_IEntry` still not found, check:
1. Is DafnyRuntime.dll actually copied to the build context?
2. Does the translated C# have `using Dafny;` at the top?
3. Is the DafnyRuntime.dll version compatible with the translated C#?

**Files touched:** Possibly `src/Posit.Tools/BotHarness.cs` (FindDafnyRuntimeDll)
or `src/Posit.Tools/BotHarnessProjects.cs` (add using directives to .csproj).

---

### Bug 5: Program.cs Entry Point Conflict (STRUCTURAL)

**What:** BotHarnessProjects.GenerateProgramCs creates a `Program.Main`
entry point. But if the CLI component also has a `Wire.cs` with its own
`Main` method (from EmitCliWiring), there are two `Main` methods in the
same project. The compiler fails with CS0017 (multiple entry points) or
the Wire.Main is never called.

**Fix:** If the CLI component has a Wire.cs with Main, DON'T generate
a separate Program.cs. Or: generate Program.cs that delegates to Wire.Main.
Simplest: BotHarness should check if Wire.cs exists for the CLI component
and skip Program.cs generation.

**Files touched:**
- `src/Posit.Tools/BotHarness.cs` (Program.cs generation logic)

**Verify:** Docker build — single entry point, Wire.Main is called.

---

### Bug 6: Cotton Candy — Patterns Don't Implement the Spec (SEMANTIC)

**What:** The pipeline.dfy pattern has a hardcoded `HandleRequest` that
parses `"task|create|Buy groceries"` format. A spec asking for "CSV-to-JSON"
gets this generic pipeline — it parses `|`-delimited input, NOT CSV. The
code compiles and Z3 verifies, but it doesn't do what the spec asked.
This is the "cotton candy" problem: looks substantial, dissolves on contact.

**Root cause:** `ComposeSkeleton` concatenates the pattern body verbatim.
The spec says "the architect sets parameters" but there's no parameter
substitution mechanism. The pattern body is a fixed string.

**Fix approach (two options):**

**Option A: Parameter Substitution (incremental)**
- Add `parameters` to ArchitectureContract (per component): a dictionary
  of `{paramName: value}` (e.g. `{"inputDelimiter": ",", "entityType": "CsvRow"}`)
- Extend `ComposeSkeleton` to substitute `{{paramName}}` tokens in the
  pattern body with the architect's values
- Patterns need `{{paramName}}` tokens added at key points (delimiter,
  entity type, field names, validation rules)
- Pros: Stays within the carapace doctrine (pattern is still the skeleton)
- Cons: Limited — can only parameterize what the pattern author anticipated

**Option B: Model-Generated Dafny Bodies (deeper)**
- The architect generates spec-specific Dafny method bodies (not just
  parameter values) that conform to the pattern's method signatures
- The pattern provides the skeleton (method signatures, invariants,
  ensures clauses); the architect fills the bodies
- Z3 still verifies the composed skeleton (bodies must satisfy contracts)
- Pros: Unlimited specialization; patterns become true skeletons
- Cons: Model now participates in Dafny (was purely deterministic)

**Recommended:** Option A first (get T1-T5 working with parameterized
patterns), then Option B for T8+ where specialization is more complex.

**Files touched:**
- `src/Posit.Contracts/Core/Component.cs` (add Parameters dictionary)
- `src/Posit.Tools/PatternRegistryComposer.cs` (substitute tokens)
- `patterns/pipeline.dfy` (add `{{paramName}}` tokens)
- `src/Posit.Cli/Orchestration/PromptBuilder.cs` (ask model for parameters)
- `src/Posit.Phases/ContractScanner.cs` (validate parameter names)

**Verify:** T1 (CSV-to-JSON) generates code that actually parses CSV
with comma delimiter, not pipe delimiter. Docker test cases pass.

---

## Execution Order

```
Step 1: Fix Bug 1 (file paths) + Bug 2 (.csproj globs) + Bug 3 (__default)
        → These are tightly coupled, fix together
        → Build, run T1 through Docker harness
        → Expect: Docker build succeeds, tests run (may fail on cotton candy)

Step 2: Fix Bug 5 (Program.cs conflict) if it surfaces
        → Build, run T1 through Docker harness again

Step 3: Fix Bug 6 (cotton candy) — Option A: parameterize pipeline.dfy
        → Add {{inputDelimiter}}, {{entityType}}, {{minFields}}, {{maxFields}}
        → Update PromptBuilder to ask model for parameter values
        → Build, run T1 — expect tests PASS

Step 4: DAFNYDB — SERIOUS CUT OUTS
        → Major prefab Dafny catalog expansion
        → See "Step 4: DafnyDB" section below

Step 5: Run T2, T5 through Docker harness using new DafnyDB cut-outs
        → Fix cut-outs as needed

Step 6: Run T8, T12 — these need specialist cut-outs (state-machine, scheduler)
        → May need to add more cut-outs to the catalog

Step 7: Run T3, T4, T6, T7, T9, T11 (never run before)
        → Fix any new issues, add cut-outs as gaps appear

Step 8: Run T13-T16 (Tier 1 — multi-system)
        → This is where we've never gotten past
```

---

## Step 4: DafnyDB — Serious Cut Outs

> **Goal:** Evolve the 17 generic patterns into a rich library of pre-cut,
> Z3-verified Dafny modules — "cut outs" — that the architect selects and the
> pipeline composes to build REAL software. The 17 patterns are the abstract
> shapes; the cut-outs are the prefab pieces that fit those shapes.

### What's Wrong With Just Parameterization

Bug 6's Option A (parameter substitution) gets T1 working — the pipeline
pattern gets `{{inputDelimiter}}` set to `,` instead of `|`. But it's still
the same generic pipeline. A CSV parser needs line splitting, field counting,
header detection, type inference — none of which the pipeline pattern has.

Parameterization is a bridge. DafnyDB is the destination.

### What DafnyDB Is

A catalog of domain-specific Dafny modules, each:
1. **Pre-written** — real Dafny code that does real work (not a template)
2. **Z3-verified** — proven correct against its own contracts
3. **Composable** — slots into a pattern's position in the architecture
4. **Translatable** — compiles to real C# via the Dafny compiler

The 17 patterns stay as the abstract shapes (parse → validate → transform
→ store). The cut-outs are concrete implementations of those shapes for
specific domains. The architect selects a cut-out instead of a generic
pattern + parameters.

### Cut-Out Structure

Each cut-out is a `.dfy` file in `patterns/cut-outs/` with:

```dafny
// Cut-out: csv-parser
// Pattern: parser (conforms to parser pattern signatures)
// Domain: data processing
// Params: none (fully self-contained)

include "result.dfy"

method ParseLine(line: string, delimiter: string) returns (fields: seq<string>)
  requires |line| >= 0
  requires |delimiter| == 1
  ensures |fields| >= 1
  // ... real CSV parsing with quote handling
```

Each cut-out has a companion C# stub template (if needed) for I/O portals.

### Phase 4A: Core Data Cut-Outs (T1-T5)

These replace the generic `pipeline` pattern for data processing trials:

| Cut-out | Pattern | Replaces | Trials |
|---------|---------|----------|--------|
| `csv-parser` | parser | generic ParseInput | T1 |
| `json-serializer` | transformer | generic TransformToEntity | T1 |
| `json-parser` | parser | generic ParseInput | T2 |
| `document-classifier` | strategy | generic TransformToEntity | T5 |
| `field-validator` | validator | generic ValidateFields | T1, T2 |
| `line-reader` | iterator | generic parse loop | T1, T5 |

Each cut-out:
1. Write the Dafny source (real parsing, real validation)
2. Z3-verify it (must pass `dafny verify`)
3. Translate to C# (must compile via `dafny translate`)
4. Add to the registry (PatternRegistry loads it)
5. Add to the architect prompt (available for selection)

### Phase 4B: Scheduler & State Cut-Outs (T8-T12)

| Cut-out | Pattern | Replaces | Trials |
|---------|---------|----------|--------|
| `task-scheduler` | scheduler | generic scheduler | T12 |
| `priority-queue` | scheduler | generic dequeue | T12 |
| `cicd-pipeline` | pipeline | generic HandleRequest | T8 |
| `stage-runner` | state-machine | generic transition | T8 |
| `task-validator` | validator | generic ValidateFields | T12 |

### Phase 4C: Commerce & Domain Cut-Outs (T13-T16)

| Cut-out | Pattern | Replaces | Trials |
|---------|---------|----------|--------|
| `cart-manager` | repository | generic store | T13 |
| `order-processor` | pipeline | generic HandleRequest | T13 |
| `payment-validator` | validator | generic validate | T13 |
| `patient-registry` | repository | generic store | T14 |
| `prescription-validator` | validator | generic validate | T14 |
| `message-router` | observer | generic publish | T15 |
| `channel-manager` | repository | generic store | T15 |
| `account-ledger` | repository | generic store | T16 |
| `transaction-processor` | pipeline | generic HandleRequest | T16 |
| `fraud-detector` | strategy | generic transform | T16 |

### Registry Changes

The PatternRegistry needs to know about cut-outs:

1. **Loading** — scan `patterns/cut-outs/*.dfy` alongside `patterns/*.dfy`
2. **Selection** — the architect prompt lists cut-outs as AVAILABLE CUT-OUTS,
   grouped by domain. The architect selects a cut-out by name (e.g.
   `csv-parser`) instead of a generic pattern + parameters.
3. **Composition** — `ComposeSkeleton` uses the cut-out body directly
   (no parameter substitution needed — it's already spec-specific)
4. **ContractScanner** — validates cut-out names against the registry
   (same as pattern names today)

### Contract Changes

The `Component` record gets a new optional field:

```csharp
public string? CutOutName { get; set; }  // e.g. "csv-parser"
```

If `CutOutName` is set, the pipeline uses that cut-out's Dafny source
instead of composing from the generic pattern. `PatternName` is still
required (the cut-out conforms to a pattern's signatures) but the
cut-out IS the body.

### Prompt Changes

The architecture prompt gets a new section:

```
═══ AVAILABLE CUT-OUTS (pre-cut domain modules — prefer these over generic patterns) ═══
  Data Processing:
    - csv-parser: parse CSV lines with quote/comma handling (pattern: parser)
    - json-serializer: convert records to JSON objects (pattern: transformer)
    - json-parser: parse JSON strings to records (pattern: parser)
    ...
  Scheduling:
    - task-scheduler: enqueue/dequeue tasks with priority (pattern: scheduler)
    ...
  Commerce:
    - cart-manager: add/remove items, calculate totals (pattern: repository)
    ...

If a cut-out matches the component's responsibility, USE IT (set cutOutName).
Only fall back to a generic pattern + parameters if no cut-out fits.
```

### DafnyDB Storage

The cut-outs live in `patterns/cut-outs/` as `.dfy` files. This is the
DafnyDB — a flat-file directory of verified Dafny modules. No database
needed yet (the 17 patterns are flat files too). The registry loads them
at startup.

Future: if the catalog grows large (>100 cut-outs), move to the Postgres
database with a `posit_catalog.cut_outs` table. For now, flat files are
simpler and match the existing pattern registry approach.

### Verification

Each cut-out must pass:
1. `dafny verify --allow-warnings` — Z3 proof
2. `dafny translate` — C# translation compiles
3. Bot harness — generated project including the cut-out builds in Docker

A cut-out that fails Z3 or translation does NOT go in the catalog.

### What DafnyDB Enables

After Step 4, when the architect sees "CSV-to-JSON CLI":
- Today: selects `pipeline` pattern, sets `inputDelimiter=,` → generic code
  that parses pipes-but-now-commas, still creates Record entities, not JSON
- After DafnyDB: selects `csv-parser` cut-out + `json-serializer` cut-out
  → real CSV parsing with quote handling, real JSON serialization

The generated code DOES what the spec asked. Not cotton candy — real software.

## What "Done" Looks Like

- T1-T12 all pass Docker harness: build succeeds, test cases pass
- Generated code is REAL: it reads CSV, parses it, outputs JSON
- No cotton candy: test cases verify spec-specific behavior
- DafnyDB has 20+ pre-cut, Z3-verified domain modules
- T13+ is the next frontier, with commerce/scheduling cut-outs ready