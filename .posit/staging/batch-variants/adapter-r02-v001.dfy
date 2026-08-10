datatype Result<T> = Success(value: T) | Failure(error: string)

datatype JsonSrc = JsonSrc(raw: string)
datatype ObjTarget = ObjTarget(fields: seq<string>)

function CountChar(s: string, c: char): int
  ensures CountChar(s, c) >= 0
  decreases |s|
{
  if |s| == 0 then 0
  else if s[0] == c then 1 + CountChar(s[1..], c)
  else CountChar(s[1..], c)
}

function RepeatStr(n: int, s: string): seq<string>
  requires n >= 0
  ensures |RepeatStr(n, s)| == n
  decreases n
{
  if n == 0 then [] else [s] + RepeatStr(n - 1, s)
}

class JsonToObjectAdapter {
  method Validate(src: JsonSrc) returns (ok: bool)
    requires |src.raw| > 0
    ensures ok == (src.raw[0] == '{')
  {
    ok := src.raw[0] == '{';
  }

  method Parse(src: JsonSrc) returns (r: Result<ObjTarget>)
    requires |src.raw| > 0
    ensures r.Success? ==> |r.value.fields| >= 1
    ensures r.Failure? ==> r.error == "invalid json"
    ensures src.raw[0] == '{' ==> r.Success?
  {
    if src.raw[0] != '{' {
      r := Failure("invalid json");
    } else {
      var n := CountChar(src.raw, ',');
      r := Success(ObjTarget(RepeatStr(n + 1, "")));
    }
  }

  method ParseBatch(srcs: seq<JsonSrc>) returns (ts: seq<ObjTarget>)
    requires forall i :: 0 <= i < |srcs| ==> |srcs[i].raw| > 0
    requires forall i :: 0 <= i < |srcs| ==> srcs[i].raw[0] == '{'
    ensures |ts| == |srcs|
    ensures forall i :: 0 <= i < |ts| ==> |ts[i].fields| >= 1
  {
    ts := [];
    var i := 0;
    while i < |srcs|
      invariant 0 <= i <= |srcs|
      invariant |ts| == i
      invariant forall j :: 0 <= j < |ts| ==> |ts[j].fields| >= 1
      decreases |srcs| - i
    {
      var pr := Parse(srcs[i]);
      ts := ts + [pr.value];
      i := i + 1;
    }
  }
}