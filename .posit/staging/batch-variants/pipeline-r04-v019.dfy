datatype Entity = Record(id: int, data: string)
datatype Result<T> = Success(value: T) | Failure(error: string)
datatype ValResult = Valid | Invalid(reason: string)
datatype ErrorCode = ErrAuth | ErrDup | ErrEmpty | ErrUnknown
datatype ErrorReport = ErrorReport(code: ErrorCode, msg: string)

function ErrMsg(code: ErrorCode): string
  decreases code
{
  match code
  case ErrAuth => "auth failed"
  case ErrDup => "duplicate id"
  case ErrEmpty => "empty field"
  case ErrUnknown => "unknown"
}

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
{ if |fields[0]| == 0 { vr := Invalid(ErrMsg(ErrEmpty)); } else { vr := Valid; } }

method TransformToEntity(fields: seq<string>, nextId: int) returns (entity: Entity)
  requires |fields| >= 1
  requires |fields[0]| > 0
  ensures entity.id == nextId
{ entity := Record(nextId, fields[0]); }

function MapError(code: ErrorCode): ErrorReport
  ensures MapError(code).code == code
  ensures MapError(code).msg == ErrMsg(code)
{
  ErrorReport(code, ErrMsg(code))
}

predicate NoDupIds(entities: seq<Entity>)
{ forall i, j :: 0 <= i < j < |entities| ==> entities[i].id != entities[j].id }

method StoreEntity(entities: seq<Entity>, entity: Entity) returns (result: Result<seq<Entity>>)
  requires NoDupIds(entities)
  ensures result.Success? ==> NoDupIds(result.value)
  ensures result.Success? ==> |result.value| == |entities| + 1
  ensures result.Failure? ==> |result.error| > 0
{
  var i := 0; var found := false;
  while i < |entities|
    invariant 0 <= i <= |entities|
    invariant NoDupIds(entities)
    invariant found ==> exists j :: 0 <= j < |entities| && entities[j].id == entity.id
    invariant !found ==> forall j :: 0 <= j < i ==> entities[j].id != entity.id
    decreases |entities| - i
  {
    if entities[i].id == entity.id {
      found := true;
    }
    i := i + 1;
  }
  if found {
    result := Failure(ErrMsg(ErrDup));
  } else {
    result := Success(entities + [entity]);
  }
}

method Pipeline(token: string, input: string, nextId: int, entities: seq<Entity>) returns (report: Result<ErrorReport>)
  requires NoDupIds(entities)
  requires |input| > 0
  ensures report.Failure? ==> |report.error| > 0
{
  var ok := Authenticate(token);
  if !ok {
    report := Failure(ErrMsg(ErrAuth));
    return;
  }
  
  var fields := ParseInput(input);
  var vr := ValidateFields(fields);
  if vr.Invalid? {
    report := Failure(vr.reason);
    return;
  }
  
  var entity := TransformToEntity(fields, nextId);
  var storeResult := StoreEntity(entities, entity);
  if storeResult.Failure? {
    report := Failure(storeResult.error);
    return;
  }
  
  report := Success(MapError(ErrUnknown));
}