```dafny
datatype ErrorCode = Ok | AuthErr | ValidationErr | StoreErr
datatype Result<T> = Success(value: T) | Failure(code: ErrorCode, msg: string)
datatype Entity = Record(id: int, payload: string)
datatype VRes = Valid | Invalid(msg: string)

function GetDelim(d: string): char
  requires |d| == 1
{ d[0] }

method Auth(token: string) returns (ok: bool, principal: string)
  ensures ok ==> |token| > 0 && |principal| > 0
  ensures !ok ==> |principal| == 0
{
  if |token| > 0 { ok := true; principal := "user"; }
  else { ok := false; principal := ""; }
}

method Parse(input: string, d: string) returns (fields: seq<string>)
  requires |input| > 0 && |d| == 1
  ensures |fields| >= 1
  decreases |input|
{
  var delim := GetDelim(d);
  fields := []; var cur := ""; var i := 0;
  while i < |input|
    invariant 0 <= i <= |input| && |fields| >= 0
    decreases |input| - i
  {
    if input[i] == delim { fields := fields + [cur]; cur := ""; }
    else { cur := cur + [input[i]]; }
    i := i + 1;
  }
  fields := fields + [cur];
}

method Validate(fields: seq<string>, maxLen: int) returns (v: VRes)
  requires |fields| >= 1 && maxLen >= 1
  ensures v.Valid? ==> |fields| >= 1 && |fields| <=