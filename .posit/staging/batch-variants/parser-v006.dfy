function GetDelim(d: string): char
  requires |d| == 1
{
  d[0]
}

method ParseLine(input: string, delim: string) returns (fields: seq<string>)
  requires |input| > 0
  requires |delim| == 1
  ensures |fields| >= 1
  decreases |input|
{
  var d := GetDelim(delim);
  fields := [];
  var cur := "";
  var i := 0;
  while i < |input|
    invariant 0 <= i <= |input|
    invariant |fields| >= 0
    decreases |input| - i
  {
    if input[i] == d {
      fields := fields + [cur];
      cur := "";
    } else {
      cur := cur + [input[i]];
    }
    i := i + 1;
  }
  fields := fields + [cur];
}

method ParseLines(input: string, delim: string) returns (rows: seq<seq<string>>)
  requires |input| > 0
  requires |delim| == 1
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
        var f := ParseLine(line, delim);
        rows := rows + [f];
      }
      line := "";
    } else if input[i] != '\r' {
      line := line + [input[i]];
    }
    i := i + 1;
  }
  if |line| > 0 {
    var f := ParseLine(line, delim);
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