datatype Result<T> = Success(value: T) | Failure(error: string)

datatype XmlSrc = XmlSrc(raw: string)
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

class XmlToObjectAdapter {
  method Parse(src: XmlSrc) returns (t: ObjTarget)
    requires |src.raw| > 0
    ensures |t.fields| >= 1
  {
    var n := CountChar(src.raw, '<');
    t := ObjTarget(RepeatStr(n + 1, ""));
  }

  method ParseBatch(srcs: seq<XmlSrc>) returns (ts: seq<ObjTarget>)
    requires forall i :: 0 <= i < |srcs| ==> |srcs[i].raw| > 0
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
      var r := Parse(srcs[i]);
      ts := ts + [r];
      i := i + 1;
    }
  }
}

method FieldCount(t: ObjTarget) returns (c: int)
  ensures c == |t.fields|
  ensures c >= 0
{
  c := |t.fields|;
}