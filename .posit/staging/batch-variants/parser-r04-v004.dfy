function GetDelim(d: string): char
  requires |d| == 1
{
  d[0]
}

method ParseLine(input: string, delimiter: string) returns (fields: seq<string>)
  requires |input| > 0
  requires |delimiter| == 1
  ensures |fields| >= 1
  decreases |input|
{
  var delim := GetDelim(delimiter);
  fields := [];
  var cur := "";
  var i := 0;
  while i < |input|
    invariant 0 <= i <= |input|
    invariant |fields| >= 0
    decreases |input| - i
  {
    if input[i] == delim {
      fields := fields + [cur];
      cur := "";
    } else {
      cur := cur + [input[i]];
    }
    i := i + 1;
  }
  fields := fields + [cur];
}

method ParseInt(s: string) returns (n: int)
  requires forall k :: 0 <= k < |s| ==> '0' <= s[k] <= '9'
  ensures n >= 0
  decreases |s|
{
  n := 0;
  var i := 0;
  while i < |s|
    invariant 0 <= i <= |s|
    invariant n >= 0
    decreases |s| - i
  {
    n := n * 10 + (s[i] as int - '0' as int);
    i := i + 1;
  }
}

method ParseLines(input: string, delimiter: string, hasHeader: bool) returns (rows: seq<seq<string>>)
  requires |input| > 0
  requires |delimiter| == 1
  ensures |rows| >= 0
  decreases |input|
{
  rows := [];
  var cur := "";
  var i := 0;
  var first := hasHeader;
  while i < |input|
    invariant 0 <= i <= |input|
    decreases |input| - i
  {
    if input[i] == '\n' {
      if |cur| > 0 {
        if !first {
          var f := ParseLine(cur, delimiter);
          rows := rows + [f];
        }
        first := false;
        cur := "";
      }
    } else if input[i] != '\r' {
      cur := cur + [input[i]];
    }
    i := i + 1;
  }
  if |cur| > 0 && !first {
    var f := ParseLine(cur, delimiter);
    rows := rows + [f];
  }
}