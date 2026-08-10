datatype Result = Valid | Invalid(errors: seq<string>) | Warning(warnings: seq<string>)

method Validate(input: string, minVal: int, maxVal: int) returns (r: Result)
  requires minVal == 0
  requires maxVal == 1000
  ensures r.Valid? || r.Invalid? || r.Warning?
  ensures r.Invalid? ==> |r.errors| > 0
  ensures r.Warning? ==> |r.warnings| > 0
{
  var errors: seq<string> := [];
  var warnings: seq<string> := [];
  if |input| == 0 {
    errors := errors + ["empty"];
  } else if |input| > 800 {
    warnings := warnings + ["near max"];
  }
  if |input| > maxVal {
    errors := errors + ["too long"];
  }
  if |errors| > 0 {
    r := Invalid(errors);
  } else if |warnings| > 0 {
    r := Warning(warnings);
  } else {
    r := Valid;
  }
}