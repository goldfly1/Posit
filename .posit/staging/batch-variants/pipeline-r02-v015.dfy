datatype Result<T> = Success(value: T) | Failure(error: string)

datatype Entity = Record(id: int, command: string, action: string, token: string, payload: string)

predicate NoDupIds(entities: seq<Entity>)
{
  forall i, j :: 0 <= i < j < |entities| ==> entities[i].id != entities[j].id
}

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

method TransformToEntity(fields: seq<string>, nextId: int) returns (entity: Entity)
  requires |fields| >= 3
  ensures entity.id == nextId
{
  var payload := if |fields| >= 4 then fields[3] else "";
  entity := Record(nextId, fields[0], fields[1], fields[2], payload);
}

method StoreEntity(entities: seq<Entity>, entity: Entity) returns (result: Result<seq<Entity>>)
  requires NoDupIds(entities)
  ensures result.Success? ==> NoDupIds(result.value)
  ensures result.Success? ==> |result.value| == |entities| + 1
{
  var i := 0;
  var found := false;
  while i < |entities|
    invariant 0 <= i <= |entities|
    invariant NoDupIds(entities)
    invariant !found ==> forall j :: 0 <= j < i ==> entities[j].id != entity.id
    decreases |entities| - i
  {
    if entities[i].id == entity.id {
      found := true;
    }
    i := i + 1;
  }
  if found {
    result := Failure("duplicate id");
  } else {
    result := Success(entities + [entity]);
  }
}

method HandleRequest(input: string, d: string, minF: int, maxF: int, entities: seq<Entity>, nextId: int) returns (result: Result<seq<Entity>>)
  requires |input| > 0
  requires |d| == 1
  requires minF >= 3
  requires maxF >= minF
  requires NoDupIds(entities)
  ensures result.Success? ==> NoDupIds(result.value)
{
  var fields := ParseInput(input, d);
  var ok, msg := ValidateFields(fields, minF, maxF);
  if !ok {
    result := Failure(msg);
    return;
  }
  assert |fields| >= 3;
  var authOk := CheckAuth(fields[2]);
  if !authOk {
    result := Failure("auth failed");
    return;
  }
  var entity := TransformToEntity(fields, nextId);
  result := StoreEntity(entities, entity);
}