datatype XmlNode = XmlText(value: string) | XmlElem(tag: string, children: seq<XmlNode>)
datatype RecordNode = Record(field: string, value: string)
datatype Result<T> = Success(value: T) | Failure(error: string)

method AdaptXmlToRecord(node: XmlNode) returns (result: Result<seq<RecordNode>>)
  ensures result.Success? ==> |result.value| >= 0
  ensures result.Failure? ==> |result.error| > 0
{
  match node {
    case XmlText(v) => result := Success([Record("text", v)]);
    case XmlElem(tag, children) =>
      if |tag| == 0 { result := Failure("empty tag"); return; }
      var out: seq<RecordNode> := [Record("tag", tag)];
      var i := 0;
      result := Failure("partial");
      while i < |children|
        invariant 0 <= i <= |children|
        invariant |out| >= 1
        decreases |children| - i
      {
        var c := children[i];
        match c {
          case XmlText(v) => out := out + [Record("child", v)];
          case XmlElem(ct, _) =>
            if |ct| == 0 { result := Failure("empty child tag"); return; }
            out := out + [Record("child", ct)];
        }
        i := i + 1;
      }
      result := Success(out);
  }
}

method GetResult<T>(r: Result<T>) returns (ok: bool)
  ensures ok == r.Success?
{
  ok := r.Success?;
}