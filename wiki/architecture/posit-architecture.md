---
title: "Posit Architecture"
type: architecture
tags: [posit, architecture, pipeline, dafny]
component: core
version: 1.0.0
last_updated: 2026-08-06
---

# Posit Architecture

## Identity

Posit is a spec compiler. The architect posits contracts (requires/ensures). Z3 confirms or denies. The code that survives is proven. Nothing ships unproven.

## Design Principles

1. **Spec before code** — Dafny contracts are written before implementation. The exoskeleton comes first.
2. **Proof is the test** — Z3 verification replaces test generation for verified modules. No test stubs, no edge case patterns, no QA test code.
3. **Pure logic in Dafny, I/O in C#** — The proof boundary is the function signature. Side effects stay outside the proof.
4. **Partial verification is success** — Verified modules skip QA. Unverified modules fall through to traditional QA.
5. **Deterministic judgment** — Z3 doesn't guess. It proves or it doesn't. No model quality lottery on verification.
6. **Small modules** — 200 lines max per Dafny module. Forces decomposition. Tractable proofs.
7. **Wiki-driven** — 2,147 proven Dafny examples in the wiki. The model learns by imitation, not by guessing.

## Pipeline

```
Ideation → Architecture → API Definition → Pseudocode → Design Review
  → Dafny Contracts (architect writes .dfy skeletons with requires/ensures, {:extern} portals for I/O)
  → Dafny Implementation (Imp fills Dafny bodies, Z3 verifies, translate cs — verified logic + extern holes)
  → C# Implementation (Imp writes C# shells that plug into extern portals, calls translated Dafny)
  → QA (compile translated C#, test unverified modules only)
  → Deployment → Observability → Documentation
```

### Phase Detail

| Phase | Model | Input | Output | Notes |
|-------|-------|-------|--------|-------|
| Ideation | deepseek-v4-pro:cloud | User request | Requirements doc | Module registry pre-selection |
| Architecture | deepseek-v4-pro:cloud | Requirements | Architecture contract with module classification (dafny/io-shell) + Dafny contracts | Architect writes .dfy skeletons |
| API Definition | deterministic | Architecture | API spec | No model call |
| Pseudocode | deterministic | Architecture + API | Module specs | No model call |
| Design Review | kimi-2.7-code:cloud | Accumulated design | Approve/reject | Independent review gate |
| Dafny Contracts | deterministic + Z3 | Architecture | Verified .dfy skeleton files on disk | Z3 verifies the spec is sound (bodyless methods, {:axiom} attribute) |
| Dafny Implementation | deepseek-v4-pro:cloud | .dfy skeletons + wiki | Dafny bodies filled in, Z3 verified, translated C# with extern holes | Pass 1: fill bodies, Z3 verify, `dafny translate cs` — produces partial classes |
| C# Implementation | glm-5.2:cloud | Translated C# + extern holes + type shells | C# shells that plug into extern portals | Pass 2: implement {:extern} methods in partial classes, wire I/O to translated Dafny |
| QA | glm-5.2:cloud | Translated C# + unverified modules | Build + test results | Verified modules: compile only. Unverified: full test generation. |
| Deployment | deterministic | Implementation | Deployment manifest | No model call |
| Observability | deterministic | Deployment | Observability config | No model call |
| Documentation | deepseek-v4-pro:cloud | All artifacts | Docs | |

### Model Calls: 5 (Ideation, Architecture, Dafny Implementation, C# Implementation, QA)
- Design Review, Documentation: 2 more if counted
- 6 deterministic phases: API Definition, Pseudocode, Dafny Contracts, Deployment, Observability, + Z3 verification

## Module Classification

The architect classifies each module as one of:

### `dafny` — Pure Logic (Verified)
- Parsing, validation, transformation, generation, business rules
- No I/O, no side effects, no external state
- Gets a `.dfy` skeleton with contracts
- Imp writes Dafny bodies
- Z3 verifies
- `dafny translate cs` produces C#
- QA compiles only (no tests)

### `io-shell` — I/O Wrapper (Unverified)
- File reading, console output, database connection, HTTP client
- Side effects, external state, framework calls
- Gets a C# type shell (existing Shepherd pattern)
- Imp writes C# bodies
- Build judge checks compilation
- QA generates tests (existing behavior)

### `mixed` — Partial (Split into dafny + io-shell)
- Config loader (file I/O + parsing)
- Split into: `ConfigParser` (dafny — pure parsing) + `ConfigFileReader` (io-shell — file I/O)
- Each piece goes through its respective pipeline

**Mixed module splitting:** The architect outputs two separate `Component` records — one `dafny`, one `io-shell` — with a dependency from the io-shell to the dafny. The pipeline does not split modules. The architect is responsible for decomposition at design time.

## Dafny Contract Format

```dafny
// Module: CsvParser
// Responsibility: Parse CSV lines into typed rows

module CsvParser {

  datatype DataType = Integer | Float | Date | Boolean | Varchar

  class CsvParser {
    var delimiter: char
    var quote: char

    predicate Valid() reads this
      { delimiter != '\000' }

    constructor(delimiter: char, quote: char)
      ensures Valid()

    method ParseLine(line: string) returns (fields: seq<string>)
      requires Valid()
      requires |line| > 0
      ensures |fields| >= 1
      ensures forall i :: 0 <= i < |fields| ==> fields[i] != null
    // Bodyless — Imp fills in the body during Implementation
  }
}
```

## Extern Portal Pattern

The architect walks down the Dafny sidewalk — pure logic, requires/ensures, Z3-verified. When it hits something that can't be proven (file read, stream, database call), it doesn't stop. It puts an `{:extern}` stub — the portal.

```dafny
// In the .dfy skeleton (Architecture phase):
class FileProcessor {
  // The portal — C# implements this, Z3 assumes the contract
  method {:extern} ReadFile(path: string) returns (content: string)
    requires |path| > 0
    ensures |content| >= 0

  // Pure logic — verified by Z3, calls through the portal
  method CountLines(content: string) returns (count: int)
    requires |content| >= 0
    ensures count >= 0
  // Bodyless in skeleton — Imp fills in during Dafny Implementation
}
```

Z3 verifies everything around the portal. The extern's contract is taken as an axiom — "whatever comes out of that door satisfies `|content| >= 0`." The proof holds. The portal is the membrane.

`dafny translate cs` produces a `partial class` with the verified logic baked in and the extern method as a hole:

```csharp
// Generated by dafny translate cs (Dafny Implementation phase):
public partial class FileProcessor {
  public BigInteger CountLines(Dafny.ISequence<Dafny.Rune> content) {
    // Verified logic — proven correct by Z3
  }
  // ReadFile is not here — it's the extern hole
}
```

Pass 2 (C# Implementation) plugs into the portal:

```csharp
// Written by Imp in C# Implementation phase:
public partial class FileProcessor {
  public string ReadFile(string path) => File.ReadAllText(path);
}
```

The C# plugs into the portal. The translated Dafny calls it at runtime. The logic inside the exoskeleton is proven. The I/O passes through the portal and back out.

### Verified by testing (Aug 6, 2026)

- **Bodyless methods with `{:axiom}`:** `dafny verify` → 4 verified, 0 errors ✅
- **Bodyless methods translate:** fails (expected — bodyless methods can't compile). Translation happens after Imp fills bodies.
- **Extern stubs:** `dafny verify` → 2 verified, 0 errors ✅. Z3 verifies logic around the extern.
- **Extern translate:** `dafny translate cs` → exit 0, produces `partial class` with verified logic + extern hole ✅

## I/O Shell Pattern

```csharp
// io-shell module — C#, not verified
public class CsvFileReader
{
    public string ReadAllText(string path)
        => File.ReadAllText(path);
}

// dafny module — translated to C# by dafny translate cs
// The translated code calls into the proof-verified logic
// The I/O shell calls the translated code
public class CsvProgram
{
    public void Run(string filePath)
    {
        var reader = new CsvFileReader();       // io-shell
        var content = reader.ReadAllText(filePath); // io-shell
        var parser = new CsvParser(',', '"');    // translated Dafny
        var rows = parser.ParseLine(content);    // proven correct
    }
}
```

## Limits and Boundaries

### Per-Module Dafny Source
- Max 200 lines
- Max 10 methods/functions
- Max 5 classes/datatypes
- Max 3 requires/ensures per method

### Prompt Budgets
| Phase | System Prompt Cap | Wiki Cap | Other |
|-------|-------------------|----------|-------|
| Architecture | 16K | 8K | Dafny syntax ref 2K |
| Dafny Contracts | N/A — deterministic | N/A | Z3 only, no model call |
| Implementation (Dafny — Pass 1) | 16K | 4K | Contract file on disk |
| Implementation (C# — Pass 2) | 32K | 8K | Translated Dafny + extern holes |
| QA | 32K | 4K | Edge cases 4K |

### Z3 Verification
- `--verification-time-limit 30` (default)
- `--standard-libraries` (enable Std imports)
- `--resource-limit` configurable

### Output Tokens
- Dafny generation: 16K max
- C# generation: 64K max (existing)
- QA generation: 64K max (existing)

## Agent Model Assignments

| Task | Model | Rationale |
|------|-------|-----------|
| Ideation | deepseek-v4-pro:cloud | Better reasoning for decomposition |
| Architecture (Dafny contracts) | deepseek-v4-pro:cloud | Formal spec writing |
| Design Review | kimi-2.7-code:cloud | Independent review — separation from architect |
| Implementation (Dafny bodies — Pass 1) | deepseek-v4-pro:cloud | Proven 2/5, understands architect's contract intent |
| Implementation (C# shells — Pass 2) | glm-5.2:cloud | Plugs into extern portals, wires I/O to translated Dafny |
| QA (test generation) | glm-5.2:cloud | Architect knows module intent, well-positioned for unprovable module tests |
| Documentation | deepseek-v4-pro:cloud | Better prose |
| File ops, wiki search | local ollama | Fast, no reasoning |

## Project Structure

```
Posit/
  src/
    Posit.Contracts/        # Artifacts, enums, interfaces
    Posit.Core/              # FSM, state machine, session
    Posit.Data/              # DB, repositories, migrations
    Posit.AI/                 # Model gateway, prompt registry, context manager
    Posit.Phases/             # All phase implementations
      DafnyContractsPhase.cs
      DafnyImplementationPhase.cs  # Pass 1: fill Dafny bodies, Z3 verify, translate cs
      CSharpImplementationPhase.cs # Pass 2: plug into extern portals, wire I/O
      QaPhase.cs             # Modified for verified module skip
      ...
    Posit.Tools/             # Build engine, staging, Z3 runner
    Posit.Cli/                # CLI entry point
    Posit.Web/                # Dashboard (Blazor)
  tests/
  prompts/
    ideation/
    architecture/            # Dafny contract writing prompt
    dafny/                   # Dafny body writing prompt
    implementation/          # C# I/O shell prompt
    qa/
    ...
  wiki/
    plans/
    patterns/
    architecture/
    contracts/               # Dafny contract templates per module type
  migrations/
```

## Skeleton Verification Semantics

Dafny distinguishes between **bodyless methods** and **methods with empty bodies**:

- **Bodyless** (`method Foo() ensures ... ` with no `{ }`): Z3 treats as abstract specification. Checks contracts are well-formed and self-consistent. No proof obligation for the body. This is what skeletons use.
- **Empty body** (`method Foo() ensures ... { }`): Z3 tries to prove postconditions from default return values. Almost always fails. Do NOT use.

All `.dfy` skeletons use bodyless declarations. Predicates are the exception — `predicate Valid() reads this { ... }` has a body because it's a definition, not a proof obligation.

## Cross-File Dafny Dependencies

Each module gets its own `.dfy` file. If a module needs types defined in another module, it uses:

```dafny
include "OtherModule.dfy"
```

Z3 verifies each file independently. `dafny translate cs` handles multi-file projects — it produces a single C# output per compilation. The DafnyContractsPhase writes all `.dfy` files to a staging directory and verifies them as a batch with `dafny verify *.dfy`.

## Correction Signal — Dafny Contracts Loopback

When a skeleton fails Z3 verification, the correction signal loops **back to the Architecture phase**, not within Dafny Contracts. The FSM rolls back to Architecture with the Z3 error attached. The architect re-runs with the correction signal and produces a fixed `.dfy` skeleton.

- **Max loopbacks:** 2 (same as Shepherd's Implementation→Architecture loopback cap)
- **After exhausting loopbacks:** the module is downgraded to `io-shell` and proceeds through the C# pipeline with QA test generation
- **Partial success:** verified skeletons are preserved; only failed modules loop back

## FSM Design

Posit inherits Shepherd's FSM states and escalation chain:

```
Idle → Planning → Active → Validating → Retry → CheckpointRollback → Recovery → ReviewGate → Paused → Completed → Aborted
```

**Escalation chain:** retry in-phase → rollback to checkpoint → recovery with backoff → review gate (human) → abort to Idle.

**New transitions for Dafny-first:**

| Event | From | To | Notes |
|-------|------|----|-------|
| `dafny.skeleton_failed` | Validating | CheckpointRollback | Rolls back to Architecture with Z3 error |
| `dafny.skeleton_verified` | Validating | Planning | Advances to Implementation |
| `dafny.body_failed` | Validating | Retry | Retries within Implementation (Imp fixes body) |
| `dafny.body_verified` | Validating | Planning | Advances to QA |
| `module.downgraded` | Active | Active | Module reclassified io-shell, pipeline continues |
| `dafny.translated_cs` | Validating | Planning | Translated C# dropped into project, advances |

**Loopback counter:** `LoopbackCount` on `SessionState` tracks Architecture→Dafny Contracts round-trips. Capped at 2.

## Multi-Target Stance

C# now. Multi-target (Rust, Go, Java, JS, Python) is a future aspiration, not a current design goal. The plan, project structure, and all phases target C# only. `dafny translate cs` is the only translation path. When multi-target becomes a real requirement, a target-language abstraction will be added — but not before.

## Determinism Stance

Determinism is a target-specific concern, not a core property of Posit. The `--enforce-determinism` flag is relevant only if/when a Rust target is added. For C# output, standard Dafny translation is sufficient. Proofs are deterministic (Z3); translated code follows the target language's semantics.

## What's Different from Shepherd

| Aspect | Shepherd | Posit |
|--------|----------|-------|
| Spec language | C# type signatures | Dafny contracts (requires/ensures) |
| Implementation language | C# | Dafny (for logic) + C# (for I/O) |
| Verification | Build judge (compilation) | Z3 (formal proof) + build judge |
| QA | Generate tests for all modules | Compile translated C#, test unverified only |
| Test stubs | All modules | Unverified modules only |
| Edge cases | All modules | Unverified modules only |
| Correction signal | Compiler errors | Z3 proof failures (exact clause + counterexample) |
| Wiki | C# patterns + edge cases | C# patterns + 2,147 Dafny examples |
| Module classification | None | dafny / io-shell / mixed |

## Implementation Steps

### Step 1: Project Structure
- Solution, projects, build system
- Reuse Shepherd's infrastructure (FSM, state machine, DB, migrations)
- New: DafnyContractsPhase, modified ImplementationPhase, modified QaPhase

### Step 2: Dafny Contracts Phase
- Architect writes .dfy skeletons with contracts
- Z3 verifies skeleton (contracts without bodies)
- .dfy files written to disk

### Step 3: Dafny-first Implementation
- Imp fills in Dafny bodies
- Z3 verifies complete program
- `dafny translate cs` produces C#
- C# I/O shells implemented normally

### Step 4: Modified QA
- Verified modules: compile translated C# only
- Unverified modules: full test generation (existing behavior)

### Step 5: Limits and Budgets
- 200-line cap, 10 method cap, 5 class cap per Dafny module
- Prompt budgets per phase

### Step 6: Trial
- End-to-end Dafny-first pipeline run
- Compare: modules verified, verification rate, QA prompt size, total pipeline time