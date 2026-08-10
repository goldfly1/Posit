datatype Result<T> = Success(value: T) | Failure(error: string)

datatype Xml = XmlElem(tag: string, text: string) | XmlAttr(k: string, v: string)
datatype MapRep = MapRep(entries: seq<(string, string)>)

method DoAdapt(x: Xml) returns (m: MapRep)
  ensures m.MapRep?
{
  match x {
    case XmlElem(t, tx) => { m := MapRep([(t, tx)]); }
    case XmlAttr(k, v) => { m := MapRep([(k, v)]); }
  }
}

method Adapt(x: Xml) returns (r: Result<MapRep>)
  ensures r.Success? ==> r.value.MapRep?
  ensures r.Failure? ==> |r.error| > 0
{
  var m := DoAdapt(x);
  if |m.entries| == 0 { r := Failure("no entries"); } else { r := Success(m); }
}

method AdaptBatch(xs: seq<Xml>) returns (ys: seq<MapRep>)
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

method AdaptRetry(x: Xml, k: int) returns (r: Result<MapRep>)
  requires k >= 0
  ensures r.Success? ==> r.value.MapRep?
  decreases k
{
  if k == 0 { r := Failure("exhausted"); }
  else {
    var m := DoAdapt(x);
    if |m.entries| == 0 { r := AdaptRetry(x, k - 1); } else { r := Success(m); }
  }
}

method Validate(x: Xml) returns (r: Result<Xml>)
  ensures r.Success? ==> r.value == x
  ensures r.Failure? ==> |r.error| > 0
{
  match x {
    case XmlElem(t, tx) => {
      if |t| == 0 { r := Failure("empty tag"); } else { r := Success(x); }
    }
    case XmlAttr(k, v) => {
      if |k| == 0 { r := Failure("empty key"); } else { r := Success(x); }
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