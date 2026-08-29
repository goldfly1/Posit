# Interface Pattern: Validator with Branching

## Problem Shape
Read input → validate → (if valid: serialize and print output | if invalid: print error and exit 1).
Branching on a validation result. The validator MUST return data that chains into the next step,
NOT a bare bool that breaks the type chain.

## Spec Verbs
validate, parse, serialize, check

## Component Interfaces

```csharp
// CORRECT: validator returns the validated data (passes through for chaining)
interface IRowValidator {
    string[][] Validate(string[][] rows, int expectedFieldCount);
    // returns the rows if valid, throws or returns empty if invalid
    // — the return type chains into the serializer
}

// CORRECT: error detection via return-value convention
interface IResultReporter {
    string Report(string[][] validRows);  // valid rows → output string
}
```

## WRONG Pattern (causes type-chain mismatch — T5 failure)
```csharp
// BAD: bool return breaks the chain — nothing to feed the next step
interface IValidator {
    bool Validate(string[][] rows);  // ← bool can't chain into Merge/Serialize
}
// The architect connects Validate → Merge, but Merge expects string[][],
// not bool. TypeChainChecker rejects this.
```

## Type Chain (CORRECT)
```
string[] (input) → string[][] (parsed) → string[][] (validated) → string (output)
```

## Connection Order
```
ReadLines → Parse → Validate → Serialize → PrintLine
```
Error path: if Validate throws or returns empty → print error → exit 1

## Proven Trials
T3 (Filtered CSV Export — validate then serialize or error)

## Key Constraint
Validation components must return DATA, not bool. The error path uses the
Error: string convention (return "Error: ..." or throw) — NOT a bool that
breaks the type chain. BranchCondition on the orchestrator component handles
the if-branch; the method signature must still chain.