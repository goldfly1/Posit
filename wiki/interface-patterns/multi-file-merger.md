# Interface Pattern: Multi-File Merger

## Problem Shape
Read two files → validate compatibility → merge → print merged output.
Two input files, one output. The merge step takes BOTH parsed inputs.

## Spec Verbs
read, validate, merge, serialize

## Component Interfaces

```csharp
// Parser — parses one file's lines into structured rows
interface ICsvParser {
    string[][] Parse(string[] lines);
}

// Validator — validates two parsed datasets are compatible (same columns)
// Returns the MERGED rows, not a bool — data passes through for chaining
interface IMergeValidator {
    string[][] ValidateAndMerge(string[][] rows1, string[][] rows2);
    // returns merged rows if compatible, throws if column count mismatch
}

// Serializer — converts merged rows to output format
interface ICsvSerializer {
    string Serialize(string[][] rows);
}
```

## Type Chain
```
string[] (file1) → string[][] (parsed1)
string[] (file2) → string[][] (parsed2)
(string[][] + string[][]) → string[][] (merged) → string (output)
```

## Connection Order
```
ReadLines(file1) → Parse → [ret1]
ReadLines(file2) → Parse → [ret2]
ValidateAndMerge(ret1, ret2) → Serialize → PrintLine
```

## Proven Trials
T12 (Config Merger — two INI files, merge with conflict detection)

## Key Constraint
The merge/validate method takes TWO inputs (both parsed datasets) and returns
the merged result. Do NOT split into separate Validate(bool) + Merge(string[][])
— that creates a type-chain mismatch. Combine validation and merging into one
method that returns data.

## Entry Type
file (two file paths as args[0] and args[1])