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

method AdaptJsonToObject(json: string) returns (obj: TargetObject)
  ensures obj.data == json
{
  obj := new TargetObject(json);
}

method BatchAdapt(requests: seq<string>) returns (objs: seq<TargetObject>)
  requires |requests| > 0
  ensures |objs| == |requests|
  ensures forall i :: 0 <= i < |objs| ==> objs[i].data == requests[i]
  decreases |requests|
{
  objs := [];
  var i := 0;
  while i < |requests|
    invariant 0 <= i <= |requests|
    invariant |objs| == i
    invariant forall j :: 0 <= j < |objs| ==> objs[j].data == requests[j]
    decreases |requests| - i
  {
    var o : TargetObject := AdaptJsonToObject(requests[i]);
    objs := objs + [o];
    i := i + 1;
  }
}

function JsonLength(json: string): int
  decreases |json|
{
  |json|
}