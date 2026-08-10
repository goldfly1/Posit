datatype Result<T> = Success(value: T) | Failure(error: string)

datatype Xml = XmlElem(tag: string, text: string) | XmlAttr(k: string, v: string)
datatype Obj = Obj(id: int, payload: string)

method DoAdapt(x: Xml) returns (o: Obj)
  ensures o.Obj?
{
  match x {
    case XmlElem(t, tx) => { o := Obj(0, tx); }
    case XmlAttr(k, v) => { o := Obj(0, k + ":" + v); }
  }
}

method Adapt(x: Xml) returns (o: Obj)
  ensures o.Obj?
{
  o := DoAdapt(x);
}

method AdaptBatch(xs: seq<Xml>) returns (ys: seq<Obj>)
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

method AdaptRetry(x: Xml, k: int) returns (r: Result<Obj>)
  requires k >= 0
  ensures r.Success? ==> r.value.Obj?
  decreases k
{
  if k == 0 { r := Failure("exhausted"); }
  else {
    var o := DoAdapt(x);
    if o.id < 0 { r := AdaptRetry(x, k - 1); } else { r := Success(o); }
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