```dafny
datatype XmlNode = XText(val: string) | XElem(tag: string, children: seq<XmlNode>)
datatype Rec = RecData(id: int, payload: string)

method Adapt(node: XmlNode) returns (r: Rec)
  ensures r.RecData?
{
  if node.XText? {
    r := RecData(0, node.val);
  } else {
    if node.XElem? {
      r := RecData(|node.children|, node.tag);
    } else {
      r := RecData(-1, "");
    }
  }
}

method BatchAdapt(items: seq<XmlNode>) returns (res: seq<Rec>)
  requires |items| > 0
  ensures |res| == |items|
  decreases |