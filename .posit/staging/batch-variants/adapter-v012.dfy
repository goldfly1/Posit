datatype XmlNode = XmlText(value: string) | XmlElem(tag: string, children: seq<XmlNode>)
datatype MapNode = MapEntry(key: string, val: string)

predicate ValidXmlTags(node: XmlNode)
  decreases node
{
  match node {
    case XmlElem(tag, children) => |tag| > 0 && forall i :: 0 <= i < |children| ==> ValidXmlTags(children[i])
    case _ => true
  }
}

method AdaptXmlToMap(node: XmlNode) returns (out: seq<MapNode>)
  requires ValidXmlTags(node)
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

method ValidateXml(node: XmlNode) returns (ok: bool)
  ensures ok == ValidXmlTags(node)
{
  ok := ValidXmlTags(node);
}