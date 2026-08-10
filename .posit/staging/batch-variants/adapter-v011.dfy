datatype XmlNode = XmlText(value: string) | XmlElem(tag: string, children: seq<XmlNode>)
datatype MapNode = MapEntry(key: string, val: string)

method AdaptXmlToMap(node: XmlNode) returns (out: seq<MapNode>)
  ensures |out| >= 0
{
  match node {
    case XmlText(v) => out := [MapEntry("text", v)];
    case XmlElem(tag, children) =>
      out := [MapEntry("tag", tag)];
      var i := 0;
      while i < |children|
        invariant 0 <= i <= |children|
        invariant |out| >= 1
        decreases |children| - i
      {
        var c := children[i];
        match c {
          case XmlText(v) => out := out + [MapEntry("child", v)];
          case XmlElem(ct, _) => out := out + [MapEntry("child", ct)];
        }
        i := i + 1;
      }
  }
}

method MapCount(out: seq<MapNode>) returns (n: int)
  ensures n == |out|
  ensures n >= 0
{
  n := |out|;
}