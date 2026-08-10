datatype Entity = Record(id: int, data: string)
datatype Result<T> = Success(value: T) | Failure(error: string)
datatype ValResult = Valid | Invalid(reason: string)
datatype LogEntry = LogEntry(id: int, msg: string)

method Authenticate(token: string) returns (ok: bool)
  ensures ok ==> |token| > 0
{ ok := |token| > 0; }

method ParseInput(input: string) returns (fields: seq<string>)
  requires |input| > 0
  ensures |fields| >= 1
{ fields := [input]; }

method ValidateFields(fields: seq<string>) returns (vr: ValResult)
  requires |fields| >= 1
  ensures vr.Valid? ==> |fields[0]| > 0
  ensures vr.Invalid? ==> |vr.reason| > 0
{ if |fields[0]| == 0 { vr := Invalid("empty"); } else { vr := Valid; } }

method TransformToEntity(fields: seq<string>, nextId: int) returns (entity: Entity)
  requires |fields| >= 1
  requires |fields[0]| > 0
  ensures entity.id == nextId
{ entity := Record(nextId, fields[0]); }

method LogEntity(entity: Entity) returns (entry: LogEntry)
  ensures entry.id == entity.id
{ entry := LogEntry(entity.id, entity.data); }

predicate NoDupIds(entities: seq<Entity>)
{ forall i, j :: 0 <= i < j < |entities| ==> entities[i].id != entities[j].id }

method StoreEntity(entities: seq<Entity>, entity: Entity) returns (result: Result<seq<Entity>>)
  requires NoDupIds(entities)
  ensures result.Success? ==> NoDupIds(result.value)
  ensures result.Success? ==> |result.value| == |entities| + 1
{
  var i := 0; var found := false;
  while i < |entities|
    invariant 0 <= i <= |entities| invariant NoDupIds(entities)
    invariant found ==> exists j :: 0 <= j < |entities| && entities[j].id == entity.id
    invariant !found ==> forall j :: 0 <= j < i ==> entities[j].id != entity.id
    decreases |entities| - i
  {
    if entities[i].id == entity.id { found := true; }
    i := i + 1;
  }
  if found { result := Failure("duplicate id"); }
  else { result := Success(entities + [entity]); }
}

method HandleRequest(token: string, input: string, entities: seq<Entity>, nextId: int) returns (result: Result<seq<Entity>>)
  requires |input| > 0
  requires NoDupIds(entities)
  ensures result.Success? ==> NoDupIds(result.value)
{
  var ok := Authenticate(token);
  if !ok { result := Failure("auth failed"); return; }
  var fields := ParseInput(input);
  var vr := ValidateFields(fields);
  if vr.Invalid? { result := Failure(vr.reason); return; }
  var entity := TransformToEntity(fields, nextId);
  result := StoreEntity(entities, entity);
  if result.Success? { var log := LogEntity(entity); }
}