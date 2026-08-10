datatype Result<T> = Success(value: T) | Failure(error: string)
datatype Ctx = Ctx(input: string, fields: seq<string>)

method Split(s: string, d: char) returns (p: seq<string>)
  requires |s| > 0
  ensures |p| >= 1
{
  p := [];
  var c := "";
  var i := 0;
  while i < |s|
    invariant 0 <= i <= |s|
    invariant |p| >= 0
    decreases |s| - i
  {
    if s[i] == d {
      p := p + [c];
      c := "";
    } else {
      c := c + [s[i]];
    }
    i := i + 1;
  }
  p := p + [c];
}

method Parse(c: Ctx) returns (r: Result<Ctx>)
  requires |c.input| > 0
  ensures r.Success? ==> |r.value.fields| >= 1
{
  var f := Split(c.input, '|');
  r := Success(Ctx(c.input, f));
}

method Validate(c: Ctx) returns (r: Result<Ctx>)
  requires |c.fields| >= 1
  ensures r.Success? ==> |r.value.fields| >= 1
  ensures r.Failure? ==> |r.error| > 0
{
  if |c.fields[0]| == 0 {
    r := Failure("empty command");
  } else {
    r := Success(c);
  }
}

method Auth(c: Ctx) returns (r: Result<Ctx>)
  requires |c.fields| >= 1
  ensures r.Success? ==> |r.value.fields| >= 1
  ensures r.Failure? ==> |r.error| > 0
{
  if |c.fields| < 2 {
    r := Failure("no token");
  } else {
    r := Success(c);
  }
}

method Store(c: Ctx) returns (r: Result<Ctx>)
  requires |c.fields| >= 1
  ensures r.Success? ==> |r.value.fields| >= 1
{
  r := Success(c);
}

method Run(input: string) returns (r: Result<Ctx>)
  requires |input| > 0
  ensures r.Success? ==> |r.value.fields| >= 1
{
  var c := Ctx(input, []);
  var p := Parse(c);
  if p.Failure? {
    r := p;
    return;
  }
  var v := Validate(p.value);
  if v.Failure? {
    r := v;
    return;
  }
  var a := Auth(v.value);
  if a.Failure? {
    r := a;
    return;
  }
  r := Store(a.value);
}