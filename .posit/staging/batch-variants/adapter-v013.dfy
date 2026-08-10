datatype XmlNode = XmlText(value: string) | XmlElem(tag: string, children: seq<XmlNode>)
datatype MapNode = MapEntry(key: string, val: string)
datatype Result<T> = Success(value: T) | Failure(error: string)

method AdaptXmlToMap(node: XmlNode) returns (result: Result<seq<MapNode>>)
  ensures result.Success? ==> |result.value| >= 0
  ensures result.Failure? ==> |result.error| > 0
{
  match node {
    case XmlText(v) => result := Success([MapEntry("text", v)]);
    case XmlElem(tag, children) =>
      if |tag| == 0 { result := Failure("empty tag"); return; }
      var out: seq<MapNode> := [MapEntry("tag", tag)];
      var i := 0;
      result := Failure("partial");
      while i < |children|
        invariant 0 <= i <= |children|
        invariant |out| >= 1
        decreases |children| - i
      {
        var c := children[i];
        match c {
          case XmlText(v) => out := out + [MapEntry("child", v)];
          case XmlElem(ct, _) =>
            if |ct| == 0 { result := Failure("empty child tag"); return; }
            out := out + [MapEntry("child", ct)];
        }
        i := i + 1;
      }
      result := Success(out);
  }
}

method IsErr<T>(r: Result<T>) returns (b: bool)
  ensures b == r.Failure?
{
  b := r.Failure?;
}