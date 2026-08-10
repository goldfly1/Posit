datatype XmlNode = XmlText(value: string) | XmlElem(tag: string, children: seq<XmlNode>)
datatype RecordNode = Record(field: string, value: string)

predicate ValidXmlNode(node: XmlNode)
  decreases node
{
  match node {
    case XmlElem(tag, children) => |tag| > 0 && forall i :: 0 <= i < |children| ==> ValidXmlNode(children[i])
    case _ => true
  }
}

method AdaptXmlToRecord(node: XmlNode) returns (out: seq<RecordNode>)
  requires ValidXmlNode(node)
  ensures |out| >= 0
{
  match node {
    case XmlText(v) => out := [Record("text", v)];
    case XmlElem(tag, children) =>
      out := [Record("tag", tag)];
      var i := 0;
      while i < |children|
        invariant 0 <= i <= |children|
        invariant |out| >= 1
        decreases |children| - i
      {
        var c := children[i];
        match c {
          case XmlText(v) => out := out + [Record("child", v)];
          case XmlElem(ct, _) => out := out + [Record("child", ct)];
        }
        i := i + 1;
      }
  }
}

method CheckValidXml(node: XmlNode) returns (ok: bool)
  ensures ok == ValidXmlNode(node)
{
  ok := ValidXmlNode(node);
}