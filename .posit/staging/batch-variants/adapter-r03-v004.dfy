datatype Result<T> = Success(value: T) | Failure(error: string)

datatype Json = JsonStr(v: string) | JsonInt(n: int) | JsonObj(d: string)
datatype MapRep = MapRep(entries: seq<(string, string)>)

method DoAdapt(j: Json) returns (m: MapRep)
  ensures m.MapRep?
{
  match j {
    case JsonStr(v) => { m := MapRep([("value", v)]); }
    case JsonInt(n) => { m := MapRep([("num", "0")]); }
    case JsonObj(d) => { m := MapRep([("data", d)]); }
  }
}

method Adapt(j: Json) returns (m: MapRep)
  ensures m.MapRep?
{
  m := DoAdapt(j);
}

method AdaptValidated(j: Json) returns (r: Result<MapRep>)
  ensures r.Success? ==> r.value.MapRep?
  ensures r.Failure? ==> |r.error| > 0
{
  var v := Validate(j);
  if v.Success? {
    var m := DoAdapt(v.value);
    r := Success(m);
  } else { r := Failure(v.error); }
}

method AdaptBatch(xs: seq<Json>) returns (ys: seq<MapRep>)
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

method AdaptRetry(j: Json, k: int) returns (r: Result<MapRep>)
  requires k >= 0
  ensures r.Success? ==> r.value.MapRep?
  decreases k
{
  if k == 0 { r := Failure("exhausted"); }
  else {
    var m := DoAdapt(j);
    if |m.entries| == 0 { r := AdaptRetry(j, k - 1); } else { r := Success(m); }
  }
}

method Validate(j: Json) returns (r: Result<Json>)
  ensures r.Success? ==> r.value == j
  ensures r.Failure? ==> |r.error| > 0
{
  match j {
    case JsonStr(v) => {
      if |v| == 0 { r := Failure("empty str"); } else { r := Success(j); }
    }
    case JsonInt(n) => {
      if n < 0 { r := Failure("negative"); } else { r := Success(j); }
    }
    case JsonObj(d) => {
      if |d| == 0 { r := Failure("empty obj"); } else { r := Success(j); }
    }
  }
}

method CountValid(ys: seq<MapRep>) returns (n: int)
  requires |ys| >= 0
  ensures 0 <= n <= |ys|
  decreases |ys|
{
  if |ys| == 0 { n := 0; }
  else {
    var c := CountValid(ys[1..]);
    if |ys[0].entries| == 0 { n := c; } else { n := c + 1; }
  }
}