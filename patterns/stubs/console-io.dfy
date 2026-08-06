// Stubs: Console I/O portals
// Pre-cut {:extern} declarations for console operations.
// Attach to patterns that interact with the user.

// Print a line to console
method {:extern} PrintLine(text: string)
  ensures true

// Print without newline
method {:extern} Print(text: string)
  ensures true

// Read a line from console
method {:extern} ReadLine() returns (line: string)
  ensures |line| >= 0

// Read a character from console
method {:extern} ReadChar() returns (ch: string)
  ensures |ch| <= 1

// Clear the console
method {:extern} ClearScreen()
  ensures true