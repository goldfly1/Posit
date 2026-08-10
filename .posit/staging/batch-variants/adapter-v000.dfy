datatype JsonNode = JsonStr(value: string) | JsonNum(value: int) | JsonBool(value: bool) | JsonNull | JsonObj(fields: seq<(string, JsonNode)>)
datatype ObjectNode = ObjField(name: string, value: string) | ObjNested(name: string, children: seq<ObjectNode>)

method ValidateJson(node: JsonNode) returns (ok: bool)
  ensures ok ==> ValidNames(node)
{
  match node {
    case JsonObj(fields) =>
      ok := true;
      var i := 0;
      while i < |fields|
        invariant 0 <= i <= |fields|
        decreases |fields| - i
      {
        var p := fields[i];
        if |p.0| == 0 { ok := false; return; }
        i := i + 1;
      }
    case _ => ok := true;
  }
}

predicate ValidNames(node: JsonNode)
  decreases node
{
  match node {
    case JsonObj(fields) => forall i :: 0 <= i < |fields| ==> |fields[i].0| > 0 && ValidNames(fields[i].1)
    case _ => true
  }
}

method AdaptJsonToObject(node: JsonNode) returns (out: seq<ObjectNode>)
  requires ValidNames(node)
  ensures |out| >= 0
{
  match node {
    case JsonStr(v) => out := [ObjField("value", v)];
    case JsonNum(v) => out := [ObjField("value", "number")];
    case JsonBool(v) => out := [ObjField("value", if v then "true" else "false")];
    case JsonNull => out := [ObjField("value", "null")];
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
          case JsonStr(v) => out := out + [ObjField(p.0, v)];
          case JsonNum(v) => out := out + [ObjField(p.0, "number")];
          case JsonBool(v) => out := out + [ObjField(p.0, if v then "true" else "false")];
          case JsonNull => out := out + [ObjField(p.0, "null")];
          case JsonObj(_) => out := out + [ObjField(p.0, "nested")];
        }
        i := i + 1;
      }
  }
}