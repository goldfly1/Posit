datatype Result<T> = Success(value: T) | Failure(error: string)
datatype Ctx = Ctx(input: string, fields: seq<string>, authed: bool, log: seq<string>, data: string, response: string)

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
  ensures r.Success? ==> |r.value.fields| >= 1 && |r.value.log| == |c.log| + 1
{
  var f := Split(c.input, '|');
  r := Success(Ctx(c.input, f, false, c.log + ["parse"], c.data, c.response));
}

method Validate(c: Ctx) returns (r: Result<Ctx>)
  requires |c.fields| >= 1
  ensures r.Success? ==> |c.fields| >= 2 && |r.value.log| == |c.log| + 1
  ensures r.Failure? ==> |r.error| > 0
{
  if |c.fields| < 2 then
    r := Failure("too few fields");
  else if |c.fields[0]| == 0 then
    r := Failure("empty command");
  else
    r := Success(Ctx(c.input, c.fields, c.authed, c.log + ["validate"], c.data, c.response));
}

method Auth(c: Ctx) returns (r: Result<Ctx>)
  requires |c.fields| >= 2
  ensures r.Success? ==> r.value.authed && |r.value.log| == |c.log| + 1
  ensures r.Failure? ==> |r.error| > 0
{
  if c.fields[1] == "token" then
    r := Success(Ctx(c.input, c.fields, true, c.log + ["auth"], c.data, c.response));
  else
    r := Failure("unauthorized");
}

method Transform(c: Ctx) returns (r: Result<Ctx>)
  requires |c.fields| >= 2 && c.authed
  ensures r.Success? ==> r.value.data == c.fields[0] && |r.value.log| == |c.log| + 1
{
  r := Success(Ctx(c.input, c.fields, c.authed, c.log + ["transform"], c.fields[0], c.response));
}

method Store(c: Ctx) returns (r: Result<Ctx>)
  requires c.authed && |c.fields| >= 2
  ensures r.Success? ==> |r.value.log| == |c.log| + 1 && r.value.data == c.data
{
  r := Success(Ctx(c.input, c.fields, c.authed, c.log + ["store"], c.data, c.response));
}

method Respond(c: Ctx) returns (r: Result<Ctx>)
  requires c.authed && |c.fields| >= 2
  ensures r.Success? ==> |r.value.log| == |c.log| + 1 && r.value.response == c.data
{
  r := Success(Ctx(c.input, c.fields, c.authed, c.log + ["respond"], c.data, c.data));
}

method Run(input: string) returns (r: Result<Ctx>)
  requires |input| > 0
  ensures r.Success? ==> r.value.authed && |r.value.fields| >= 2 && r.value.data == r.value.fields[0] && r.value.response == r.value.data && |r.value.log| == 6
{
  var c := Ctx(input, [], false, [], "", "");
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
  var t := Transform(a.value);
  if t.Failure? {
    r := t;
    return;
  }
  var s := Store(t.value);
  if s.Failure? {
    r := s;
    return;
  }
  r := Respond(s.value);
}