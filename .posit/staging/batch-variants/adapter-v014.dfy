datatype XmlNode = XmlText(value: string) | XmlElem(tag: string, children: seq<XmlNode>)
datatype RecordNode = Record(field: string, value: string)

method AdaptXmlToRecord(node: XmlNode) returns (out: seq<RecordNode>)
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

method RecordSize(out: seq<RecordNode>) returns (n: int)
  ensures n == |out|
  ensures n >= 0
{
  n := |out|;
}