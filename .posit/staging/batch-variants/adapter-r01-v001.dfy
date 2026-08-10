datatype JsonNode = JStr(s: string) | JNum(n: int) | JObj(fields: seq<(string, JsonNode)>)
datatype Obj = ObjData(name: string, value: string)
datatype Result<T> = Success(value: T) | Failure(error: string)

method Adapt(json: JsonNode) returns (obj: Obj)
  ensures obj.ObjData?
{
  match json {
    case JStr(s) => obj := ObjData("string", s);
    case JNum(_) => obj := ObjData("number", "");
    case JObj(_) => obj := ObjData("object", "");
  }
}

method Validate(json: JsonNode) returns (result: Result<JsonNode>)
  ensures result.Success? ==> result.value == json
  ensures result.Failure? ==> |result.error| > 0
{
  match json {
    case JStr(s) => {
      if |s| == 0 then {
        result := Failure("empty string");
      } else {
        result := Success(json);
      }
    }
    case JNum(n) => {
      if n < 0 then {
        result := Failure("negative number");
      } else {
        result := Success(json);
      }
    }
    case JObj(fields) => {
      if |fields| == 0 then {
        result := Failure("empty object");
      } else {
        result := Success(json);
      }
    }
  }
}

method BatchAdapt(items: seq<JsonNode>) returns (res: seq<Obj>)
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
      res := res + [ObjData("error", "")];
    }
    i := i + 1;
  }
}