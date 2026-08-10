datatype Result<T> = Success(value: T) | Failure(error: string)

datatype Entity = Record(id: int, command: string, token: string)

function GetDelim(d: string): char
  requires |d| == 1
{ d[0] }

method ParseInput(input: string, d: string) returns (fields: seq<string>)
  requires |input| > 0
  requires |d| == 1
  ensures |fields| >= 1
{
  var delim := GetDelim(d);
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

method ValidateFields(fields: seq<string>, minF: int, maxF: int) returns (ok: bool, msg: string)
  requires minF >= 0
  requires maxF >= minF
  ensures ok ==> minF <= |fields| <= maxF
  ensures !ok ==> |msg| > 0
{
  if |fields| < minF {
    ok := false; msg := "too few fields";
  } else if |fields| > maxF {
    ok := false; msg := "too many fields";
  } else {
    ok := true; msg := "";
  }
}

method CheckAuth(token: string) returns (ok: bool)
  ensures ok ==> |token| > 0
{
  ok := |token| > 0;
}

method HandleRequest(input: string, d: string, minF: int, maxF: int) returns (result: Result<Entity>)
  requires |input| > 0
  requires |d| == 1
  requires minF >= 2
  requires maxF >= minF
{
  var fields := ParseInput(input, d);
  var ok, msg := ValidateFields(fields, minF, maxF);
  if !ok {
    result := Failure(msg);
    return;
  }
  assert |fields| >= 2;
  var authOk := CheckAuth(fields[1]);
  if !authOk {
    result := Failure("auth failed");
    return;
  }
  result := Success(Record(1, fields[0], fields[1]));
}