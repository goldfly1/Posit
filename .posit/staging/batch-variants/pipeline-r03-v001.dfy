datatype Result<T> = Success(value: T) | Failure(error: string)
datatype Entity = Record(id: int, data: string)

method Parse(input: string, token: int) returns (res: Result<string>)
  requires |input| > 0
  ensures res.Failure? ==> res.error == "unauthorized" || res.error == "invalid"
  ensures res.Success? ==> |res.value| > 0
{
  if token != 123 {
    res := Failure("unauthorized");
  } else if |input| == 0 {
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

method RunPipeline(input: string, token: int, id: int, store: seq<Entity>) returns (res: Result<seq<Entity>>)
  requires |input| > 0
{
  var p := Parse(input, token);
  if p.Failure? {
    res := Failure(p.error);
    return;
  }
  var e := Transform(p.value, id);
  res := Store(e, store);
}