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

method Auth(token: int) returns (res: Result<bool>)
  ensures res.Failure? ==> res.error == "auth error"
{
  if token != 123 { res := Failure("auth error"); } else { res := Success(true); }
}

method Transform(data: string, id: int) returns (res: Result<Entity>)
  requires |data| > 0
  ensures res.Failure? ==> res.error == "transform error"
{
  if |data| > 8 { res := Failure("transform error"); } else { res := Success(Record(id, data)); }
}

method StoreSize(e: Entity, store: seq<Entity>) returns (res: Result<int>)
  ensures res.Success? ==> res.value == |store| + 1
{
  res := Success(|store| + 1);
}

method RunPipeline(input: string, token: int, id: int, store: seq<Entity>) returns (res: Result<int>)
  requires |input| > 0
{
  var p := Parse(input);
  if p.Failure? { res := Failure(p.error); return; }
  var v := Validate(p.value);
  if v.Failure? { res := Failure(v.error); return; }
  var a := Auth(token);
  if a.Failure? { res := Failure(a.error); return; }
  var t := Transform(v.value, id);
  if t.Failure? { res := Failure(t.error); return; }
  res := StoreSize(t.value, store);
}