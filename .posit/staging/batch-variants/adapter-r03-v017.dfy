datatype Result<T> = Success(value: T) | Failure(error: string)

datatype Xml = XmlElem(tag: string, text: string) | XmlAttr(k: string, v: string)
datatype Rec = Rec(id: int, fields: seq<string>)

method DoAdapt(x: Xml) returns (r: Rec)
  ensures r.Rec?
{
  match x {
    case XmlElem(t, tx) => { r := Rec(0, [t, tx]); }
    case XmlAttr(k, v) => { r := Rec(0, [k, v]); }
  }
}

method Adapt(x: Xml) returns (r: Result<Rec>)
  ensures r.Success? ==> r.value.Rec?
  ensures r.Failure? ==> |r.error| > 0
{
  var rec := DoAdapt(x);
  if |rec.fields| == 0 { r := Failure("no fields"); } else { r := Success(rec); }
}

method AdaptBatch(xs: seq<Xml>) returns (ys: seq<Rec>)
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

method AdaptRetry(x: Xml, k: int) returns (r: Result<Rec>)
  requires k >= 0
  ensures r.Success? ==> r.value.Rec?
  decreases k
{
  if k == 0 { r := Failure("exhausted"); }
  else {
    var rec := DoAdapt(x);
    if rec.id < 0 { r := AdaptRetry(x, k - 1); } else { r := Success(rec); }
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