datatype Result<T> = Success(value: T) | Failure(error: string)

datatype Entity = Record(id: int, command: string, action: string, payload: string)

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

method TransformToEntity(fields: seq<string>, nextId: int) returns (entity: Entity)
  requires |fields| >= 2
  ensures entity.id == nextId
{
  var payload := if |fields| >= 3 then fields[2] else "";
  entity := Record(nextId, fields[0], fields[1], payload);
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
    invariant found ==> (exists j :: 0 <= j < |entities| && entities[j].id == entity.id)
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

method HandleRequest(input: string, d: string, minF: int, maxF: int, nextId: int, entities: seq<Entity>) returns (result: Result<Entity>, newEntities: seq<Entity>, log: seq<string>)
  requires |input| > 0
  requires |d| == 1
  requires minF >= 2
  requires maxF >= minF
  requires NoDupIds(entities)
  ensures |log| >= 3
  ensures result.Success? ==> NoDupIds(newEntities)
{
  log := [];
  var fields := ParseInput(input, d);
  log := log + ["parse"];
  var ok, msg := ValidateFields(fields, minF, maxF);
  log := log + ["validate"];
  if !ok {
    log := log + ["error"];
    result := Failure(msg);
    newEntities := entities;
    return;
  }
  var entity := TransformToEntity(fields, nextId);
  var sresult := StoreEntity(entities, entity);
  log := log + ["store"];
  if sresult.Success? {
    result := Success(entity);
    newEntities := sresult.value;
  } else {
    result := Failure(sresult.error);
    newEntities := entities;
  }
}