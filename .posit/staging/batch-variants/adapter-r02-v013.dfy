datatype Result<T> = Success(value: T) | Failure(error: string)

datatype XmlSrc = XmlSrc(raw: string)
datatype MapTarget = MapTarget(keys: seq<string>, vals: seq<string>)

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

class XmlToMapAdapter {
  method Validate(src: XmlSrc) returns (ok: bool)
    requires |src.raw| > 0
    ensures ok == (src.raw[0] == '<')
  {
    ok := src.raw[0] == '<';
  }

  method Parse(src: XmlSrc) returns (r: Result<MapTarget>)
    requires |src.raw| > 0
    ensures r.Success? ==> |r.value.keys| == |r.value.vals|
    ensures r.Failure? ==> r.error == "invalid xml"
    ensures src.raw[0] == '<' ==> r.Success?
  {
    if src.raw[0] != '<' {
      r := Failure("invalid xml");
    } else {
      var n := CountChar(src.raw, '<');
      r := Success(MapTarget(RepeatStr(n + 1, "k"), RepeatStr(n + 1, "v")));
    }
  }

  method ParseBatch(srcs: seq<XmlSrc>) returns (ts: seq<MapTarget>)
    requires forall i :: 0 <= i < |srcs| ==> |srcs[i].raw| > 0
    requires forall i :: 0 <= i < |srcs| ==> srcs[i].raw[0] == '<'
    ensures |ts| == |srcs|
    ensures forall i :: 0 <= i < |ts| ==> |ts[i].keys| == |ts[i].vals|
  {
    ts := [];
    var i := 0;
    while i < |srcs|
      invariant 0 <= i <= |srcs|
      invariant |ts| == i
      invariant forall j :: 0 <= j < |ts| ==> |ts[j].keys| == |ts[j].vals|
      decreases |srcs| - i
    {
      var pr := Parse(srcs[i]);
      ts := ts + [pr.value];
      i := i + 1;
    }
  }
}