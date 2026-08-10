datatype Result<T> = Success(value: T) | Failure(error: string)

class TargetObject {
  var data: string
  constructor(d: string)
    ensures data == d
  { data := d; }
  method GetData() returns (d: string)
    ensures d == data
  { d := data; }
}

method AdaptJsonToObjectSafe(json: string) returns (result: Result<TargetObject>)
  ensures result.Success? ==> result.value.data == json
  ensures result.Failure? ==> result.error == "invalid json"
{
  if |json| > 0 && json[0] == '{' {
    var o := new TargetObject(json);
    result := Success(o);
  } else {
    result := Failure("invalid json");
  }
}

method BatchAdaptSafe(requests: seq<string>) returns (results: seq<Result<TargetObject>>)
  requires |requests| > 0
  ensures |results| == |requests|
  ensures forall i :: 0 <= i < |results| && results[i].Success? ==> results[i].value.data == requests[i]
  ensures forall i :: 0 <= i < |results| && results[i].Failure? ==> results[i].error == "invalid json"
  decreases |requests|
{
  results := [];
  var i := 0;
  while i < |requests|
    invariant 0 <= i <= |requests|
    invariant |results| == i
    invariant forall j :: 0 <= j < |results| && results[j].Success? ==> results[j].value.data == requests[j]
    invariant forall j :: 0 <= j < |results| && results[j].Failure? ==> results[j].error == "invalid json"
    decreases |requests| - i
  {
    var r : Result<TargetObject> := AdaptJsonToObjectSafe(requests[i]);
    results := results + [r];
    i := i + 1;
  }
}