datatype ValidationResult = Valid | Invalid(errors: seq<string>)

method Validate(input: string) returns (result: ValidationResult)
  ensures result.Valid? || result.Invalid?
  ensures result.Invalid? ==> |result.errors| == 1
{
  if |input| == 0 {
    result := Invalid(["input is empty"]);
  } else if |input| > 10000 {
    result := Invalid(["input too long"]);
  } else {
    result := Valid;
  }
}