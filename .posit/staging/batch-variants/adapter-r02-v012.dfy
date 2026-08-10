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
  method Parse(src: XmlSrc) returns (t: MapTarget)
    requires |src.raw| > 0
    ensures |t.keys| == |t.vals|
    ensures |t.keys| >= 1
  {
    var n := CountChar(src.raw, '<');
    t := MapTarget(RepeatStr(n + 1, "k"), RepeatStr(n + 1, "v"));
  }

  method ParseBatch(srcs: seq<XmlSrc>) returns (ts: seq<MapTarget>)
    requires forall i :: 0 <= i < |srcs| ==> |srcs[i].raw| > 0
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
      var r := Parse(srcs[i]);
      ts := ts + [r];
      i := i + 1;
    }
  }
}

method EntryCount(t: MapTarget) returns (c: int)
  ensures c == |t.keys|
  ensures c >= 0
{
  c := |t.keys|;
}