datatype JsonNode = JsonStr(value: string) | JsonNum(value: int) | JsonBool(value: bool) | JsonNull | JsonObj(fields: seq<(string, JsonNode)>)
datatype RecordNode = Record(field: string, value: string)

predicate ValidFields(node: JsonNode)
  decreases node
{
  match node {
    case JsonObj(fields) => forall i :: 0 <= i < |fields| ==> |fields[i].0| > 0 && ValidFields(fields[i].1)
    case _ => true
  }
}

method AdaptJsonToRecord(node: JsonNode) returns (out: seq<RecordNode>)
  requires ValidFields(node)
  ensures |out| >= 0
{
  match node {
    case JsonStr(v) => out := [Record("value", v)];
    case JsonNum(v) => out := [Record("value", "number")];
    case JsonBool(v) => out := [Record("value", if v then "true" else "false")];
    case JsonNull => out := [Record("value", "null")];
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
          case JsonStr(v) => out := out + [Record(p.0, v)];
          case JsonNum(v) => out := out + [Record(p.0, "number")];
          case JsonBool(v) => out := out + [Record(p.0, if v then "true" else "false")];
          case JsonNull => out := out + [Record(p.0, "null")];
          case JsonObj(_) => out := out + [Record(p.0, "nested")];
        }
        i := i + 1;
      }
  }
}

method CheckValid(node: JsonNode) returns (ok: bool)
  ensures ok == ValidFields(node)
{
  ok := ValidFields(node);
}