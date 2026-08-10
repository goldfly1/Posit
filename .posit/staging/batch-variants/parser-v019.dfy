function GetDelim(d: string): char
  requires |d| == 1
{
  d[0]
}

method ParseLine(input: string, delim: string, quote: string) returns (fields: seq<string>)
  requires |input| > 0
  requires |delim| == 1
  requires |quote| == 1
  ensures |fields| >= 1
  decreases |input|
{
  var d := GetDelim(delim);
  var q := quote[0];
  fields := [];
  var cur := "";
  var i := 0;
  var inQ := false;
  while i < |input|
    invariant 0 <= i <= |input|
    invariant |fields| >= 0
    decreases |input| - i
  {
    if input[i] == q {
      inQ := !inQ;
    } else if !inQ && input[i] == d {
      fields := fields + [cur];
      cur := "";
    } else {
      cur := cur + [input[i]];
    }
    i := i + 1;
  }
  fields := fields + [cur];
}

method CountQuoted(input: string, quote: string) returns (n: int)
  requires |quote| == 1
  ensures n >= 0
  decreases |input|
{
  var q := quote[0];
  n := 0;
  var i := 0;
  while i < |input|
    invariant 0 <= i <= |input|
    invariant n >= 0
    decreases |input| - i
  {
    if input[i] == q { n := n + 1; }
    i := i + 1;
  }
}

method ParseLines(input: string, delim: string, quote: string) returns (rows: seq<seq<string>>)
  requires |input| > 0
  requires |delim| == 1
  requires |quote| == 1
  ensures |rows| >= 0
  decreases |input|
{
  rows := [];
  var line := "";
  var i := 0;
  while i < |input|
    invariant 0 <= i <= |input|
    invariant |rows| >= 0
    decreases |input| - i
  {
    if input[i] == '\n' {
      if |line| > 0 {
        var f := ParseLine(line, delim, quote);
        rows := rows + [f];
      }
      line := "";
    } else if input[i] != '\r' {
      line := line + [input[i]];
    }
    i := i + 1;
  }
  if |line| > 0 {
    var f := ParseLine(line, delim, quote);
    rows := rows + [f];
  }
}

function CountFields(input: string, delim: string): int
  requires |delim| == 1
  decreases |input|
{
  if |input| == 0 then 1
  else if input[0] == GetDelim(delim) then 1 + CountFields(input[1..], delim)
  else if |input| == 1 then 1
  else CountFields(input[1..], delim)
}