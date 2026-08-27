# The Proof Methodology — C#-Direct (Aug 26, 2026)

> **Dafny dropped Aug 24.** This doc was rewritten. The Z3 proof gate is gone.
> `dotnet build` is the compile gate. Docker tests are the behavioral gate.

## The Flow

```
1. SPEC          → User provides natural language spec
2. ARCHITECT     → Model decomposes into components, writes C# interfaces (the carapace)
3. IMPLEMENT     → Model writes C# classes implementing the interfaces
                   Build correction loop: dotnet build → feed CS errors → retry (4×)
4. WIRE          → Deterministic WiringGenerator emits Wire.cs from scanned signatures
5. TEST          → Docker harness builds + runs program with test data
                   Exact output comparison against expected output
6. FIX           → WireFixer (6 retries) fixes compile/test failures
                   ImplFixer (3 retries) regenerates component code on logic failures
```

## What Replaced Z3

The Dafny→Z3 pipeline proved contracts were mathematically consistent. But 1/4 model hit rate on Dafny, opaque CoCo/R parser errors, and decorative contracts (`requires |lines| >= 0`) meant Z3 was proving nothing useful.

The C#-direct pipeline replaces formal proof with **empirical verification**:

- **`dotnet build`** — the compiler catches type errors, missing methods, signature mismatches. Clear, actionable error messages.
- **Docker harness** — builds the actual program, runs it with test data, captures output, compares exactly against expected output.
- **Correction loops** — model sees its own errors (compile errors, test failures) and fixes them. The compiler is the teacher.

This is weaker than mathematical proof — it verifies *tested inputs*, not *all possible inputs*. But it works with models that speak C# fluently, and the errors are actionable.

## The Hard Part — Does It Answer the Spec?

This remains the critical check. A program can compile and pass tests but still be wrong if the test data doesn't cover the spec's intent.

**How it works now:**

1. The architect writes test case descriptions (expected behavior shape, not specific values)
2. The QA model generates concrete test input + expected output from the architect's frame
3. The Docker harness builds and runs each test case
4. Output is compared **exactly** (whitespace-trimmed) against expected output
5. If exact comparison fails, fuzzy matching (keyword/shape) is a fallback

**Exact comparison catches real bugs that fuzzy matching rubber-stamps.**
Example: program outputs `[]` instead of `[{"name":"Alice",...}]`. Fuzzy matcher passes (starts with `[`). Exact comparison fails.

## What We Lost

- **Z3 proof.** No mathematical guarantee that implementations satisfy contracts for all inputs.
- **The "spec compiler" identity.** Posit is now a spec-driven code generator, not a verified spec compiler.
- **Contract enforcement at compile time.** C# interfaces enforce signatures and types, but not preconditions/postconditions.

## What We Gained

- **Model writes in its strong language.** No more C#-isms in Dafny.
- **No translation gap.** No ISequence<Rune> ↔ string, no BigRational, no dtor_ mapping.
- **Clear compiler errors.** `dotnet build` produces actionable C# compiler messages.
- **Simpler pipeline.** 3 phases instead of 5. ~2,290 lines deleted.

## Future Options

- **Property-based testing** (FsCheck/Hedgehog.NET) — random inputs against postconditions
- **Roslyn analyzers** — static analysis beyond `dotnet build`
- **Re-add Dafny later** — if model fluency improves (Dafny 5.0 parser, better training data)
- **Per-phase model routing** — stronger model for C#Impl, flash for architecture/QA

## See Also

- `wiki/pipeline-spec.md` — canonical pipeline spec
- `wiki/carapace-doctrine.md` — C# interface as carapace
- `wiki/handoff-2026-08-26.md` — latest handoff