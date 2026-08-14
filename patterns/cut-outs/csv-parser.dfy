// Cut-out: csv-parser
// Pattern: parser (conforms to parser pattern signatures)
// Domain: data processing
// Params: none (fully self-contained)
// responsibility: parse CSV lines into fields using comma delimiter
// test: ParseLine("a,b,c") returns [["a","b","c"]]

// Parse a single CSV line into fields by comma delimiter
// Handles basic CSV (no quote escaping — that's a future cut-out)
method ParseLine(input: string, delimiter: string) returns (fields: seq<string>)
  requires |input| >= 0
  requires |delimiter| == 1
  ensures |fields| >= 1
  decreases |input|
{
  fields := [];
  var currentField := "";
  var i := 0;
  var delim := delimiter[0];
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
  fields := fields + [currentField];
}

// Parse multiple lines (each line is a string) into rows of fields
// Input is the full file content as a sequence of lines
method ParseLines(lines: seq<string>, delimiter: string) returns (rows: seq<seq<string>>)
  requires |delimiter| == 1
  ensures |rows| == |lines|
  decreases |lines|
{
  rows := [];
  var i := 0;
  while i < |lines|
    invariant 0 <= i <= |lines|
    invariant |rows| == i
    decreases |lines| - i
  {
    var fields := ParseLine(lines[i], delimiter);
    rows := rows + [fields];
    i := i + 1;
  }
}

// Count fields in a line (for validation)
method CountFields(input: string, delimiter: string) returns (count: int)
  requires |input| >= 0
  requires |delimiter| == 1
  ensures count >= 1
  decreases |input|
{
  count := 1;
  var i := 0;
  var delim := delimiter[0];
  while i < |input|
    invariant 0 <= i <= |input|
    invariant count >= 1
    decreases |input| - i
  {
    if input[i] == delim {
      count := count + 1;
    }
    i := i + 1;
  }
}