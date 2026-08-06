// Stubs: File I/O portals
// Pre-cut {:extern} declarations for file operations.
// Attach these to any pattern that reads from or writes to files.
// Z3 assumes the contract. Pass 2 (C# Implementation) plugs in the bodies.

// Read entire file as string
method {:extern} ReadFile(path: string) returns (content: string)
  requires |path| > 0
  ensures |content| >= 0

// Write string to file
method {:extern} WriteFile(path: string, content: string)
  requires |path| > 0

// Append string to file
method {:extern} AppendFile(path: string, content: string)
  requires |path| > 0

// Read file as lines (sequence of strings)
method {:extern} ReadLines(path: string) returns (lines: seq<string>)
  requires |path| > 0
  ensures |lines| >= 0

// Write lines to file
method {:extern} WriteLines(path: string, lines: seq<string>)
  requires |path| > 0

// Check if file exists
method {:extern} FileExists(path: string) returns (found: bool)
  requires |path| > 0