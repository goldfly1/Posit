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

function ParseIntField(s: string): int
  decreases |s|
{
  if |s| == 0 then 0
  else ParseIntField(s[1..]) + (if '0' <= s[0] <= '9' then (s[0] as int - '0' as int) else 0)
}

method ParseLines(input: string, delim: string, quote: string, hasHeader: bool) returns (rows: seq<seq<string>>)
  requires |input| > 0
  requires |delim| == 1
  requires |quote| == 1
  ensures |rows| >= 0
  decreases |input|
{
  rows := [];
  var line := "";
  var i := 0;
  var first := hasHeader;
  while i < |input|
    invariant 0 <= i <= |input|
    invariant |rows| >= 0
    decreases |input| - i
  {
    if input[i] == '\n' {
      if |line| > 0 && !first {
        var f := ParseLine(line, delim, quote);
        rows := rows + [f];
      }
      if |line| > 0 { first := false; }
      line := "";
    } else if input[i] != '\r' {
      line := line + [input[i]];
    }
    i := i + 1;
  }
  if |line| > 0 && !first {
    var f := ParseLine(line, delim, quote);
    rows := rows + [f];
  }
}