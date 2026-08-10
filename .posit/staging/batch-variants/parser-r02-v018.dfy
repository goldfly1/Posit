function GetDelimiter(d: string): char
  requires |d| == 1
{
  d[0]
}

function GetQuote(q: string): char
  requires |q| == 1
{
  q[0]
}

method ParseLine(input: string, delimiter: string, quoteChar: string) returns (fields: seq<string>)
  requires |input| > 0
  requires |delimiter| == 1
  requires |quoteChar| == 1
  ensures |fields| >= 1
  decreases |input|
{
  var delim := GetDelimiter(delimiter);
  var quote := GetQuote(quoteChar);
  fields := [];
  var cur := "";
  var i := 0;
  var inQ := false;
  while i < |input|
    invariant 0 <= i <= |input|
    invariant |fields| >= 0
    decreases |input| - i
  {
    if input[i] == quote {
      inQ := !inQ;
      cur := cur + [input[i]];
    } else if !inQ && input[i] == delim {
      fields := fields + [cur];
      cur := "";
    } else {
      cur := cur + [input[i]];
    }
    i := i + 1;
  }
  fields := fields + [cur];
}

method ParseLines(input: string, delimiter: string, quoteChar: string) returns (rows: seq<seq<string>>)
  requires |input| > 0
  requires |delimiter| == 1
  requires |quoteChar| == 1
  ensures |rows| >= 0
  decreases |input|
{
  rows := [];
  var cur := "";
  var i := 0;
  while i < |input|
    invariant 0 <= i <= |input|
    decreases |input| - i
  {
    if input[i] == '\n' {
      if |cur| > 0 {
        var f := ParseLine(cur, delimiter, quoteChar);
        rows := rows + [f];
        cur := "";
      }
    } else if input[i] != '\r' {
      cur := cur + [input[i]];
    }
    i := i + 1;
  }
  if |cur| > 0 {
    var f := ParseLine(cur, delimiter, quoteChar);
    rows := rows + [f];
  }
}