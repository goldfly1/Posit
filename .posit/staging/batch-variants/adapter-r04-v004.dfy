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

method ValidateJsonForMap(json: string) returns (valid: bool)
  ensures valid ==> |json| > 0
{
  valid := |json| > 0 && json[0] == '{';
}

method AdaptJsonToMap(json: string) returns (m: TargetMap)
  requires |json| > 0
  requires json[0] == '{'
  ensures m.keys == [json] && m.vals == [json]
{
  m := new TargetMap([json], [json]);
}

method BatchAdaptMap(requests: seq<string>) returns (maps: seq<TargetMap>)
  requires |requests| > 0
  requires forall i :: 0 <= i < |requests| ==> |requests[i]| > 0 && requests[i][0] == '{'
  ensures |maps| == |requests|
  ensures forall i :: 0 <= i < |maps| ==> maps[i].keys == [requests[i]] && maps[i].vals == [requests[i]]
  decreases |requests|
{
  maps := [];
  var i := 0;
  while i < |requests|
    invariant 0 <= i <= |requests|
    invariant |maps| == i
    invariant forall j :: 0 <= j < |maps| ==> maps[j].keys == [requests[j]] && maps[j].vals == [requests[j]]
    decreases |requests| - i
  {
    var m : TargetMap := AdaptJsonToMap(requests[i]);
    maps := maps + [m];
    i := i + 1;
  }
}