datatype ValidationResult = Valid | Invalid(errors: seq<string>)

method Validate(input: string) returns (result: ValidationResult)
  ensures result.Valid? || result.Invalid?
  ensures result.Invalid? ==> |result.errors| > 0
{
  var errors: seq<string> := [];
  if |input| == 0 {
    errors := errors + ["input is empty"];
  }
  if |input| > 100 {
    errors := errors + ["input too long"];
  }
  if |errors| == 0 {
    result := Valid;
  } else {
    result := Invalid(errors);
  }
}