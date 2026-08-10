datatype Result<T> = Success(value: T) | Failure(error: string)
datatype Entity = Record(id: int, data: string)

method Parse(input: string, log: seq<string>) returns (res: Result<string>, l: seq<string>)
  requires |input| > 0
  ensures |l| == |log| + 1
  ensures res.Success? ==> |res.value| > 0
{
  res := Success(input);
  l := log + ["parse"];
}

method Validate(data: string, log: seq<string>) returns (res: Result<string>, l: seq<string>)
  requires |data| > 0
  ensures |l| == |log| + 1
  ensures res.Success? ==> |res.value| > 0
{
  if |data| > 10 { res := Failure("invalid"); } else { res := Success(data); }
  l := log + ["validate"];
}

method Auth(token: int, log: seq<string>) returns (res: Result<bool>, l: seq<string>)
  ensures |l| == |log| + 1
{
  if token == 123 { res := Success(true); } else { res := Failure("unauthorized"); }
  l := log + ["auth"];
}

method Transform(data: string, id: int, log: seq<string>) returns (e: Entity, l: seq<string>)
  requires |data| > 0
  ensures |l| == |log| + 1
{
  e := Record(id, data);
  l := log + ["transform"];
}

method Store(e: Entity, store: seq<Entity>, log: seq<string>) returns (res: Result<seq<Entity>>, l: seq<string>)
  ensures |l| == |log| + 1
{
  res := Success(store + [e]);
  l := log + ["store"];
}

method Log(msg: string, logs: seq<string>) returns (newLogs: seq<string>)
  ensures |newLogs| == |logs| + 1
{
  newLogs := logs + [msg];
}

method RunPipeline(input: string, token: int, id: int, store: seq<Entity>, logs: seq<string>) returns (res: Result<seq<Entity>>, newLog: seq<string>)
  requires |input| > 0
  ensures |newLog| >= |logs|
{
  var p, l1 := Parse(input, logs);
  if p.Failure? { res := Failure(p.error); newLog := l1; return; }
  var v, l2 := Validate(p.value, l1);
  if v.Failure? { res := Failure(v.error); newLog := l2; return; }
  var a, l3 := Auth(token, l2);
  if a.Failure? { res := Failure(a.error); newLog := l3; return; }
  var e, l4 := Transform(v.value, id, l3);
  res, newLog := Store(e, store, l4);
  if res.Success? {
    newLog := Log("completed", newLog);
  }
}