datatype Result<T> = Success(value: T) | Failure(error: string)

datatype JsonSrc = JsonSrc(raw: string)
datatype RecordTarget = RecordTarget(name: string, fields: seq<string>)

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

class JsonToRecordAdapter {
  method Parse(src: JsonSrc) returns (t: RecordTarget)
    requires |src.raw| > 0
    ensures |t.fields| >= 1
    ensures |t.name| > 0
  {
    var n := CountChar(src.raw, ',');
    t := RecordTarget("Record", RepeatStr(n + 1, ""));
  }

  method ParseBatch(srcs: seq<JsonSrc>) returns (ts: seq<RecordTarget>)
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

method FieldCountOf(t: RecordTarget) returns (c: int)
  ensures c == |t.fields|
  ensures c >= 0
{
  c := |t.fields|;
}