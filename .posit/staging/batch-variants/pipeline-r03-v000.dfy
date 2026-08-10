datatype Result<T> = Success(value: T) | Failure(error: string)
datatype Entity = Record(id: int, data: string)

method Parse(input: string) returns (res: Result<string>)
  requires |input| > 0
  ensures res.Failure? ==> res.error == "invalid"
  ensures res.Success? ==> |res.value| > 0 && |res.value| <= 10
{
  if |input| > 10 {
    res := Failure("invalid");
  } else {
    res := Success(input);
  }
}

method Transform(data: string, id: int) returns (e: Entity)
  requires |data| > 0
  ensures e.id == id
{
  e := Record(id, data);
}

method Store(e: Entity, store: seq<Entity>) returns (res: Result<seq<Entity>>)
  ensures res.Success? ==> |res.value| == |store| + 1
{
  res := Success(store + [e]);
}

method RunPipeline(input: string, id: int, store: seq<Entity>) returns (res: Result<seq<Entity>>)
  requires |input| > 0
{
  var p := Parse(input);
  if p.Failure? {
    res := Failure(p.error);
    return;
  }
  var e := Transform(p.value, id);
  res := Store(e, store);
}