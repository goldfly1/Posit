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

method AdaptXmlToObjectSafe(xml: string) returns (result: Result<TargetObject>)
  ensures result.Success? ==> result.value.data == xml
  ensures result.Failure? ==> result.error == "invalid xml"
{
  if |xml| > 0 && xml[0] == '<' {
    var o := new TargetObject(xml);
    result := Success(o);
  } else {
    result := Failure("invalid xml");
  }
}

method BatchAdaptXmlSafe(requests: seq<string>) returns (results: seq<Result<TargetObject>>)
  requires |requests| > 0
  ensures |results| == |requests|
  ensures forall i :: 0 <= i < |results| && results[i].Success? ==> results[i].value.data == requests[i]
  ensures forall i :: 0 <= i < |results| && results[i].Failure? ==> results[i].error == "invalid xml"
  decreases |requests|
{
  results := [];
  var i := 0;
  while i < |requests|
    invariant 0 <= i <= |requests|
    invariant |results| == i
    invariant forall j :: 0 <= j < |results| && results[j].Success? ==> results[j].value.data == requests[j]
    invariant forall j :: 0 <= j < |results| && results[j].Failure? ==> results[j].error == "invalid xml"
    decreases |requests| - i
  {
    var r : Result<TargetObject> := AdaptXmlToObjectSafe(requests[i]);
    results := results + [r];
    i := i + 1;
  }
}