datatype XmlNode = XText(val: string) | XElem(tag: string, children: seq<XmlNode>)
datatype Map = MapData(keys: seq<string>, values: seq<string>)

method Adapt(node: XmlNode) returns (m: Map)
  ensures m.MapData?
  ensures |m.keys| == |m.values|
{
  match node {
    case XText(val) => m := MapData(["text"], [val]);
    case XElem(tag, children) => {
      if |children| > 0 then {
        m := MapData(["tag", "count"], [tag, "1"]);
      } else {
        m := MapData([], []);
      }
    }
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
    res := res + [Adapt(items[i])];
    i := i + 1;
  }
}

function IsEmptyMap(m: Map): bool { |m.keys| == 0 }

method CountNonEmpty(items: seq<XmlNode>) returns (count: int)
  ensures count >= 0
  ensures count <= |items|
  decreases |items|
{
  count := 0;
  var i := 0;
  while i < |items|
    invariant 0 <= i <= |items|
    invariant count <= i
    decreases |items| - i
  {
    var m := Adapt(items[i]);
    if !IsEmptyMap(m) {
      count := count + 1;
    }
    i := i + 1;
  }
}