datatype XmlNode = XText(val: string) | XElem(tag: string, children: seq<XmlNode>)
datatype Obj = ObjData(name: string, value: string)

method Adapt(node: XmlNode) returns (obj: Obj)
  ensures obj.ObjData?
{
  match node {
    case XText(val) => obj := ObjData("text", val);
    case XElem(tag, _) => obj := ObjData("elem", tag);
  }
}

method BatchAdapt(items: seq<XmlNode>) returns (res: seq<Obj>)
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

function IsElem(obj: Obj): bool { obj.name == "elem" }

method CountElems(items: seq<XmlNode>) returns (count: int)
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
    var o := Adapt(items[i]);
    if IsElem(o) {
      count := count + 1;
    }
    i := i + 1;
  }
}