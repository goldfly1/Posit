datatype JsonNode = JsonStr(value: string) | JsonNum(value: int) | JsonBool(value: bool) | JsonNull | JsonObj(fields: seq<(string, JsonNode)>)
datatype MapNode = MapEntry(key: string, val: string)
datatype Result<T> = Success(value: T) | Failure(error: string)

method AdaptJsonToMap(node: JsonNode) returns (result: Result<seq<MapNode>>)
  ensures result.Success? ==> |result.value| >= 0
  ensures result.Failure? ==> |result.error| > 0
{
  match node {
    case JsonStr(v) => result := Success([MapEntry("value", v)]);
    case JsonNum(v) => result := Success([MapEntry("value", "number")]);
    case JsonBool(v) => result := Success([MapEntry("value", if v then "true" else "false")]);
    case JsonNull => result := Success([MapEntry("value", "null")]);
    case JsonObj(fields) =>
      if |fields| == 0 { result := Failure("empty"); return; }
      var out: seq<MapNode> := [];
      var i := 0;
      result := Failure("partial");
      while i < |fields|
        invariant 0 <= i <= |fields|
        invariant |out| == i
        decreases |fields| - i
      {
        var p := fields[i];
        if |p.0| == 0 { result := Failure("empty key"); return; }
        match p.1 {
          case JsonStr(v) => out := out + [MapEntry(p.0, v)];
          case JsonNum(v) => out := out + [MapEntry(p.0, "number")];
          case JsonBool(v) => out := out + [MapEntry(p.0, if v then "true" else "false")];
          case JsonNull => out := out + [MapEntry(p.0, "null")];
          case JsonObj(_) => out := out + [MapEntry(p.0, "nested")];
        }
        i := i + 1;
      }
      result := Success(out);
  }
}

method IsFailure<T>(r: Result<T>) returns (b: bool)
  ensures b == r.Failure?
{
  b := r.Failure?;
}