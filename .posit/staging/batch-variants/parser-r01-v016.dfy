function GetDelimiter(d: string): char
  requires |d| == 1
{ d[0] }

method ParseLine(input: string, delimiter: string, quoteChar: string) returns (fields: seq<string>)
  requires |input| > 0
  requires |delimiter| == 1
  requires |quoteChar| == 1
  ensures |fields| >= 1
  ensures forall k, j :: 0 <= k < |fields| && 0 <= j < |fields[k]| ==> fields[k][j] != GetDelimiter(delimiter) && (exists m :: 0 <= m < |input| && input[m] == fields[k][j])
  decreases |input|
{
  var delim := GetDelimiter(delimiter);
  fields := [];
  var cur := "";
  var i := 0;
  while i < |input|
    invariant 0 <= i <= |input|
    invariant |fields| >= 0
    invariant forall k, j :: 0 <= k < |fields| && 0 <= j < |fields[k]| ==> fields[k][j] != delim && (exists m :: 0 <= m < i && input[m] == fields[k][j])
    invariant forall j :: 0 <= j < |cur| ==> cur[j] != delim && (exists m :: 0 <= m < i && input[m] == cur[j])
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

method ParseIntField(s: string) returns (n: int)
  requires |s| > 0
  requires forall i :: 0 <= i < |s| ==> '0' <= s[i] && s[i] <= '9'
  ensures 0 <= n
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

method Parse(input: string) returns (rows: seq<seq<string>>, values: seq<int>)
  requires |input| > 0
  requires forall i :: 0 <= i < |input| ==> ('0' <= input[i] && input[i] <= '9') || input[i] == ','
  ensures |rows| >= 0
  ensures |values| >= 0
  decreases |input|
{
  var fields := ParseLine(input, ",", "\"");
  rows := [fields];
  values := [];
  var i := 0;
  while i < |fields|
    invariant 0 <= i <= |fields|
    invariant |values| >= 0
    decreases |fields| - i
  {
    if |fields[i]| > 0 {
      var s := fields[i];
      assert forall j :: 0 <= j < |s| ==> '0' <= s[j] && s[j] <= '9' by {
        forall j | 0 <= j < |s| {
          var c := s[j];
          var m :| 0 <= m < |input| && input[m] == c;
          assert ('0' <= input[m] && input[m] <= '9') || input[m] == ',';
          assert c != ',';
        }
      }
      var v := ParseIntField(s);
      values := values + [v];
    }
    i := i + 1;
  }
}