datatype Result<T> = Success(value: T) | Failure(error: string)

datatype Entity = Record(id: int, command: string, action: string, payload: string)

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

method LogStage(log: seq<string>, stage: string) returns (newLog: seq<string>)
  ensures |newLog| == |log| + 1
{
  newLog := log + [stage];
}

method HandleRequest(input: string, d: string, minF: int, maxF: int, nextId: int) returns (result: Result<Entity>, log: seq<string>)
  requires |input| > 0
  requires |d| == 1
  requires minF >= 2
  requires maxF >= minF
  ensures |log| >= 3
{
  log := [];
  var fields := ParseInput(input, d);
  log := LogStage(log, "parse");
  var ok, msg := ValidateFields(fields, minF, maxF);
  log := LogStage(log, "validate");
  if !ok {
    log := LogStage(log, "error");
    result := Failure(msg);
    return;
  }
  log := LogStage(log, "transform");
  var entity := TransformToEntity(fields, nextId);
  result := Success(entity);
}