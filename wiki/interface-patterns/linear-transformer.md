# Interface Pattern: Linear Data Transformer

## Problem Shape
Read input file → parse → transform → serialize → print output.
Single-direction data flow with no branching.

## Spec Verbs
parse, transform, serialize, convert, export

## Component Interfaces

```csharp
// Component 1: Parser — reads raw input, produces structured data
interface IParser {
    string[][] Parse(string[] lines);  // raw lines → structured rows
}

// Component 2: Transformer — converts structured data to output format
interface ITransformer {
    string Transform(string[][] rows);  // structured rows → output string (e.g., JSON)
}
```

## Type Chain
```
string[] (input lines) → string[][] (parsed rows) → string (output)
```

## Connection Order
```
ReadLines → Parse → Transform → PrintLine
```

## Proven Trials
T1 (CSV→JSON), T2 (JSON→CSV), T11 (Markdown→JSON)

## Key Constraint
Both params and returns use native C# types only (string, string[], string[][], int, bool).
No custom DTOs — the return type IS the data.