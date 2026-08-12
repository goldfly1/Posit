# Current Abilities Assessment — Aug 12, 2026

## What Posit Can Build Today

### Proven Patterns (17, all Z3-verified)

The registry holds 17 patterns, each named by the function it performs:

| Pattern | Function | Proven |
|---------|----------|--------|
| pipeline | parse → validate → transform → store → result | ✅ Z3 |
| repository | store entities with unique IDs, CRUD | ✅ Z3 |
| state-machine | finite states with guarded transitions | ✅ Z3 |
| aggregator | fold/reduce collection to single value | ✅ Z3 |
| builder | accumulate parts, assemble with invariants | ✅ Z3 |
| iterator | traverse collection with position tracking | ✅ Z3 |
| result | Success/Failure datatype for error handling | ✅ Z3 |
| observer | publish/subscribe event bus | ✅ Z3 |
| strategy | interchangeable algorithms behind one interface | ✅ Z3 |
| graph | nodes, edges, traversal, pathfinding | ✅ Z3 |
| cache | store + lookup + invalidate | ✅ Z3 |
| scheduler | enqueue, dequeue, prioritize, defer | ✅ Z3 |
| reducer | action → state mutation with undo | ✅ Z3 |
| adapter | wraps external API in clean interface | ✅ Z3 |
| filter | predicate-based collection filtering | ✅ Z3 |
| parser | splits delimited input into fields/records | ✅ Z3 |
| validator | checks input against rules, collects errors | ✅ Z3 |
| transformer | applies operation to each element of collection | ✅ Z3 |

### I/O Stub Caps (6 Dafny stubs, 16 C# templates, all verified)

| Stub | Function | Dafny | C# |
|------|----------|-------|-----|
| console-io | print, read, clear screen | ✅ Z3 | ✅ Compiles |
| file-io | read, write, append files | ✅ Z3 | ✅ Compiles |
| network-io | HTTP GET/POST/PUT/DELETE | ✅ Z3 | ✅ Compiles |
| database-io | query, execute, transaction | ✅ Z3 | ✅ Compiles |
| stream-io | open, read chunks, close | ✅ Z3 | ✅ Compiles |
| time-random | timestamps, sleep, random | ✅ Z3 | ✅ Compiles |

Domain-specific C# templates: banking, chat, cicd, ecommerce, healthcare, monitoring, scheduling, search, workflow (9 templates, all compile-clean).

### What This Covers

Business software: CRUD apps, schedulers, pipelines, healthcare records, e-commerce, CI/CD, document processing, task scheduling, marketplace systems. The 17 patterns cover the logic; the stub caps cover the I/O.

### What This Does NOT Cover (Yet)

- GPU rendering / graphics
- Real-time frame loops (60fps game loop)
- Audio input/output
- Gamepad/mouse input beyond console
- Desktop video games (needs game-loop pattern + graphics/audio/input stubs)

These are future expansion targets, not current capabilities. The architecture supports them — new patterns and stubs would be added to the registry the same way the existing 17 were. But they don't exist yet.

## Trial Scorecard

| Trial | Components | C# files | Compile | Z3 |
|-------|------------|----------|---------|-----|
| T1 (CSV-to-JSON) | 1 | 3 | ✅ | ✅ |
| T5 (Document processing) | 1 | 2 | ✅ | ✅ |
| T7 (Marketplace) | 5 | 6 | ✅ | ✅ |
| T8 (CI/CD pipeline) | 8 | 10 | ✅ | ✅ |
| T12 (Task scheduler) | 6 | 10 | ✅ | ✅ |
| T13 (E-commerce) | 12 | 13 | ✅ | ✅ |
| T14 (Healthcare, rerun) | 8 | 12 | ✅ | ✅ |
| T14 (Healthcare, io-shell) | 8 | 8 | ✅ | ✅ |

8/8 trials compile clean. All components Z3-verified. Every component — including io-shell — has a Dafny skeleton with a contract.

## What We Are About To Do

### The Pipeline Has Shrunk

The connector diagnosis (Aug 12) revealed that most pipeline phases are unnecessary. The model's job is *thinking*; everything after architecture is code.

**AI team (design, thinking):**
- Ideation → Architecture (carapace WITH connector specs) → Design Review (= design QA)

**Code (deterministic, no model):**
- Orchestrator assembles from carapace connector specs (wires components)
- Z3 verifies contracts
- Dafny → C# translation (with `--test-assumptions Externs` → runtime contract checks)
- Bot harness tests (pushes data through CLI, exercises GUI via hotkeys, compares to spec)

**Result:**
- Pass → carve to registry
- Fail → retry (several tries, AI team adjusts)
- Still fails → human opens it up

**Eliminated phases:** Pseudocode, Dafny Implementation (Imp), C# Implementation (Imp), QA phase (model). Design Review IS the QA. The bot harness IS the test.

### Connector Forms on the Carapace

The carapace needs new fields:
- `Connections` — which method on A calls which method on B, with argument mappings
- `MethodSignatures` — actual parameter types and return types, not just names
- `SharedTypes` — which types are shared across modules (via Dafny `include`)

The architecture prompt must ask the model to fill these out. The pattern registry must expose actual method signatures so the architect can map names to real pattern methods. See `wiki/connector-diagnosis.md`.

### Automated Proof (Bot Harness)

Every panel has a CLI (built-in stub cap). The GUI sits on top of the CLI — same commands, same entry points. Every field, button, and data entry point is keyboard-reachable.

The test harness (bot, not LLM):
1. Map the GUI (every hotkey → every control)
2. Feed test data through each control
3. Capture the output
4. Compare to the expected result from the spec
5. Pass/fail — fully automated, no human in the loop

### One Piece on the Desktop

The prompt selection sheet and the automated proof harness share the same user GUI — the Testmaster (Blazor desktop). It looks and works like one piece:

- **Left side:** the requirements checklist (select what you want, see the price)
- **Right side:** the proof dashboard (launch tests, see results, confirm it works)

One application. Two functions. The user selects requirements, the program is built, the test is launched, the proof is displayed. All in one place.

### Verify `--test-assumptions Externs`

Dafny can emit runtime contract checks for `{:extern}` methods. Status: hopeful but not yet verified. Needs standalone testing before relying on it. If it works, contracts follow the code to execution.

## See Also

- `wiki/connector-diagnosis.md` — the data flow trace, the shrunk pipeline, the closed loop
- `wiki/proof-methodology.md` — the seed → assemble → test → prove → carve flow
- `wiki/carapace-doctrine.md` — "Computers should know what I MEANT to say"
- `AGENTS.md` — project context and locked decisions
- `wiki/handoff-2026-08-12.md` — Bluejohn discovery, wiring scaffold, session handoff