# Interface Pattern: Stdin Single-Input Computation

## Problem Shape
Read one line from stdin → compute → print result.
Pure computation, no file I/O. The input line may contain multiple tokens
separated by whitespace (e.g., "32 F" = value + unit).

## Spec Verbs
convert, calculate, compute

## Component Interfaces

```csharp
// CORRECT: single string param — the whole stdin line
interface IConverter {
    string Convert(string input);
    // parses the input internally, computes, returns formatted result
}

// CORRECT: two params where input format provides exactly 2 tokens
interface IConverter {
    double Convert(double value, string unit);
    // value parsed from tokens[0], unit from tokens[1]
    // — only works if stdin format has exactly 2 tokens
}
```

## WRONG Pattern (causes IndexOutOfRange — T6 failure)
```csharp
// BAD: 3 params but stdin only provides 2 tokens ("32 F")
interface IConverter {
    double Convert(double value, string fromUnit, string toUnit);
    // tokens[2] doesn't exist → IndexOutOfRangeException
    // The spec says "converts to the other unit" — there is NO target unit
    // in the input. The method must INFER the target, not take it as a param.
}
```

## Type Chain
```
string (stdin line) → string (result)
  or
double + string (parsed tokens) → string (result)
```

## Connection Order
```
ReadLine → Convert(inputLine) → PrintLine
```

## Token Matching
Stdin input "32 F" splits into tokens[] = ["32", "F"].
- 1-param method: gets the whole line "32 F"
- 2-param method (double, string): tokens[0] parsed as double, tokens[1] as string
- 3-param method: FAILS — tokens[2] doesn't exist

The number of method parameters MUST NOT exceed the number of tokens
the stdin format produces. When the spec says "converts to the other unit,"
the method infers the target — it does NOT take it as a separate parameter.

## Proven Trials
T6 (Temperature Converter — "32 F" → "0 C")

## Key Constraint
Entry type is "stdin". The method parameter count must match the token count
of the input format. If the input is "VALUE UNIT" (2 tokens), the method
takes at most 2 parameters. A single-string param accepting the whole line
is the safest decomposition.