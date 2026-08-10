datatype Result<T> = Success(value: T) | Failure(error: string)
datatype Entity = Record(id: int, data: string)

method Parse(input: string) returns (res: Result<string>)
  requires |input| > 0
  ensures res.Failure? ==> res.error == "parse error"
  ensures res.Success? ==> |res.value| > 0
{
  if |input| > 10 {
    res := Failure("parse error");
  } else {
    res := Success(input);
  }
}

method Transform(data: string, id: int) returns (res: Result<Entity>)
  requires |data| > 0
  ensures res.Success? ==> res.value.id == id
  ensures res.Failure? ==> res.error == "transform error"
{
  if |data| < 2 {
    res := Failure("transform error");
  } else {
    res := Success(Record(id, data));
  }
}

method Store(e: Entity, store: seq<Entity>) returns (res: Result<seq<Entity>>)
  ensures res.Failure? ==> res.error == "store error"
{
  if |store| > 5 {
    res := Failure("store error");
  } else {
    res := Success(store + [e]);
  }
}

method RunPipeline(input: string, id: int, store: seq<Entity>) returns (res: Result<seq<Entity>>)
  requires |input| > 0
{
  var p := Parse(input);
  if p.Failure? {
    res := Failure(p.error);
    return;
  }
  var t := Transform(p.value, id);
  if t.Failure? {
    res := Failure(t.error);
    return;
  }
  res := Store(t.value, store);
}