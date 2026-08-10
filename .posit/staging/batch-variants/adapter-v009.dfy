datatype XmlNode = XmlText(value: string) | XmlElem(tag: string, children: seq<XmlNode>)
datatype ObjectNode = ObjField(name: string, value: string) | ObjNested(name: string, children: seq<ObjectNode>)

predicate ValidTags(node: XmlNode)
  decreases node
{
  match node {
    case XmlElem(tag, children) => |tag| > 0 && forall i :: 0 <= i < |children| ==> ValidTags(children[i])
    case _ => true
  }
}

method AdaptXmlToObject(node: XmlNode) returns (out: seq<ObjectNode>)
  requires ValidTags(node)
  ensures |out| >= 0
{
  match node {
    case XmlText(v) => out := [ObjField("text", v)];
    case XmlElem(tag, children) =>
      var childFields := AdaptChildren(children);
      out := [ObjNested(tag, childFields)];
  }
}

method AdaptChildren(children: seq<XmlNode>) returns (out: seq<ObjectNode>)
  requires forall i :: 0 <= i < |children| ==> ValidTags(children[i])
  ensures |out| >= 0
{
  out := [];
  var i := 0;
  while i < |children|
    invariant 0 <= i <= |children|
    invariant |out| >= 0
    decreases |children| - i
  {
    var c := children[i];
    match c {
      case XmlText(v) => out := out + [ObjField("text", v)];
      case XmlElem(tag, _) => out := out + [ObjNested(tag, [])];
    }
    i := i + 1;
  }
}

method CheckTags(node: XmlNode) returns (ok: bool)
  ensures ok == ValidTags(node)
{
  ok := ValidTags(node);
}