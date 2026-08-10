datatype JsonNode = JStr(s: string) | JNum(n: int) | JObj(fields: seq<(string, JsonNode)>)
datatype Map = MapData(keys: seq<string>, values: seq<string>)
datatype Result<T> = Success(value: T) | Failure(error: string)

method Validate(json: JsonNode) returns (result: Result<JsonNode>)
  ensures result.Success? ==> result.value == json
  ensures result.Failure? ==> |result.error| > 0
  ensures result.Success? ==> match result.value
    case JStr(s) => |s| > 0
    case JNum(n) => n >= 0
    case JObj(_) => true
{
  match json {
    case JStr(s) => {
      if |s| == 0 then {
        result := Failure("empty");
      } else {
        result := Success(json);
      }
    }
    case JNum(n) => {
      if n < 0 then {
        result := Failure("negative");
      } else {
        result := Success(json);
      }
    }
    case JObj(fields) => {
      if |fields| == 0 then {
        result := Failure("empty obj");
      } else {
        result := Success(json);
      }
    }
  }
}

method Adapt(json: JsonNode) returns (m: Map)
  requires match json
    case JStr(s) => |s| > 0
    case JNum(n) => n >= 0
    case JObj(_) => true
  ensures m.MapData?
  ensures |m.keys| == |m.values|
{
  match json {
    case JStr(s) => m := MapData(["s"], [s]);
    case JNum(_) => m := MapData(["n"], [""]);
    case JObj(_) => m := MapData([], []);
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
    var v := Validate(items[i]);
    if v.Success? {
      res := res + [Adapt(v.value)];
    } else {
      res := res + [MapData([], [])];
    }
    i := i + 1;
  }
}