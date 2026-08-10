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
  ensures v.Valid? ==> |fields| >= 1 && |fields| <= 3 && 0 < |fields[0]| <= maxLen
  ensures v.Invalid? ==> |v.msg| > 0
  decreases |fields|
{
  if |fields| > 3 { v := Invalid("too many fields"); }
  else if |fields[0]| == 0 { v := Invalid("empty payload"); }
  else if |fields[0]| > maxLen { v := Invalid("payload too long"); }
  else { v := Valid; }
}

method Transform(fields: seq<string>, nextId: int) returns (e: Entity)
  requires |fields| >= 1 && |fields[0]| > 0
  ensures e.id == nextId
  decreases |fields|
{ e := Record(nextId, fields[0]); }

predicate NoDup(es: seq<Entity>)
{ forall i, j :: 0 <= i < j < |es| ==> es[i].id != es[j].id }

method Store(es: seq<Entity>, e: Entity) returns (r: Result<seq<Entity>>)
  requires NoDup(es)
  ensures r.Success? ==> NoDup(r.value) && |r.value| == |es| + 1
  ensures r.Failure? ==> r.code == StoreErr
{
  var hasDup := false;
  var i := 0;
  while i < |es|
    invariant 0 <= i <= |es|
    invariant !hasDup ==> forall j :: 0 <= j < i ==> es[j].id != e.id
    decreases |es| - i
  {
    if es[i].id == e.id { hasDup := true; }
    i := i + 1;
  }
  if hasDup {
    r := Failure(StoreErr, "duplicate id");
  } else {
    r := Success(es + [e]);
  }
}