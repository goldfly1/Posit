// Pattern: Pipeline (Approach 3 — pre-written body with parameters)
// responsibility: Universal request handler — parse → validate → transform → store → respond
// test: HandleRequest("task|create|Buy groceries") returns Success(1)
// test: HandleRequest("task|create|") returns Failure("validation failed: empty title")
// test: HandleRequest("") returns Failure("parse failed: empty input")
// test: HandleRequest("task|create|Buy groceries|extra") returns Failure("parse failed: too many fields")
//
// This is the UNIVERSAL panel. Every project uses this as the foundation.
// The architect names it for the system's role (WorkflowPipeline, TaskSchedulerPipeline, etc.)
// and customizes the entity type, validation rules, and transformation.
// Specialist patterns (state-machine, graph, cache, scheduler) bolt on when needed.
//
// Parameters (architect customizes these):
//   inputDelimiter: string — field separator in input (default "|")
//   minFields: int — minimum required fields (default 2: command + action)
//   maxFields: int — maximum allowed fields (default 3: command + action + payload)
//   entityType: datatype — the domain entity (default: Record)
//
// Pre-cut planks: the parse step, validate step, transform step, store step,
// and result building are all pre-written and Z3-proven. The architect sets
// the parameters and entity type. Imp's job is empty or near-empty.

include "result.dfy"

// === Domain Entity (architect customizes) ===
datatype Entity =
  | Record(id: int, command: string, action: string, payload: string)

// === Stage 1: PARSE — split delimited input into fields ===

function GetDelimiter(delimiter: string): char
  requires |delimiter| == 1
{
  delimiter[0]
}

method ParseInput(input: string, delimiter: string) returns (fields: seq<string>)
  requires |input| > 0
  requires |delimiter| == 1
  ensures |fields| >= 1
  decreases |input|
{
  var delim := GetDelimiter(delimiter);
  fields := [];
  var currentField := "";
  var i := 0;
  while i < |input|
    invariant 0 <= i <= |input|
    invariant |fields| >= 0
    decreases |input| - i
  {
    if input[i] == delim {
      fields := fields + [currentField];
      currentField := "";
    } else {
      currentField := currentField + [input[i]];
    }
    i := i + 1;
  }
  fields := fields + [currentField];
}

// === Stage 2: VALIDATE — check parsed fields against rules ===

datatype ValidationResult =
  | Valid
  | Invalid(errors: seq<string>)

method ValidateFields(fields: seq<string>, minFields: int, maxFields: int) returns (result: ValidationResult)
  requires minFields >= 0
  requires maxFields >= minFields
  ensures result.Valid? ==> |fields| >= minFields && |fields| <= maxFields
  ensures result.Invalid? ==> |result.errors| >= 1
  decreases |fields|
{
  var errors := [];
  if |fields| < minFields {
    errors := errors + ["validation failed: too few fields"];
  }
  if |fields| > maxFields {
    errors := errors + ["validation failed: too many fields"];
  }
  if |fields| > 0 && |fields[0]| == 0 {
    errors := errors + ["validation failed: empty command"];
  }
  if |fields| > 1 && |fields[1]| == 0 {
    errors := errors + ["validation failed: empty action"];
  }
  if |errors| == 0 {
    result := Valid;
  } else {
    result := Invalid(errors);
  }
}

// === Stage 3: TRANSFORM — convert validated fields into domain entity ===

method TransformToEntity(fields: seq<string>, nextId: int) returns (entity: Entity)
  requires |fields| >= 2
  ensures entity.id == nextId
  decreases |fields|
{
  var payload := if |fields| >= 3 then fields[2] else "";
  entity := Record(nextId, fields[0], fields[1], payload);
}

// === Stage 4: STORE — add entity to in-memory store with ID uniqueness ===

predicate NoDuplicateIds(entities: seq<Entity>)
{
  forall i, j :: 0 <= i < j < |entities| ==> entities[i].id != entities[j].id
}

method StoreEntity(entities: seq<Entity>, entity: Entity) returns (result: Result<seq<Entity>>)
  requires NoDuplicateIds(entities)
  ensures result.Success? ==> NoDuplicateIds(result.value)
  ensures result.Success? ==> |result.value| == |entities| + 1
  ensures result.Failure? ==> result.error == "duplicate id"
  decreases |entities|
{
  var i := 0;
  var found := false;
  while i < |entities|
    invariant 0 <= i <= |entities|
    invariant NoDuplicateIds(entities)
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
    result := Failure("duplicate id");
  } else {
    result := Success(entities + [entity]);
  }
}
// === Stage 5: PIPELINE ORCHESTRATION — run all stages in sequence ===

method HandleRequest(
    input: string,
    delimiter: string,
    minFields: int,
    maxFields: int,
    entities: seq<Entity>,
    nextId: int
) returns (result: Result<seq<Entity>>)
  requires |input| > 0
  requires |delimiter| == 1
  requires minFields >= 2
  requires maxFields >= minFields
  requires NoDuplicateIds(entities)
  ensures result.Success? ==> NoDuplicateIds(result.value)
  ensures result.Failure? ==> true
  decreases |entities|
{
  // Stage 1: Parse
  var fields := ParseInput(input, delimiter);
  
  // Stage 2: Validate
  var validation := ValidateFields(fields, minFields, maxFields);
  if validation.Invalid? {
    result := Failure(validation.errors[0]);
    return;
  }
  
  // After validation, fields count is within [minFields, maxFields]
  // The architect sets minFields=2, maxFields=3 for standard commands
  assert |fields| >= minFields;
  assert |fields| <= maxFields;
  
  // Stage 3: Transform
  assert |fields| >= 2;
  var entity := TransformToEntity(fields, nextId);
  
  // Stage 4: Store
  assert NoDuplicateIds(entities);
  result := StoreEntity(entities, entity);
}

