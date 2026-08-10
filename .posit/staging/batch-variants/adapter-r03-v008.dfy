datatype Result<T> = Success(value: T) | Failure(error: string)

datatype Json = JsonStr(v: string) | JsonInt(n: int) | JsonObj(d: string)
datatype Rec = Rec(id: int, fields: seq<string>)

method DoAdapt(j: Json) returns (r: Rec)
  ensures r.Rec?
{
  match j {
    case JsonStr(v) => { r := Rec(0, [v]); }
    case JsonInt(n) => { r := Rec(n, []); }
    case JsonObj(d) => { r := Rec(0, [d]); }
  }
}

method Adapt(j: Json) returns (r: Result<Rec>)
  ensures r.Success? ==> r.value.Rec?
  ensures r.Failure? ==> |r.error| > 0
{
  var rec := DoAdapt(j);
  if |rec.fields| == 0 { r := Failure("no fields"); } else { r := Success(rec); }
}

method AdaptBatch(xs: seq<Json>) returns (ys: seq<Rec>)
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

method AdaptRetry(j: Json, k: int) returns (r: Result<Rec>)
  requires k >= 0
  ensures r.Success? ==> r.value.Rec?
  decreases k
{
  if k == 0 { r := Failure("exhausted"); }
  else {
    var rec := DoAdapt(j);
    if rec.id < 0 { r := AdaptRetry(j, k - 1); } else { r := Success(rec); }
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

method CountValid(ys: seq<Rec>) returns (n: int)
  requires |ys| >= 0
  ensures 0 <= n <= |ys|
  decreases |ys|
{
  if |ys| == 0 { n := 0; }
  else {
    var c := CountValid(ys[1..]);
    if |ys[0].fields| == 0 { n := c; } else { n := c + 1; }
  }
}