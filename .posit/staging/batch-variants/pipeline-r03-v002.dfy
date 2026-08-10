datatype Result<T> = Success(value: T) | Failure(error: string)
datatype Entity = Record(id: int, data: string)

method Parse(input: string, log: seq<string>) returns (res: Result<string>, newLog: seq<string>)
  requires |input| > 0
  ensures |newLog| == |log| + 1
  ensures res.Success? ==> |res.value| > 0
{
  res := Success(input);
  newLog := log + ["parse"];
}

method Transform(data: string, id: int, log: seq<string>) returns (e: Entity, newLog: seq<string>)
  requires |data| > 0
  ensures |newLog| == |log| + 1
  ensures e.id == id
{
  e := Record(id, data);
  newLog := log + ["transform"];
}

method Store(e: Entity, store: seq<Entity>, log: seq<string>) returns (res: Result<seq<Entity>>, newLog: seq<string>)
  ensures |newLog| == |log| + 1
  ensures res.Success? ==> |res.value| == |store| + 1
{
  res := Success(store + [e]);
  newLog := log + ["store"];
}

method RunPipeline(input: string, id: int, store: seq<Entity>, log: seq<string>) returns (res: Result<seq<Entity>>, newLog: seq<string>)
  requires |input| > 0
  ensures |newLog| >= |log|
{
  var p, l1 := Parse(input, log);
  if p.Failure? {
    res := Failure(p.error);
    newLog := l1;
    return;
  }
  var e, l2 := Transform(p.value, id, l1);
  res, newLog := Store(e, store, l2);
}