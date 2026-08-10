datatype ErrorCode = ErrParse | ErrValidate | ErrAuth | ErrStore
datatype Outcome<T> = Ok(value: T) | Err(code: ErrorCode, msg: string)
datatype Ctx = Ctx(input: string, fields: seq<string>, authed: bool, log: seq<string>)

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

method Parse(c: Ctx) returns (r: Outcome<Ctx>)
  requires |c.input| > 0
  ensures r.Ok? ==> |r.value.fields| >= 1 && |r.value.log| == |c.log| + 1
  ensures r.Err? ==> r.code == ErrParse
{
  var f := Split(c.input, '|');
  r := Ok(Ctx(c.input, f, false, c.log + ["parse"]));
}

method Validate(c: Ctx) returns (r: Outcome<Ctx>)
  requires |c.fields| >= 1
  ensures r.Ok? ==> |r.value.fields| >= 2 && |r.value.log| == |c.log| + 1
  ensures r.Err? ==> r.code == ErrValidate
{
  if |c.fields| < 2 {
    r := Err(ErrValidate, "too few fields");
  } else if |c.fields[0]| == 0 {
    r := Err(ErrValidate, "empty command");
  } else {
    r := Ok(Ctx(c.input, c.fields, c.authed, c.log + ["validate"]));
  }
}

method Auth(c: Ctx) returns (r: Outcome<Ctx>)
  requires |c.fields| >= 2
  ensures r.Ok? ==> r.value.authed && |r.value.fields| >= 2 && |r.value.log| == |c.log| + 1
  ensures r.Err? ==> r.code == ErrAuth
{
  if c.fields[1] == "token" {
    r := Ok(Ctx(c.input, c.fields, true, c.log + ["auth"]));
  } else {
    r := Err(ErrAuth, "unauthorized");
  }
}

method Store(c: Ctx) returns (r: Outcome<Ctx>)
  requires c.authed && |c.fields| >= 2
  ensures r.Ok? ==> r.value.authed && |r.value.fields| >= 2 && |r.value.log| == |c.log| + 1
  ensures r.Err? ==> r.code == ErrStore
{
  if c.fields[0] == "dup" {
    r := Err(ErrStore, "duplicate");
  } else {
    r := Ok(Ctx(c.input, c.fields, c.authed, c.log + ["store"]));
  }
}

method Run(input: string) returns (r: Outcome<Ctx>)
  requires |input| > 0
  ensures r.Ok? ==> r.value.authed && |r.value.fields| >= 2 && |r.value.log| == 4
  ensures r.Err? ==> r.code in {ErrParse, ErrValidate, ErrAuth, ErrStore}
{
  var c := Ctx(input, [], false, []);
  var p := Parse(c);
  if p.Err? {
    r := p;
    return;
  }
  var v := Validate(p.value);
  if v.Err? {
    r := v;
    return;
  }
  var a := Auth(v.value);
  if a.Err? {
    r := a;
    return;
  }
  r := Store(a.value);
}