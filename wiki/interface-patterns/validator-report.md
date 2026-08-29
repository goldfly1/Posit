# Interface Pattern: Validator with Report

## Problem Shape
Read input → validate each row → count valid/invalid → print summary report.
Multiple output lines (report format), not a single transformed value.

## Spec Verbs
validate, count, report

## Component Interfaces

```csharp
// Validator — validates rows and returns a result with counts
interface IValidator {
    ValidationReport Validate(string[][] rows, int expectedFieldCount);
    // returns valid count, invalid count, and error details
}

// Report struct — native C# type, no custom DTOs needed
record ValidationReport(int ValidCount, int InvalidCount, string[] Errors);

// Reporter — formats the report as output lines
interface IReporter {
    string[] Report(ValidationReport result);
    // returns lines: "Valid: N", "Invalid: M", "Row X: expected Y, got Z"
}
```

## Type Chain
```
string[] (input) → string[][] (parsed) → ValidationReport (validated) → string[] (report lines)
```

## Connection Order
```
ReadLines → Parse → Validate → Report → PrintLines
```

## Output Format
Multiple lines printed via string[] joined with newlines.
Use OutputFormat if the spec demands a specific format per line.

## Proven Trials
T9 (CSV Validator with Report — "Valid: 2\nInvalid: 0")

## Key Constraint
The validator returns a RESULT TYPE (record/struct), not a bare bool.
The result carries both the counts AND the error details.
The reporter formats the result into output lines.
Do NOT split into separate Validate(bool) + Count(int) — combine into one
method that returns a structured result.

## Alternative (simpler — no record type)
If the spec's output is a single string, combine validation and reporting:
```csharp
interface IValidator {
    string ValidateAndReport(string[] lines);
    // returns the full report as a single string with \n-separated lines
}
```
This avoids custom types and chains directly to PrintLine.