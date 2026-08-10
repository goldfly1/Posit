datatype XmlNode = XText(val: string) | XElem(tag: string, children: seq<XmlNode>)
datatype Map = MapData(keys: seq<string>, values: seq<string>)
datatype Result<T> = Success(value: T) | Failure(error: string)

method Validate(node: XmlNode) returns (result: Result<XmlNode>)
  ensures result.Success? ==> result.value == node
  ensures result.Failure? ==> |result.error| > 0
  ensures result.Success? ==> match result.value
    case XText(val) => |val| > 0
    case XElem(tag, _) => |tag| > 0
{
  match node {
    case XText(val) => {
      if |val| == 0 then {
        result := Failure("empty text");
      } else {
        result := Success(node);
      }
    }
    case XElem(tag, _) => {
      if |tag| == 0 then {
        result := Failure("empty tag");
      } else {
        result := Success(node);
      }
    }
  }
}

method Adapt(node: XmlNode) returns (m: Map)
  requires match node
    case XText(val) => |val| > 0
    case XElem(tag, _) => |tag| > 0
  ensures m.MapData?
  ensures |m.keys| == |m.values|
{
  match node {
    case XText(val) => m := MapData(["text"], [val]);
    case XElem(tag, _) => m := MapData(["tag"], [tag]);
  }
}

method BatchAdapt(items: seq<XmlNode>) returns (res: seq<Map>)
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
    var v := Validate(items[i]);
    if v.Success? {
      res := res + [Adapt(v.value)];
    } else {
      res := res + [MapData([], [])];
    }
    i := i + 1;
  }
}