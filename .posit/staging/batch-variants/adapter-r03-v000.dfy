datatype Result<T> = Success(value: T) | Failure(error: string)

datatype Json = JsonStr(v: string) | JsonInt(n: int) | JsonObj(d: string)
datatype Obj = Obj(id: int, payload: string)

method DoAdapt(j: Json) returns (o: Obj)
  ensures o.Obj?
{
  match j {
    case JsonStr(v) => { o := Obj(0, v); }
    case JsonInt(n) => { o := Obj(n, ""); }
    case JsonObj(d) => { o := Obj(0, d); }
  }
}

method Adapt(j: Json) returns (o: Obj)
  ensures o.Obj?
{
  o := DoAdapt(j);
}

method AdaptBatch(xs: seq<Json>) returns (ys: seq<Obj>)
  requires |xs| >= 0
  ensures |ys| == |xs|
  decreases |xs|
{
  if |xs| == 0 { ys := []; }
  else {
    var h := DoAdapt(xs[0]);
    var t := AdaptBatch(xs[1..]);
    ys := [h] + t;
  }
}

method AdaptRetry(j: Json, k: int) returns (r: Result<Obj>)
  requires k >= 0
  ensures r.Success? ==> r.value.Obj?
  decreases k
{
  if k == 0 { r := Failure("exhausted"); }
  else {
    var o := DoAdapt(j);
    if o.id < 0 { r := AdaptRetry(j, k - 1); } else { r := Success(o); }
  }
}

method Validate(j: Json) returns (r: Result<Json>)
  ensures r.Success? ==> r.value == j
  ensures r.Failure? ==> |r.error| > 0
{
  match j {
    case JsonStr(v) => {
      if |v| == 0 { r := Failure("empty"); } else { r := Success(j); }
    }
    case JsonInt(n) => { r := Success(j); }
    case JsonObj(d) => {
      if |d| == 0 { r := Failure("empty"); } else { r := Success(j); }
    }
  }
}

method CountValid(ys: seq<Obj>) returns (n: int)
  requires |ys| >= 0
  ensures 0 <= n <= |ys|
  decreases |ys|
{
  if |ys| == 0 { n := 0; }
  else {
    var c := CountValid(ys[1..]);
    if ys[0].payload == "" { n := c; } else { n := c + 1; }
  }
}