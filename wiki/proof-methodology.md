# The Proof Methodology — End-to-End Verification

> **"We will prove the first 20 by eye. We will either just use those and add a few more as we find them, or we will take the things that work together out of the program and add to the registry."**

## The Flow

```
1. SEED          → Registry holds the 17 proven panels (the jigsaw puzzle pieces)
2. ASSEMBLE      → The team (architect + pipeline) puts the pieces together into a program
3. TEST          → Push data through the CLI, push every button in the GUI, capture the output
4. PROVE         → When the bot or human shows it does what we said it would → IT WORKS
5. CARVE         → Pull out any piece that worked well, add it to the registry (proven because we just proved it)
```

The registry grows from verified end products. Each new piece is carved from a program that was proven to work in step 4. The next assembly has more pieces to choose from.

## The Foundation

The 17 base patterns are Z3-proven. They're the seed. The architect picks from the menu, sets parameters, the pipeline composes the skeleton. That works — 8/8 trials green.

The patterns are named by the function they perform: `parser` parses, `repository` stores, `scheduler` queues, `pipeline` processes through stages. The name IS the spec. The function IS the proof.

We don't re-verify the 17 panels for every trial. They're proven once. Compositions of proven parts are verified by Z3 (consistency) and the harness (compilation). The remaining question is whether the program does what the user asked for.

## The Hard Part — #11: Does It Answer the Prompt?

This is the cotton candy check. Everything before it (contracts, compilation, stubs) is necessary but not sufficient. #11 is the one that says "this is a real program that does what I asked for, and I can prove it because I just used it."

### How It Works

Every panel has a CLI built in (the `io-console-program` stub cap). The CLI is always there. The GUI sits on top of the CLI — same commands, same entry points. Every field, button, and data entry point in the GUI is reachable by hotkey.

**The test procedure:**

1. **Somebody makes a list of what the program does** — from the user prompt, through the architect, through the design review. The actual user-facing objectives: what buttons exist, what happens when they get pushed, what the user can do with the software. Not contracts. Not patterns. The thing the user asked for.

2. **These requirements are the objectives for the program** — what do you want the software and your user to be able to do? Down to what buttons are there and what happens when they get pushed.

3. **Generate test data** — 1,000 or 10,000 or 100,000 faux records. Push them through the always-built-in CLI. The commands are deterministic.

4. **Map the GUI** — every field, every button, every data entry point is reachable by hotkey. Map the layout. Capture the data. Push the buttons. See that the right command gets fired.

5. **Compare output to expected result** — the expected result comes from the spec (step 1). Not from the model. Not from the test. From what the user asked for. If output matches expected → golden. If not → cotton candy.

### Why This Works

- The CLI is deterministic — same input, same output, every time
- The GUI is a face on the CLI — same commands, same entry points
- Every GUI control is keyboard-reachable — fully automatable, no human in the loop
- The data is generatable — faux records at any scale
- The expected results come from the spec — not circular, not model-generated
- The check is: does the program do what its name says it does
- The "bot" is a script, not an LLM — push button, get output, compare to expected. Deterministic, repeatable, no bleary-eyed humans.

### What This Proves

- The 17 base panels are proven by Z3 (contracts consistent) + stub certification (plumbing compiles)
- Each composed program is proven by Z3 (composition consistent) + harness (C# compiles) + **runtime verification** (program does what the spec asked)
- Pieces carved from proven programs are trusted — they were just proven in context

## The Registry as a Jigsaw Puzzle

The registry holds whatever we seed it with (the 17 proven panels). The team assembles programs from those pieces. When a program is proven to work (step 4), interesting combinations can be carved out and added to the registry (step 5). The registry grows. Each new piece is proven because we just proved the program it came from.

Next time the architect needs that shape, it's already in the registry — already proven, already trusted. The puzzle gets bigger. The assemblies get richer. The verification accumulates.

## The Missing Artifact

Nobody in the pipeline currently writes down "here are the things this program does, here are the buttons, here's what happens when you push them." The architect writes contract-level test cases. The design review reviews the architecture. But the user-facing requirements list — what the program does for the user — is the gap between contract review (#1) and the cotton candy check (#11).

That list is the bridge. It's produced from the spec, checked against the running program. It's the Testmaster's checklist.

## Where the Pieces Are

- CLI: built into every panel (`io-console-program` stub cap)
- GUI: sits on top of CLI (same commands, same entry points)
- GUI controls: every field, button, entry point reachable by hotkey
- Test data: generatable at any scale (1K, 10K, 100K faux records)
- Expected results: from the spec (the user-facing requirements list)
- The Testmaster (Blazor desktop): where the test is launched and results are displayed
- The harness: pushes data, captures output, compares to expected

## Status

- Items 6, 7, 8, 9: DONE (phantom imports, fabricated template, carapace checks, stub certification)
- Option B: DONE (io-shell components get Dafny skeletons — every component has a contract)
- Item 3 (xUnit tests): answered by runtime verification — the program doing what the spec asked IS the test
- Item 1 (contract review): the user-facing requirements list bridges this
- Item 11 (cotton candy): **ROOT CAUSE FOUND** — the carapace has no connector data. The orchestrator can't wire components because the architect was never asked for connection specifications. See `wiki/connector-diagnosis.md`.
- Item 12 (Testmaster): the Blazor desktop where the test is launched and results are shown
- Item 13 (harness → JSON): the harness pushes data and captures output as structured proof

## The Closed Loop (Aug 12, 2026)

Two insights closed the loop:

1. **Connector forms on the carapace** — the architect fills out not just component names and dependencies, but method signatures, connection specs (A.method calls B.method with what args), and shared types. The orchestrator reads these and wires deterministically. No model judgment at wiring time.

2. **Automated QA via bot harness** — every GUI control is keyboard-reachable. A bot (script, not LLM) maps hotkeys, pushes data, captures output, compares to spec. Fully deterministic, no human.

The pipeline shrinks: AI does thinking (ideation, architecture, design review). Code does everything else (assemble, verify, translate, test). See `wiki/connector-diagnosis.md` for the full diagnosis and the shrunk pipeline.

The registry grows organically: 17 atoms → proven molecules → proven compounds → proven systems. Each cycle makes the next assembly richer.