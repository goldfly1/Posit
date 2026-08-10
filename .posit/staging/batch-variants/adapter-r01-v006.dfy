datatype JsonNode = JStr(s: string) | JNum(n: int) | JObj(fields: seq<(string, JsonNode)>)
datatype Rec = RecData(id: int, payload: string)

method Adapt(json: JsonNode) returns (r: Rec)
  ensures r.RecData?
{
  match json {
    case JStr(s) => r := RecData(0, s);
    case JNum(n) => r := RecData(n, "");
    case JObj(_) => r := RecData(-1, "obj");
  }
}

method BatchAdapt(items: seq<JsonNode>) returns (res: seq<Rec>)
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

function IsValidId(r: Rec): bool { r.id >= 0 }

method CountValidIds(items: seq<JsonNode>) returns (count: int)
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
    var r := Adapt(items[i]);
    if IsValidId(r) {
      count := count + 1;
    }
    i := i + 1;
  }
}