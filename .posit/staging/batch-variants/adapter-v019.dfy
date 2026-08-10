datatype JsonNode = JsonStr(value: string) | JsonNum(value: int)
datatype MapNode = MapEntry(key: string, val: string)

method Adapt(node: JsonNode) returns (out: seq<MapNode>)
  ensures |out| >= 0
{
  match node {
    case JsonStr(v) => out := [MapEntry("value", v)];
    case JsonNum(v) => out := [MapEntry("value", "number")];
  }
}