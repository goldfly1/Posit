# Interface Pattern: Filter and Aggregate

## Problem Shape
Read input → filter by criteria → count/aggregate → print formatted result.
A scalar CLI arg (filter word, level name) narrows the dataset before aggregation.

## Spec Verbs
filter, count, aggregate, analyze

## Component Interfaces

```csharp
// Filter — narrows rows by a scalar criterion (level, category, etc.)
interface IFilter {
    string[] Filter(string[] lines, string criteria);
    // returns only lines matching the criteria
}

// Counter — counts the filtered result
interface ICounter {
    int Count(string[] filteredLines);
    // returns the count
}
```

## Type Chain
```
string[] (input lines) + string (scalar arg) → string[] (filtered) → int (count)
```

## Connection Order
```
ReadLines → Filter(lines, scalarArg) → Count → PrintLine
```

## Output Format
The count is a raw int. Use OutputFormat on the test case:
- OutputFormat: "LEVEL: {value}" → prints "ERROR: 2"
- EmptyOutputText: "No entries" → prints when count == 0

## Scalar Arg Handling
The filter word arrives as args[1] (after the data file at args[0]).
Set cliArgs on the test case: "cliArgs": "ERROR".
The emitter routes it via the Scalar role (role-dispatch).

## Proven Trials
T8 (Log File Analyzer — filter by level, count, print "ERROR: 2")

## Key Constraint
The filter method takes the scalar arg as a string parameter.
Do NOT make the architect read the scalar as a file — it's a plain word.
The connection chain passes the scalar from args[1] through to the filter method.