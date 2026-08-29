# Interface Pattern: Multi-Step Transform Pipeline

## Problem Shape
Read input → filter → convert → group → serialize → print.
3+ transformation steps in sequence, each feeding the next.

## Spec Verbs
filter, convert, group, transform, serialize

## Component Interfaces

```csharp
// Filter — removes rows not matching a criteria
interface IProductFilter {
    string[][] Filter(string[][] rows, double minPrice);
}

// Converter — transforms values in each row
interface IPriceConverter {
    string[][] Convert(string[][] rows, double rate);
}

// Grouper — groups rows by a field
interface ICategoryGrouper {
    string Group(string[][] rows);
    // returns JSON object: {"category": [{"name":"...","price":"..."}]}
}
```

## Type Chain
```
string[] (input) → string[][] (parsed) → string[][] (filtered) → string[][] (converted) → string (JSON output)
```

## Connection Order
```
ReadLines → Parse → Filter → Convert → Group → PrintLine
```

## Proven Trials
T10 (Product Pipeline — filter by price, convert USD→EUR, group by category)

## Key Constraint
Each step returns the SAME type (string[][]) so the chain type-checks.
The final step (grouper) returns string (JSON output).
Do NOT introduce custom DTOs between steps — use string[][] throughout
and let each method interpret the columns by index.

## Fidelity Gate Note
The fidelity gate requires ALL logic components to appear as connection targets.
Do NOT connect CLI → FileIO.ReadFile → print (bypassing the logic components).
Every declared logic component MUST be in the connection chain.