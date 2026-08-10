datatype Entity = Record(id: int, data: string)
datatype Result<T> = Success(value: T) | Failure(error: string)

method AuthAndParse(token: string, input: string) returns (result: Result<seq<string>>)
  requires |input| > 0
  ensures result.Success? ==> |result.value| >= 1
  ensures result.Failure? ==> |result.error| > 0
{
  if |token| == 0 { result := Failure("auth failed"); }
  else { result := Success([input]); }
}

method TransformToEntity(fields: seq<string>, nextId: int) returns (entity: Entity)
  requires |fields| >= 1
  ensures entity.id == nextId
{ entity := Record(nextId, fields[0]); }

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
  var pr := AuthAndParse(token, input);
  if pr.Failure? { result := Failure(pr.error); return; }
  var entity := TransformToEntity(pr.value, nextId);
  result := StoreEntity(entities, entity);
}