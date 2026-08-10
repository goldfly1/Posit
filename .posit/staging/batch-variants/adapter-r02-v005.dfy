datatype Result<T> = Success(value: T) | Failure(error: string)

datatype JsonSrc = JsonSrc(raw: string)
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

class JsonToMapAdapter {
  method Parse(src: JsonSrc) returns (r: Result<MapTarget>)
    requires |src.raw| > 0
    ensures r.Success? ==> |r.value.keys| == |r.value.vals|
    ensures r.Failure? ==> r.error == "invalid json"
  {
    if src.raw[0] != '{' {
      r := Failure("invalid json");
    } else {
      var n := CountChar(src.raw, ',');
      r := Success(MapTarget(RepeatStr(n + 1, "k"), RepeatStr(n + 1, "v")));
    }
  }

  method ParseBatch(srcs: seq<JsonSrc>) returns (r: Result<seq<MapTarget>>)
    requires forall i :: 0 <= i < |srcs| ==> |srcs[i].raw| > 0
    ensures r.Success? ==> |r.value| == |srcs|
    ensures r.Failure? ==> r.error == "batch failed"
  {
    r := Failure("batch failed");
    var ts: seq<MapTarget> := [];
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

  method ParseWithRetry(src: JsonSrc, maxAttempts: int) returns (r: Result<MapTarget>)
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