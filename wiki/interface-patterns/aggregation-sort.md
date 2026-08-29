# Interface Pattern: Aggregation with Sort

## Problem Shape
Read input → tokenize → aggregate (count frequencies) → sort → print lines.
Output is multiple lines, one per aggregated entry, sorted by criteria.

## Spec Verbs
aggregate, count, sort, tokenize

## Component Interfaces

```csharp
// Tokenizer — splits text into words
interface ITokenizer {
    string[] Tokenize(string content);
}

// Aggregator — counts word frequencies and sorts
interface IFrequencyAggregator {
    string[] Aggregate(string[] words);
    // returns "count word" lines sorted by count desc, ties alphabetical
}
```

## Type Chain
```
string (file content) → string[] (words) → string[] (output lines)
```

## Connection Order
```
ReadFile → Tokenize → Aggregate → PrintLines
```

## Proven Trials
T4 (Word Frequency Counter — "3 the\n2 cat\n1 mat")

## Key Constraint
The aggregator returns string[] (one line per entry), printed via
the string[] join convention in EmitPrint. Empty input → empty output
(no bare newline — the empty-collection guard handles this).

## Entry Type
file (single file path as args[0], content read via ReadAllText)