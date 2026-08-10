datatype Result<T> = Success(value: T) | Failure(error: string)

datatype TargetRecord = TargetRecord(name: string, value: string)

method AdaptJsonToRecordSafe(json: string) returns (result: Result<TargetRecord>)
  ensures result.Success? ==> result.value == TargetRecord(json, json)
  ensures result.Failure? ==> result.error == "invalid json"
{
  if |json| > 0 && json[0] == '{' {
    result := Success(TargetRecord(json, json));
  } else {
    result := Failure("invalid json");
  }
}

method BatchAdaptRecordSafe(requests: seq<string>) returns (results: seq<Result<TargetRecord>>)
  requires |requests| > 0
  ensures |results| == |requests|
  ensures forall i :: 0 <= i < |results| && results[i].Success? ==> results[i].value == TargetRecord(requests[i], requests[i])
  ensures forall i :: 0 <= i < |results| && results[i].Failure? ==> results[i].error == "invalid json"
  decreases |requests|
{
  results := [];
  var i := 0;
  while i < |requests|
    invariant 0 <= i <= |requests|
    invariant |results| == i
    invariant forall j :: 0 <= j < |results| && results[j].Success? ==> results[j].value == TargetRecord(requests[j], requests[j])
    invariant forall j :: 0 <= j < |results| && results[j].Failure? ==> results[j].error == "invalid json"
    decreases |requests| - i
  {
    var r : Result<TargetRecord> := AdaptJsonToRecordSafe(requests[i]);
    results := results + [r];
    i := i + 1;
  }
}