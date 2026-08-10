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
  method Parse(src: JsonSrc) returns (r: Result<RecordTarget>)
    requires |src.raw| > 0
    ensures r.Success? ==> |r.value.fields| >= 1
    ensures r.Failure? ==> r.error == "invalid json"
  {
    if src.raw[0] != '{' {
      r := Failure("invalid json");
    } else {
      var n := CountChar(src.raw, ',');
      r := Success(RecordTarget("Record", RepeatStr(n + 1, "")));
    }
  }

  method ParseBatch(srcs: seq<JsonSrc>) returns (r: Result<seq<RecordTarget>>)
    requires forall i :: 0 <= i < |srcs| ==> |srcs[i].raw| > 0
    ensures r.Success? ==> |r.value| == |srcs|
    ensures r.Failure? ==> r.error == "batch failed"
  {
    r := Failure("batch failed");
    var ts: seq<RecordTarget> := [];
    var i := 0;
    while i < |srcs|
      invariant 0 <= i <= |srcs|
      invariant |ts| == i
      invariant r.Failure? ==> r.error == "batch failed"
      decreases |srcs| - i
    {
      var pr := Parse(srcs[i]);
      if pr.Failure? {
        return;
      }
      ts := ts + [pr.value];
      i := i + 1;
    }
    r := Success(ts);
  }

  method ParseWithRetry(src: JsonSrc, maxAttempts: int) returns (r: Result<RecordTarget>)
    requires |src.raw| > 0
    requires maxAttempts > 0
    ensures r.Failure? ==> r.error == "invalid json"
    decreases maxAttempts
  {
    var attempts := 0;
    r := Failure("invalid json");
    while attempts < maxAttempts
      invariant 0 <= attempts <= maxAttempts
      invariant r.Failure? ==> r.error == "invalid json"
      decreases maxAttempts - attempts
    {
      var pr := Parse(src);
      if pr.Success? {
        r := pr;
        return;
      }
      attempts := attempts + 1;
    }
  }
}