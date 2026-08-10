datatype Result<T> = Success(value: T) | Failure(error: string)

class TargetMap {
  var keys: seq<string>
  var vals: seq<string>
  constructor(k: seq<string>, v: seq<string>)
    requires |k| == |v|
    ensures keys == k && vals == v
  { keys := k; vals := v; }
  method GetSize() returns (n: int)
    ensures n == |keys| && n == |vals|
  { n := |keys|; }
}

method AdaptJsonToMapSafe(json: string) returns (result: Result<TargetMap>)
  ensures result.Success? ==> result.value.keys == [json] && result.value.vals == [json]
  ensures result.Failure? ==> result.error == "invalid json"
{
  if |json| > 0 && json[0] == '{' {
    var m := new TargetMap([json], [json]);
    result := Success(m);
  } else {
    result := Failure("invalid json");
  }
}

method BatchAdaptMapSafe(requests: seq<string>) returns (results: seq<Result<TargetMap>>)
  requires |requests| > 0
  ensures |results| == |requests|
  ensures forall i :: 0 <= i < |results| && results[i].Success? ==> results[i].value.keys == [requests[i]] && results[i].value.vals == [requests[i]]
  ensures forall i :: 0 <= i < |results| && results[i].Failure? ==> results[i].error == "invalid json"
  decreases |requests|
{
  results := [];
  var i := 0;
  while i < |requests|
    invariant 0 <= i <= |requests|
    invariant |results| == i
    invariant forall j :: 0 <= j < |results| && results[j].Success? ==> results[j].value.keys == [requests[j]] && results[j].value.vals == [requests[j]]
    invariant forall j :: 0 <= j < |results| && results[j].Failure? ==> results[j].error == "invalid json"
    decreases |requests| - i
  {
    var r : Result<TargetMap> := AdaptJsonToMapSafe(requests[i]);
    results := results + [r];
    i := i + 1;
  }
}