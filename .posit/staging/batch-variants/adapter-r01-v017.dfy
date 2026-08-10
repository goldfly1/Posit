datatype XmlNode = XText(val: string) | XElem(tag: string, children: seq<XmlNode>)
datatype Rec = RecData(id: int, payload: string)
datatype Result<T> = Success(value: T) | Failure(error: string)

method Adapt(node: XmlNode) returns (result: Result<Rec>)
  ensures result.Success? ==> result.value.RecData?
  ensures result.Failure? ==> |result.error| > 0
{
  if node.XText? {
    if |node.val| > 8 {
      result := Failure("too long")
    } else {
      result := Success(RecData(0, node.val))
    }
  } else if node.XElem? {
    if |node.tag| == 0 {
      result := Failure("empty tag")
    } else {
      result := Success(RecData(|node.children|, node.tag))
    }
  } else {
    result := Failure("unknown")
  }
}

method AdaptWithRetry(node: XmlNode, maxAttempts: int) returns (result: Result<Rec>)
  requires maxAttempts > 0
  ensures result.Success? || result.Failure?
  decreases maxAttempts
{
  var attempts := 0;
  result := Failure("all attempts failed");
  while attempts < maxAttempts
    invariant 0 <= attempts <= maxAttempts
    invariant result.Failure? ==> result.error == "all attempts failed"
    decreases maxAttempts - attempts
  {
    var r := Adapt(node);
    if r.Success? {
      result := r;
      return;
    }
    attempts := attempts + 1;
  }
}

method BatchAdapt(items: seq<XmlNode>) returns (res: seq<Result<Rec>>)
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