datatype XmlNode = XmlText(value: string) | XmlElem(tag: string, children: seq<XmlNode>)
datatype ObjectNode = ObjField(name: string, value: string) | ObjNested(name: string, children: seq<ObjectNode>)
datatype Result<T> = Success(value: T) | Failure(error: string)

method AdaptXmlToObject(node: XmlNode) returns (result: Result<seq<ObjectNode>>)
  ensures result.Success? ==> |result.value| >= 0
  ensures result.Failure? ==> |result.error| > 0
{
  match node {
    case XmlText(v) => result := Success([ObjField("text", v)]);
    case XmlElem(tag, children) =>
      if |tag| == 0 { result := Failure("empty tag"); return; }
      var childFields: seq<ObjectNode> := [];
      var i := 0;
      result := Failure("partial");
      while i < |children|
        invariant 0 <= i <= |children|
        invariant |childFields| >= 0
        decreases |children| - i
      {
        var c := children[i];
        match c {
          case XmlText(v) => childFields := childFields + [ObjField("text", v)];
          case XmlElem(ct, _) =>
            if |ct| == 0 { result := Failure("empty child tag"); return; }
            childFields := childFields + [ObjNested(ct, [])];
        }
        i := i + 1;
      }
      result := Success([ObjNested(tag, childFields)]);
  }
}

method IsOk<T>(r: Result<T>) returns (b: bool)
  ensures b == r.Success?
{
  b := r.Success?;
}