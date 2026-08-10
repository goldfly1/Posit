datatype JsonNode = JsonStr(value: string) | JsonNum(value: int) | JsonBool(value: bool) | JsonNull | JsonObj(fields: seq<(string, JsonNode)>)
datatype ObjectNode = ObjField(name: string, value: string) | ObjNested(name: string, children: seq<ObjectNode>)
datatype Result<T> = Success(value: T) | Failure(error: string)

method AdaptJsonToObject(node: JsonNode) returns (result: Result<seq<ObjectNode>>)
  ensures result.Success? ==> |result.value| >= 0
  ensures result.Failure? ==> |result.error| > 0
{
  match node {
    case JsonStr(v) => result := Success([ObjField("value", v)]);
    case JsonNum(v) => result := Success([ObjField("value", "number")]);
    case JsonBool(v) => result := Success([ObjField("value", if v then "true" else "false")]);
    case JsonNull => result := Success([ObjField("value", "null")]);
    case JsonObj(fields) =>
      if |fields| == 0 {
        result := Failure("empty object");
        return;
      }
      var out: seq<ObjectNode> := [];
      var i := 0;
      result := Failure("partial");
      while i < |fields|
        invariant 0 <= i <= |fields|
        invariant |out| == i
        decreases |fields| - i
      {
        var p := fields[i];
        if |p.0| == 0 { result := Failure("empty field name"); return; }
        match p.1 {
          case JsonStr(v) => out := out + [ObjField(p.0, v)];
          case JsonNum(v) => out := out + [ObjField(p.0, "number")];
          case JsonBool(v) => out := out + [ObjField(p.0, if v then "true" else "false")];
          case JsonNull => out := out + [ObjField(p.0, "null")];
          case JsonObj(_) => out := out + [ObjField(p.0, "nested")];
        }
        i := i + 1;
      }
      result := Success(out);
  }
}

method IsSuccess<T>(r: Result<T>) returns (b: bool)
  ensures b == r.Success?
{
  b := r.Success?;
}