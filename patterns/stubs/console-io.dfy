// Stubs: Console I/O portals
// Pre-cut {:extern} declarations for console operations.
// Attach to patterns that interact with the user.
// Robust CLI: ReadLine, ReadSplitLine, PrintLine, PrintError, PrintResult.

// Print a line to console
method {:extern} PrintLine(text: string)
  ensures true

// Print without newline
method {:extern} Print(text: string)
  ensures true

// Read a line from console
method {:extern} ReadLine() returns (line: string)
  ensures |line| >= 0

// Read a line and split by delimiter into fields
// Returns empty seq if input is empty
method {:extern} ReadSplitLine(delimiter: string) returns (fields: seq<string>)
  ensures |fields| >= 0

// Read a line and split by space delimiter
method {:extern} ReadArgs() returns (args: seq<string>)
  ensures |args| >= 0

// Print an error to stderr and return non-zero
method {:extern} PrintError(text: string)
  ensures true

// Print a result line to stdout
method {:extern} PrintResult(text: string)
  ensures true

// Read a character from console
method {:extern} ReadChar() returns (ch: string)
  ensures |ch| <= 1

// Clear the console
method {:extern} ClearScreen()
  ensures true