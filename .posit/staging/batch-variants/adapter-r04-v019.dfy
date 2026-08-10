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

method ValidateCsv(csv: string) returns (valid: bool)
  ensures valid ==> |csv| > 0
{
  if |csv| == 0 {
    valid := false;
  } else if |csv| == 1 {
    valid := false;
  } else {
    valid := csv[0] != csv[1];
  }
}

method AdaptCsvToObject(csv: string) returns (obj: TargetObject)
  requires |csv| > 1
  requires csv[0] != csv[1]
  ensures obj.data == csv
{
  obj := new TargetObject(csv);
}

method BatchAdaptCsv(requests: seq<string>) returns (objs: seq<TargetObject>)
  requires |requests| > 0
  requires forall i :: 0 <= i < |requests| ==> |requests[i]| > 1 && requests[i][0] != requests[i][1]
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
    var o : TargetObject := AdaptCsvToObject(requests[i]);
    objs := objs + [o];
    i := i + 1;
  }
}