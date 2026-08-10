datatype JsonNode = JsonStr(value: string) | JsonNum(value: int) | JsonBool(value: bool) | JsonNull | JsonObj(fields: seq<(string, JsonNode)>)
datatype RecordNode = Record(field: string, value: string)
datatype Result<T> = Success(value: T) | Failure(error: string)

method AdaptJsonToRecord(node: JsonNode) returns (result: Result<seq<RecordNode>>)
  ensures result.Success? ==> |result.value| >= 0
  ensures result.Failure? ==> |result.error| > 0
{
  match node {
    case JsonStr(v) => result := Success([Record("value", v)]);
    case JsonNum(v) => result := Success([Record("value", "number")]);
    case JsonBool(v) => result := Success([Record("value", if v then "true" else "false")]);
    case JsonNull => result := Success([Record("value", "null")]);
    case JsonObj(fields) =>
      if |fields| == 0 { result := Failure("empty record"); return; }
      var out: seq<RecordNode> := [];
      var i := 0;
      result := Failure("partial");
      while i < |fields|
        invariant 0 <= i <= |fields|
        invariant |out| == i
        decreases |fields| - i
      {
        var p := fields[i];
        if |p.0| == 0 { result := Failure("empty field"); return; }
        match p.1 {
          case JsonStr(v) => out := out + [Record(p.0, v)];
          case JsonNum(v) => out := out + [Record(p.0, "number")];
          case JsonBool(v) => out := out + [Record(p.0, if v then "true" else "false")];
          case JsonNull => out := out + [Record(p.0, "null")];
          case JsonObj(_) => out := out + [Record(p.0, "nested")];
        }
        i := i + 1;
      }
      result := Success(out);
  }
}

method ExtractValue<T>(r: Result<T>) returns (v: T)
  requires r.Success?
  ensures v == r.value
{
  v := r.value;
}