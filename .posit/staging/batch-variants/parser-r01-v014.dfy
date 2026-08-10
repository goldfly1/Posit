function GetDelimiter(d: string): char
  requires |d| == 1
{ d[0] }

method ParseLine(input: string, delimiter: string, quoteChar: string) returns (fields: seq<string>)
  requires |input| > 0
  requires |delimiter| == 1
  requires |quoteChar| == 1
  ensures |fields| >= 1
  decreases |input|
{
  var delim := GetDelimiter(delimiter);
  var qc := quoteChar[0];
  fields := [];
  var cur := "";
  var i := 0;
  var inQuote := false;
  var escaped := false;
  while i < |input|
    invariant 0 <= i <= |input|
    invariant |fields| >= 0
    decreases |input| - i
  {
    if escaped {
      cur := cur + [input[i]];
      escaped := false;
    } else if input[i] == '\\' {
      escaped := true;
    } else if input[i] == qc {
      inQuote := !inQuote;
    } else if !inQuote && input[i] == delim {
      fields := fields + [cur];
      cur := "";
    } else {
      cur := cur + [input[i]];
    }
    i := i + 1;
  }
  fields := fields + [cur];
}

method Parse(input: string) returns (rows: seq<seq<string>>)
  requires |input| > 0
  ensures |rows| >= 0
  decreases |input|
{
  var fields := ParseLine(input, ",", "\"");
  rows := [fields];
}