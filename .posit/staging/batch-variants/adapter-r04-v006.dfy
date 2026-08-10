datatype Result<T> = Success(value: T) | Failure(error: string)

datatype TargetRecord = TargetRecord(name: string, value: string)

method AdaptJsonToRecord(json: string) returns (rec: TargetRecord)
  ensures rec == TargetRecord(json, json)
{
  rec := TargetRecord(json, json);
}

method BatchAdaptRecord(requests: seq<string>) returns (recs: seq<TargetRecord>)
  requires |requests| > 0
  ensures |recs| == |requests|
  ensures forall i :: 0 <= i < |recs| ==> recs[i] == TargetRecord(requests[i], requests[i])
  decreases |requests|
{
  recs := [];
  var i := 0;
  while i < |requests|
    invariant 0 <= i <= |requests|
    invariant |recs| == i
    invariant forall j :: 0 <= j < |recs| ==> recs[j] == TargetRecord(requests[j], requests[j])
    decreases |requests| - i
  {
    var r : TargetRecord := AdaptJsonToRecord(requests[i]);
    recs := recs + [r];
    i := i + 1;
  }
}