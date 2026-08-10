datatype JsonNode = JStr(s: string) | JNum(n: int) | JObj(fields: seq<(string, JsonNode)>)
datatype Map = MapData(keys: seq<string>, values: seq<string>)

method Adapt(json: JsonNode) returns (m: Map)
  ensures m.MapData?
  ensures |m.keys| == |m.values|
{
  match json {
    case JStr(s) => m := MapData(["str"], [s]);
    case JNum(_) => m := MapData(["num"], [""]);
    case JObj(fields) => {
      if |fields| > 0 then {
        m := MapData(["k0"], [""]);
      } else {
        m := MapData([], []);
      }
    }
  }
}

method BatchAdapt(items: seq<JsonNode>) returns (res: seq<Map>)
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

function IsEmptyMap(m: Map): bool { |m.keys| == 0 }

method CountNonEmpty(items: seq<JsonNode>) returns (count: int)
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
    var m := Adapt(items[i]);
    if !IsEmptyMap(m) {
      count := count + 1;
    }
    i := i + 1;
  }
}