datatype Result<T> = Success(value: T) | Failure(error: string)

datatype Csv = CsvRecord(cells: seq<string>)
datatype Obj = Obj(id: int, payload: string)

method DoAdapt(c: Csv) returns (o: Obj)
  ensures o.Obj?
{
  match c {
    case CsvRecord(cells) => {
      if |cells| > 0 { o := Obj(0, cells[0]); } else { o := Obj(0, ""); }
    }
  }
}

method Adapt(c: Csv) returns (o: Obj)
  ensures o.Obj?
{
  o := DoAdapt(c);
}

method AdaptValidated(c: Csv) returns (r: Result<Obj>)
  ensures r.Success? ==> r.value.Obj?
  ensures r.Failure? ==> |r.error| > 0
{
  var v := Validate(c);
  if v.Success? {
    var o := DoAdapt(v.value);
    r := Success(o);
  } else { r := Failure(v.error); }
}

method AdaptBatch(xs: seq<Csv>) returns (ys: seq<Obj>)
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

method AdaptRetry(c: Csv, k: int) returns (r: Result<Obj>)
  requires k >= 0
  ensures r.Success? ==> r.value.Obj?
  decreases k
{
  if k == 0 { r := Failure("exhausted"); }
  else {
    var o := DoAdapt(c);
    if o.id < 0 { r := AdaptRetry(c, k - 1); } else { r := Success(o); }
  }
}

method Validate(c: Csv) returns (r: Result<Csv>)
  ensures r.Success? ==> r.value == c
  ensures r.Failure? ==> |r.error| > 0
{
  match c {
    case CsvRecord(cells) => {
      if |cells| == 0 {
        r := Failure("no cells");
      } else if |cells[0]| == 0 {
        r := Failure("empty first cell");
      } else {
        r := Success(c);
      }
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