function GetDelim(d: string): char
  requires |d| == 1 { d[0] }

method ParseLine(input: string, d: string) returns (f: seq<string>)
  requires |input| > 0
  requires |d| == 1
  ensures |f| >= 1
  decreases |input|
{
  var delim := GetDelim(d);
  f := [];
  var cf := "";
  var i := 0;
  while i < |input|
    invariant 0 <= i <= |input|
    decreases |input| - i
  {
    if input[i] == delim { f := f + [cf]; cf := ""; }
    else { cf := cf + [input[i]]; }
    i := i + 1;
  }
  f := f + [cf];
}

method ParseLines(input: string, d: string) returns (r: seq<seq<string>>)
  requires |input| > 0
  requires |d| == 1
  ensures |r| >= 0
  decreases |input|
{
  r := [];
  var cl := "";
  var i := 0;
  var first := true;
  while i < |input|
    invariant 0 <= i <= |input|
    decreases |input| - i
  {
    if input[i] == '\n' {
      if |cl| > 0 {
        if !first { var f := ParseLine(cl, d); r := r + [f]; }
        first := false;
        cl := "";
      }
    } else if input[i] != '\r' { cl := cl + [input[i]]; }
    i := i + 1;
  }
  if |cl| > 0 && !first { var f := ParseLine(cl, d); r := r + [f]; }
}