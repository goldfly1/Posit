datatype Entity = Record(id: int, data: string)
datatype Result<T> = Success(value: T) | Failure(error: string)
datatype ValResult = Valid | Invalid(reason: string)
datatype ErrorCode = ErrAuth | ErrDup | ErrEmpty

function ErrMsg(code: ErrorCode): string
  decreases code
{
  match code
  case ErrAuth => "auth failed"
  case ErrDup => "duplicate id"
  case ErrEmpty => "empty field"
}

method Authenticate(token: string) returns (ok: bool)
  ensures ok ==> |token| > 0
  ensures !ok ==> true
{ ok := |token| > 0; }

method ParseInput(input: string) returns (fields: seq<string>)
  requires |input| > 0
  ensures |fields| >= 1
{ fields := [input]; }

method ValidateFields(fields: seq<string>) returns (vr: ValResult)
  requires |fields| >= 1
  ensures vr.Valid? ==> |fields[0]| > 0
  ensures vr.Invalid? ==> |vr.reason| > 0
{ if |fields[0]| == 0 { vr := Invalid(ErrMsg(ErrEmpty)); } else { vr := Valid; } }

method TransformToEntity(fields: seq<string>, nextId: int) returns (entity: Entity)
  requires |fields| >= 1
  requires |fields[0]| > 0
  ensures entity.id == nextId
{ entity := Record(nextId, fields[0]); }

predicate NoDupIds(entities: seq<Entity>)
{ forall i, j :: 0 <= i < j < |entities| ==> entities[i].id != entities[j].id }

method StoreEntity(entities: seq<Entity>, entity: Entity) returns (result: Result<seq<Entity>>)
  requires NoDupIds(entities)
  ensures result.Success? ==> NoDupIds(result.value)
  ensures result.Success? ==> |result.value| == |entities| + 1
  ensures result.Failure? ==> result.error == ErrMsg(ErrDup)
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
  if found { result := Failure(ErrMsg(ErrDup)); }
  else { result := Success(entities + [entity]); }
}

method HandleRequest(token: string, input: string, entities: seq<Entity>, nextId: int) returns (result: Result<seq<Entity>>)
  requires |input| > 0
  requires NoDupIds(entities)
  ensures result.Success? ==> NoDupIds(result.value)
{
  var ok := Authenticate(token);
  if !ok { result := Failure(ErrMsg(ErrAuth)); return; }
  var fields := ParseInput(input);
  var vr := ValidateFields(fields);
  if vr.Invalid? { result := Failure(vr.reason); return; }
  var entity := TransformToEntity(fields, nextId);
  result := StoreEntity(entities, entity);
}