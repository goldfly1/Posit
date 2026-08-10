datatype Result<T> = Success(value: T) | Failure(error: string)
datatype Entity = Record(id: int, data: string)

method Parse(input: string) returns (res: Result<string>)
  requires |input| > 0
  ensures res.Success? ==> |res.value| > 0
{
  res := Success(input);
}

method Validate(data: string) returns (res: Result<string>)
  requires |data| > 0
  ensures res.Failure? ==> res.error == "invalid"
  ensures res.Success? ==> |res.value| > 0
{
  if |data| > 10 { res := Failure("invalid"); } else { res := Success(data); }
}

method Auth(token: int) returns (res: Result<bool>)
  ensures res.Failure? ==> res.error == "unauthorized"
{
  if token == 123 { res := Success(true); } else { res := Failure("unauthorized"); }
}

method Transform(data: string, id: int) returns (e: Entity)
  requires |data| > 0
  ensures e.id == id
{
  e := Record(id, data);
}

method Store(e: Entity, store: seq<Entity>) returns (res: Result<seq<Entity>>)
{
  res := Success(store + [e]);
}

method Log(e: Entity, logs: seq<string>) returns (newLogs: seq<string>)
  ensures |newLogs| == |logs| + 1
{
  newLogs := logs + ["stored"];
}

method RunPipeline(input: string, token: int, id: int, store: seq<Entity>, logs: seq<string>) returns (res: Result<seq<Entity>>, newLogs: seq<string>)
  requires |input| > 0
  ensures |newLogs| >= |logs|
{
  var p := Parse(input);
  if p.Failure? { res := Failure(p.error); newLogs := logs; return; }
  var v := Validate(p.value);
  if v.Failure? { res := Failure(v.error); newLogs := logs; return; }
  var a := Auth(token);
  if a.Failure? { res := Failure(a.error); newLogs := logs; return; }
  var e := Transform(v.value, id);
  res := Store(e, store);
  if res.Success? {
    newLogs := Log(e, logs);
  } else {
    newLogs := logs;
  }
}