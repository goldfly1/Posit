datatype Result<T> = Success(value: T) | Failure(error: string)
datatype Entity = Record(id: int, data: string)

method Parse(input: string) returns (res: Result<string>)
  requires |input| > 0
  ensures res.Failure? ==> res.error == "parse error"
  ensures res.Success? ==> |res.value| > 0
{
  if |input| > 10 { res := Failure("parse error"); } else { res := Success(input); }
}

method Validate(data: string) returns (res: Result<string>)
  requires |data| > 0
  ensures res.Failure? ==> res.error == "validation error"
  ensures res.Success? ==> |res.value| > 0
{
  if |data| < 2 { res := Failure("validation error"); } else { res := Success(data); }
}

method Transform(data: string, id: int) returns (res: Result<Entity>)
  requires |data| > 0
  ensures res.Failure? ==> res.error == "transform error"
{
  if |data| > 8 { res := Failure("transform error"); } else { res := Success(Record(id, data)); }
}

method Store(e: Entity, store: seq<Entity>) returns (res: Result<seq<Entity>>)
  ensures res.Failure? ==> res.error == "store error"
{
  if |store| > 5 { res := Failure("store error"); } else { res := Success(store + [e]); }
}

method RunPipeline(input: string, id: int, store: seq<Entity>) returns (res: Result<seq<Entity>>)
  requires |input| > 0
{
  var p := Parse(input);
  if p.Failure? { res := Failure(p.error); return; }
  var v := Validate(p.value);
  if v.Failure? { res := Failure(v.error); return; }
  var t := Transform(v.value, id);
  if t.Failure? { res := Failure(t.error); return; }
  res := Store(t.value, store);
}