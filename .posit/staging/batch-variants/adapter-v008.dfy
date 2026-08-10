datatype XmlNode = XmlText(value: string) | XmlElem(tag: string, children: seq<XmlNode>)
datatype ObjectNode = ObjField(name: string, value: string) | ObjNested(name: string, children: seq<ObjectNode>)

method AdaptXmlToObject(node: XmlNode) returns (out: seq<ObjectNode>)
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
  ensures |out| >= 0
  decreases |children|
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

method CountObjects(out: seq<ObjectNode>) returns (n: int)
  ensures n == |out|
  ensures n >= 0
{
  n := |out|;
}