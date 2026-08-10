datatype Field = Str(s: string) | Empty

function GetDelim(d: string): char
  requires |d| == 1 { d[0] }

method ParseLine(input: string, d: string, q: string, e: string, c: string) returns (f: seq<Field>)
  requires |input| > 0
  requires |d| == 1
  requires |q| <= 1
  requires |e| <= 1
  requires |c| <= 1
  ensures |f| >= 1
  decreases |input|
{
  var delim := GetDelim(d);
  var qc := if |q| == 1 then q[0] else '\0';
  var ec := if |e| == 1 then e[0] else '\0';
  f := [];
  var cf := "";
  var i := 0;
  var inq := false;
  while i < |input|
    invariant 0 <= i <= |input|
    decreases |input| - i
  {
    if |e| == 1 && input[i] == ec {
      cf := cf + [input[i]];
      if i + 1 < |input| {
        cf := cf + [input[i+1]];
        i := i + 1;
      }
    } else if |q| == 1 && input[i] == qc {
      inq := !inq;
      cf := cf + [input[i]];
    } else if !inq && input[i] == delim {
      f := f + [if |cf| == 0 then Empty else Str(cf)]; cf := "";
    } else {
      cf := cf + [input[i]];
    }
    i := i + 1;
  }
  f := f + [if |cf| == 0 then Empty else Str(cf)];
}

method ParseLines(input: string, d: string, q: string, e: string, c: string) returns (r: seq<seq<Field>>)
  requires |input| > 0
  requires |d| == 1
  requires |q| <= 1
  requires |e| <= 1
  requires |c| <= 1
  ensures |r| >= 0
  decreases |input|
{
  r := [];
  var cl := "";
  var i := 0;
  var first := false;
  while i < |input|
    invariant 0 <= i <= |input|
    decreases |input| - i
  {
    if input[i] == '\n' {
      if |cl| > 0 {
        var isCom := |c| == 1 && cl[0] == c[0];
        if !first && !isCom { var f := ParseLine(cl, d, q, e, c); r := r + [f]; }
        first := false;
        cl := "";
      }
    } else if input[i] != '\r' { cl := cl + [input[i]]; }
    i := i + 1;
  }
  if |cl| > 0 && !first {
    var isCom := |c| == 1 && cl[0] == c[0];
    if !isCom { var f := ParseLine(cl, d, q, e, c); r := r + [f]; }
  }
}