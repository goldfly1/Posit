datatype Result<T> = Success(value: T) | Failure(error: string)
datatype Entity = Record(id: int, data: string)

method Parse(input: string) returns (res: Result<string>)
  requires |input| > 0
  ensures res.Success? ==> |res.value| == |input|
  ensures res.Success? ==> |res.value| > 0
{
  res := Success(input);
}

method Validate(data: string) returns (res: Result<string>)
  requires |data| > 0
  ensures res.Failure? ==> res.error == "invalid"
  ensures res.Success? ==> |res.value| <= 10
  ensures res.Success? ==> |res.value| > 0
{
  if |data| > 10 {
    res := Failure("invalid");
  } else {
    res := Success(data);
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
  if p.Failure? { res := Failure(p.error); return; }
  var v := Validate(p.value);
  if v.Failure? { res := Failure(v.error); return; }
  var e := Transform(v.value, id);
  res := Store(e, store);
}