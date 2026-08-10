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

method Transform(data: string, id: int, log: seq<string>) returns (e: Entity, l: seq<string>)
  requires |data| > 0
  ensures |l| == |log| + 1
  ensures e.id == id
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

method RunPipeline(input: string, id: int, store: seq<Entity>, log: seq<string>) returns (res: Result<seq<Entity>>, newLog: seq<string>)
  requires |input| > 0
  ensures |newLog| >= |log|
{
  var p, l1 := Parse(input, log);
  if p.Failure? { res := Failure(p.error); newLog := l1; return; }
  var v, l2 := Validate(p.value, l1);
  if v.Failure? { res := Failure(v.error); newLog := l2; return; }
  var e, l3 := Transform(v.value, id, l2);
  res, newLog := Store(e, store, l3);
}