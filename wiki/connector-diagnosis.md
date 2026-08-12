# The Connector Diagnosis — How the Loop Closes

> **"We should be capturing EVERYTHING in order to troubleshoot and document."**

## The Discovery

Aug 12, 2026. Traced the full data flow from architecture prompt → model → artifact → orchestrator → wiring phase. Found the root cause of the Bluejohn problem: **the carapace doesn't carry connector data.** The orchestrator was expected to wire components deterministically from a dependency graph of names alone — no signatures, no connection points, no type mappings. The data was never requested, never produced, never captured. Nothing was lost in transit; nothing existed to capture.

## The Triereme Analogy

The vision: an ancient Roman finds a trireme kit with instructions. Copies it. Builds a fleet. Becomes unstoppable.

The kit has numbered pieces. Each piece says where it connects — tab A into slot B. You don't engineer the connections; the kit specifies them. You build, it floats, you scale.

Posit's 17 patterns are the trireme hulls — proven, complete, Z3-verified. But the kit was missing the connection instructions. The architect said "piece A is near piece B" and the orchestrator was expected to figure out how they bolt together. That's not a kit — that's a pile of planks.

## The Data Flow Trace

### Layer 1: The Prompt

`prompts/architecture/1.0.0.md` asks the model for:
- `publicSurface: ["RunPipeline"]` — **names only, no signatures**
- `dependencies: ["DagResolver", "JobScheduler"]` — **names only, no connection points**
- `internals: "Coordinates DagResolver, JobScheduler..."` — **free-text prose**

The model is NEVER asked for:
- Method signatures (parameter types, return types)
- Which method on A calls which method on B
- What data flows between components
- Type mappings between components
- Input/output port specifications

### Layer 2: The Model Output

Raw DB response for PipelineEngine (T8):
```json
{
  "publicSurface": ["RunPipeline"],
  "dependencies": ["DagResolver", "JobScheduler", "StepExecutor", "ArtifactManager", "NotificationSystem"],
  "internals": "Uses pipeline pattern. Coordinates DagResolver, JobScheduler..."
}
```

The model gave exactly what was asked for — names and prose. No connection data exists to capture.

### Layer 3: The Artifact (ArchitectureContract)

`Component` record captures: `PublicSurface: string[]`, `Dependencies: string[]`, `Internals: string`, `ParametersJson: string?`

No fields for connector specifications exist in the schema.

### Layer 4: The Orchestrator (DesignContext snowball)

`DesignComponent` carries: Name, Responsibility, Tech, PublicSurface, Internals, Dependencies, Classification, PatternName, StubNames, DafnyContractPath, TestCases, IsVerified

**DROPPED vs ArchitectureContract:** `ParametersJson`, `Layer` — both lost in the mapping at PositOrchestrator.cs line 523-533.

### Layer 5: GenerateWiring (what the wiring code receives)

```csharp
GenerateWiring(ArchitectureContract arch, List<(string ModuleName, string CSharpPath)> translatedFiles)
```

It gets:
- `arch.Components[].PublicSurface` → `["RunPipeline"]` (names)
- `arch.Components[].Dependencies` → `["DagResolver"]` (names)
- `translatedFiles` → module name + file path (no parsed signatures)

It does NOT get:
- Actual method signatures from translated C#
- Type information
- Connection specifications (A.method calls B.method with what args?)
- The pattern's actual method names (the pattern provides `HandleRequest`; the architect called it `RunPipeline`)

### The Mismatches

Three problems, all fatal to deterministic wiring:

1. **Method names don't match.** The architect says `RunPipeline`. The pattern provides `HandleRequest`. The orchestrator can't call a method that doesn't exist.

2. **Patterns are sealed capsules.** The `pipeline` pattern is self-contained: Parse → Validate → Transform → Store. It has no hooks for calling DagResolver, JobScheduler, or anything external. The dependency graph says "PipelineEngine calls DagResolver" but the pattern body has no place where that call could go.

3. **No shared types.** Each translated Dafny module gets its own `_module` namespace with its own `Entity`, its own `Result`. They can't pass data to each other because they don't share a type vocabulary.

## The Verdict

**It was never asked for.** The data doesn't exist at any layer. The architect was never told to produce connector specifications. The schema has no fields for them. The prompt asks for names and prose, and that's exactly what came back. Nothing was captured and lost — nothing was ever requested.

## The Fix — Connector Forms on the Carapace

The carapace needs new fields that the architect fills out. These are the tabs and slots — the connection instructions in the trireme kit.

### New Component Fields

1. **`Connections: ConnectionSpec[]`** — for each dependency, which method calls which:
   ```
   {
     fromMethod: "RunPipeline",
     toComponent: "DagResolver",
     toMethod: "Resolve",
     argMapping: "pipeline.jobs → jobs",
     returnType: "DAG"
   }
   ```

2. **`MethodSignatures: MethodSignature[]`** — the actual signature for each public surface method:
   ```
   {
     name: "RunPipeline",
     params: [{ name: "input", type: "string" }],
     returns: { type: "Result<ExecutionResult>" }
   }
   ```

3. **`SharedTypes: string[]`** — which types from which module are shared (via Dafny `include`)

### New Prompt Requirements

The architecture prompt must ask the model to produce:
- Method signatures, not just names
- Connection specifications — which method calls which, with what arguments
- Type mappings between components
- Which pattern method maps to which architect-named method (e.g., `RunPipeline` → `HandleRequest`)

### Registry Enhancement

The pattern registry must expose its actual method signatures so:
- The architect can see what methods the pattern provides
- The orchestrator can map architect names to pattern methods
- The carapace can carry the full connection specification

## The Shrunk Pipeline

The diagnosis revealed something bigger: most of the pipeline phases are unnecessary.

### AI Team (thinking, design)

| Phase | Model | What it does |
|-------|-------|-------------|
| Ideation | AI | Requirements → concept |
| Architecture | AI | Decompose, classify, fill carapace WITH connectors |
| Design Review | AI | Review the design — this IS the design QA |

### Code (deterministic, no model)

| Phase | How | What it does |
|-------|-----|-------------|
| Assemble | Orchestrator (code) | Wire from carapace connector specs |
| Verify | Z3 (code) | Prove contracts consistent |
| Translate | Dafny→C# (code) | With `--test-assumptions Externs` → contracts become runtime checks |
| Test | Bot harness (code) | Push data through CLI, exercise GUI via hotkeys, compare to spec |

### Result

```
Pass  → carve to registry → done
Fail  → retry (several tries, AI team adjusts)
Still fails → human opens it up
```

### Phases Eliminated

- **Pseudocode** — gone. The carapace IS the design.
- **Dafny Implementation (Imp)** — gone. Patterns ship with pre-written Z3-proven bodies. If the pattern fits, no model call needed.
- **C# Implementation (Imp)** — gone. Stub caps are registry-driven, mechanical. Wiring is deterministic from connector specs.
- **QA phase (model)** — gone. Replaced by Design Review (upstream, AI) + bot harness (downstream, deterministic).

The model's job is *thinking* — ideation, architecture, design review. Everything after that is code. The AI designs the ship. The orchestrator builds it from the kit. The bot sails it to see if it floats.

## The QA Automation Insight

The GUI is testable because every control is keyboard-reachable. A bot (script, not LLM) can:
1. Map the GUI — every hotkey → every control
2. Feed test data through each control
3. Capture the output
4. Compare to expected result from the spec
5. Pass/fail — fully automated, no human in the loop

This is deterministic. The bot is a script: push button, get output, compare. No bleary-eyed humans.

## The Closed Loop

```
SEED       → 17 proven atoms in the registry (jigsaw pieces)
ASSEMBLE   → orchestrator wires from carapace connectors (deterministic)
TEST       → bot pushes data through CLI, exercises GUI, captures output (deterministic)
PROVE      → output matches spec → it works (deterministic comparison)
CARVE      → pull the proven assembly back into the registry
```

Both blockers are now addressed:
- **Without connectors**, nothing assembles → **fix: connector forms on the carapace**
- **Without automated QA**, nothing gets proven → **fix: bot harness with hotkey mapping**

With both, the cycle runs end to end. Every cycle makes the registry richer. The registry grows organically:

```
17 atoms → proven 2-component molecules → proven compounds → proven systems
```

Eventually the AI selects from large proven segments, not atoms. The orchestrator snaps them together. The bot exercises the result. The cycle accelerates.

## Runtime Contract Checking (`--test-assumptions Externs`)

Dafny can emit runtime contract checks for `{:extern}` methods in the translated C#. When enabled, the translated C# checks `requires` and `ensures` at runtime. If a stub violates the contract, it throws. Works across all 7 target languages.

**Status: HOPEFUL but NOT YET VERIFIED.** This needs to be tested before relying on it. If it works, the proof follows the code all the way to execution — Z3 proves the logic, runtime checks enforce the contracts on the stubs. Another deterministic check, not a model judgment.

## Next Steps

1. **Add connector fields to the Component record** — `Connections`, `MethodSignatures`, `SharedTypes`
2. **Update the architecture prompt** — ask the model to fill out connector specs
3. **Expose pattern method signatures in the registry** — so the architect can map names to real methods
4. **Rewrite GenerateWiring** — read connector specs from the carapace, generate calls deterministically
5. **Verify `--test-assumptions Externs`** — test it standalone before relying on it
6. **Build the bot harness** — CLI data pushing + GUI hotkey mapping + output comparison
7. **Build the Testmaster** — Blazor desktop with prompt selection sheet (left) + proof dashboard (right)
8. **Run the loop** — assemble T8, test, prove, carve

## See Also

- `wiki/proof-methodology.md` — seed → assemble → test → prove → carve flow
- `wiki/carapace-doctrine.md` — "Computers should know what I MEANT to say"
- `wiki/current-abilities.md` — 17 patterns, 6 stubs, trial scorecard
- `wiki/handoff-2026-08-12.md` — Bluejohn discovery, wiring scaffold, session handoff