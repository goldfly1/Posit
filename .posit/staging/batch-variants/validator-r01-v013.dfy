datatype Result = Valid | Invalid(errors: seq<string>)

method Validate(input: string, minVal: int, maxVal: int) returns (r: Result)
  requires minVal == 0
  requires maxVal == 1000
  ensures r.Valid? || r.Invalid?
  ensures r.Invalid? ==> |r.errors| > 0
{
  var errors: seq<string> := [];
  if |input| == 0 {
    errors := errors + ["empty"];
  }
  if |input| > maxVal {
    errors := errors + ["too long"];
  }
  if |errors| == 0 {
    r := Valid;
  } else {
    r := Invalid(errors);
  }
}