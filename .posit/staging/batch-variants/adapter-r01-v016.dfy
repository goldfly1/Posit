datatype XmlNode = XText(val: string) | XElem(tag: string, children: seq<XmlNode>)
datatype Rec = RecData(id: int, payload: string)
datatype Result<T> = Success(value: T) | Failure(error: string)

method Validate(node: XmlNode) returns (result: Result<XmlNode>)
  ensures result.Success? ==> result.value == node
  ensures result.Failure? ==> |result.error| > 0
{
  if node.XText? && |node.val| == 0 {
    result := Failure("empty text")
  } else if node.XElem? && |node.tag| == 0 {
    result := Failure("empty tag")
  } else {
    result := Success(node)
  }
}

method Adapt(node: XmlNode) returns (r: Rec)
  requires !(node.XText? && |node.val| == 0)
  requires !(node.XElem? && |node.tag| == 0)
  ensures r.RecData?
{
  if node.XText? {
    r := RecData(0, node.val)
  } else {
    r := RecData(|node.children|, node.tag)
  }
}

method BatchAdapt(items: seq<XmlNode>) returns (res: seq<Rec>)
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
      res := res + [RecData(-2, "err")];
    }
    i := i + 1;
  }
}