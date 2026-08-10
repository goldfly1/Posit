datatype ErrorCode = Ok | AuthErr | ValidationErr | StoreErr
datatype Result<T> = Success(value: T) | Failure(code: ErrorCode, msg: string)
datatype Entity = Record(id: int, payload: string)

function GetDelim(d: string): char
  requires |d| == 1
{ d[0] }

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

predicate NoDup(es: seq<Entity>)
{ forall i, j :: 0 <= i < j < |es| ==> es[i].id != es[j].id }

method Store(es: seq<Entity>, e: Entity) returns (r: Result<seq<Entity>>)
  requires NoDup(es)
  ensures r.Success? ==> NoDup(r.value) && |r.value| == |es| + 1
  ensures r.Failure? ==> r.code == StoreErr
  decreases |es|
{
  var i := 0; var found := false;
  while i < |es|
    invariant 0 <= i <= |es| && NoDup(es)
    invariant !found ==> forall j :: 0 <= j < i ==> es[j].id != e.id
    decreases |es| - i
  {
    if es[i].id == e.id { found := true; }
    i := i + 1;
  }
  if found { r := Failure(StoreErr, "duplicate id"); } else { r := Success(es + [e]); }
}

method ValidatePayload(payload: string) returns (ok: bool)
  ensures ok ==> |payload| > 0
  ensures !ok ==> |payload| == 0
{ ok := |payload| > 0; }

method CheckToken(token: string) returns (ok: bool)
  ensures ok ==> |token| > 0
  ensures !ok ==> |token| == 0
{ ok := |token| > 0; }

method Handle(input: string, d: string, token: string, es: seq<Entity>, nextId: int)
  returns (r: Result<seq<Entity>>, log: seq<string>)
  requires |input| > 0 && |d| == 1 && NoDup(es)
  ensures r.Success? ==> NoDup(r.value)
  ensures r.Failure? ==> r.code != Ok
  ensures |log| >= 1
  decreases |es|
{
  log := [];
  if !CheckToken(token) { log := log + ["auth failed"]; r := Failure(AuthErr, "auth failed"); return; }
  log := log + ["auth ok"];
  var f := Parse(input, d);
  log := log + ["parsed"];
  if !ValidatePayload(f[0]) { log := log + ["validation failed"]; r := Failure(ValidationErr, "empty payload"); return; }
  log := log + ["validation ok"];
  var e := Record(nextId, f[0]);
  var stored := Store(es, e);
  log := log + ["stored"];
  r := stored;
}