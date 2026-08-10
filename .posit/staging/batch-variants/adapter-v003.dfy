datatype JsonNode = JsonStr(value: string) | JsonNum(value: int) | JsonBool(value: bool) | JsonNull | JsonObj(fields: seq<(string, JsonNode)>)
datatype MapNode = MapEntry(key: string, val: string)

predicate ValidKeys(node: JsonNode)
  decreases node
{
  match node {
    case JsonObj(fields) => forall i :: 0 <= i < |fields| ==> |fields[i].0| > 0 && ValidKeys(fields[i].1)
    case _ => true
  }
}

method AdaptJsonToMap(node: JsonNode) returns (out: seq<MapNode>)
  requires ValidKeys(node)
  ensures |out| >= 0
{
  match node {
    case JsonStr(v) => out := [MapEntry("value", v)];
    case JsonNum(v) => out := [MapEntry("value", "number")];
    case JsonBool(v) => out := [MapEntry("value", if v then "true" else "false")];
    case JsonNull => out := [MapEntry("value", "null")];
    case JsonObj(fields) =>
      out := [];
      var i := 0;
      while i < |fields|
        invariant 0 <= i <= |fields|
        invariant |out| == i
        decreases |fields| - i
      {
        var p := fields[i];
        match p.1 {
          case JsonStr(v) => out := out + [MapEntry(p.0, v)];
          case JsonNum(v) => out := out + [MapEntry(p.0, "number")];
          case JsonBool(v) => out := out + [MapEntry(p.0, if v then "true" else "false")];
          case JsonNull => out := out + [MapEntry(p.0, "null")];
          case JsonObj(_) => out := out + [MapEntry(p.0, "nested")];
        }
        i := i + 1;
      }
  }
}

method ValidateKeys(node: JsonNode) returns (ok: bool)
  ensures ok == ValidKeys(node)
{
  ok := ValidKeys(node);
}