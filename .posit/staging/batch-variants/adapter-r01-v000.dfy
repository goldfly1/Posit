datatype JsonNode = JStr(s: string) | JNum(n: int) | JObj(fields: seq<(string, JsonNode)>)
datatype Obj = ObjData(name: string, value: string)

method Adapt(json: JsonNode) returns (obj: Obj)
  ensures obj.ObjData?
{
  match json {
    case JStr(s) => obj := ObjData("string", s);
    case JNum(_) => obj := ObjData("number", "");
    case JObj(_) => obj := ObjData("object", "");
  }
}

method BatchAdapt(items: seq<JsonNode>) returns (res: seq<Obj>)
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

function IsStringObj(o: Obj): bool { o.name == "string" }

method CountString(items: seq<JsonNode>) returns (count: int)
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
    if IsStringObj(o) {
      count := count + 1;
    }
    i := i + 1;
  }
}