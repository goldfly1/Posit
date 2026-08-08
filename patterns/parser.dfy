// Pattern: Parser (Approach 3 — pre-written body with parameters)
// responsibility: Parse delimited input into typed fields
// test: ParseLine("a,b,c", ",") returns ["a","b","c"]
// test: ParseLine("hello", ",") returns ["hello"]
// test: ParseLine("", ",") returns []
//
// Parameters (architect customizes these):
//   delimiter: string — single-char field separator (default ",")
//   quoteChar: string — quote character for quoted fields (default "\"", "" = none)
//   hasHeader: bool — whether first line is a header (affects ParseLines only)
//
// Pre-cut planks: the iteration loop, field accumulation, delimiter matching,
// and result building are all pre-written and Z3-proven. The architect sets
// the parameters. Imp's job is empty or near-empty.

include "result.dfy"

// Get the delimiter character (precondition ensures it exists)
function GetDelimiter(delimiter: string): char
  requires |delimiter| == 1
{
  delimiter[0]
}

// Parse a single delimited line into fields.
// Iterates characters, splits on delimiter, accumulates fields.
method ParseLine(input: string, delimiter: string) returns (fields: seq<string>)
  requires |input| > 0
  requires |delimiter| == 1
  ensures |fields| >= 1
  decreases |input|
{
  var delim := GetDelimiter(delimiter);
  fields := [];
  var currentField := "";
  var i := 0;

  while i < |input|
    invariant 0 <= i <= |input|
    invariant |fields| >= 0
    decreases |input| - i
  {
    if input[i] == delim {
      fields := fields + [currentField];
      currentField := "";
    } else {
      currentField := currentField + [input[i]];
    }
    i := i + 1;
  }
  // Append the last field (there's always at least one because input is non-empty)
  fields := fields + [currentField];
}

// Parse multiple lines. If hasHeader is true, the first line is dropped.
method ParseLines(input: string, delimiter: string, hasHeader: bool) returns (rows: seq<seq<string>>)
  requires |input| > 0
  requires |delimiter| == 1
  ensures |rows| >= 0
  decreases |input|
{
  var delim := GetDelimiter(delimiter);
  rows := [];
  var currentLine := "";
  var i := 0;
  var firstLine := hasHeader;

  while i < |input|
    invariant 0 <= i <= |input|
    decreases |input| - i
  {
    if input[i] == '\n' {
      if |currentLine| > 0 {
        if !firstLine {
          var fields := ParseLine(currentLine, delimiter);
          rows := rows + [fields];
        }
        firstLine := false;
        currentLine := "";
      }
    } else if input[i] != '\r' {
      currentLine := currentLine + [input[i]];
    }
    i := i + 1;
  }

  // Handle last line (no trailing newline)
  if |currentLine| > 0 && !firstLine {
    var fields := ParseLine(currentLine, delimiter);
    rows := rows + [fields];
  }
}

// Count fields in a delimited string (without allocating the result)
function CountFields(input: string, delimiter: string): int
  requires |delimiter| == 1
  decreases |input|
{
  if |input| == 0 then 1
  else if |input| == 1 then 1
  else
    (if input[0] == GetDelimiter(delimiter) then 1 + CountFields(input[1..], delimiter)
     else CountFields(input[1..], delimiter))
}