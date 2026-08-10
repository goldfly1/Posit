datatype XmlNode = XText(val: string) | XElem(tag: string, children: seq<XmlNode>)
datatype Map = MapData(keys: seq<string>, values: seq<string>)
datatype Result<T> = Success(value: T) | Failure(error: string)

method Adapt(node: XmlNode) returns (result: Result<Map>)
  ensures result.Success? ==> result.value.MapData? && |result.value.keys| == |result.value.values|
  ensures result.Failure? ==> |result.error| > 0
{
  match node {
    case XText(val) => {
      if |val| > 5 then {
        result := Failure("too long");
      } else {
        result := Success(MapData(["t"], [val]));
      }
    }
    case XElem(tag, _) => {
      if |tag| == 0 then {
        result := Failure("empty tag");
      } else {
        result := Success(MapData(["tag"], [tag]));
      }
    }
  }
}

method AdaptWithRetry(node: XmlNode, maxAttempts: int) returns (result: Result<Map>)
  requires maxAttempts > 0
  ensures result.Success? || result.Failure?
  decreases maxAttempts
{
  var attempts := 0;
  result := Failure("all attempts failed");
  while attempts < maxAttempts
    invariant 0 <= attempts <= maxAttempts
    invariant result.Failure? ==> result.error == "all attempts failed"
    decreases maxAttempts - attempts
  {
    var r := Adapt(node);
    if r.Success? {
      result := r;
      return;
    }
    attempts := attempts + 1;
  }
}

method BatchAdapt(items: seq<XmlNode>) returns (res: seq<Result<Map>>)
  requires |items| > 0
  ensures |res| == |items|
  decreases |items|
{
  res := [];
  var i := 0;
  while i < |items|
    invariant 0 <= i <= |items|
    invariant |res| == i
    decreases |items| - i
  {
    res := res + [Adapt(items[i])];
    i := i + 1;
  }
}