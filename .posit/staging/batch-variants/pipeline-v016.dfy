datatype Result<T> = Success(value: T) | Failure(error: string)
datatype Entity = Record(id: int, payload: string)
datatype VRes = Valid | Invalid(msg: string)

function GetDelim(d: string): char
  requires |d| == 1
{ d[0] }

method Auth(token: string) returns (ok: bool)
  ensures ok ==> |token| > 0
  ensures !ok ==> |token| == 0
{ ok := |token| > 0; }

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
  ensures r.Failure? ==> r.error == "duplicate id"
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
  if found { r := Failure("duplicate id"); } else { r := Success(es + [e]); }
}

method Respond(r: Result<seq<Entity>>) returns (o: Result<seq<Entity>>)
  ensures o.Success? == r.Success? && o.Failure? == r.Failure?
  ensures o.Success? ==> o.value == r.value
  ensures o.Failure? ==> o.error == r.error
{ o := r; }

method Handle(input: string, d: string, token: string, maxLen: int, es: seq<Entity>, nextId: int) returns (r: Result<seq<Entity>>)
  requires |input| > 0 && |d| == 1 && maxLen >= 1 && NoDup(es)
  ensures r.Success? ==> NoDup(r.value)
  decreases |es|
{
  if !Auth(token) { r := Failure("auth failed"); return; }
  var f := Parse(input, d);
  var v := Validate(f, maxLen);
  if v.Invalid? { r := Failure(v.msg); return; }
  var e := Transform(f, nextId);
  r := Respond(Store(es, e));
}