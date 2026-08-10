datatype ValidationResult = Valid(warnings: seq<string>) | Invalid(errors: seq<string>)

method Validate(input: string) returns (result: ValidationResult)
  ensures result.Valid? || result.Invalid?
  ensures result.Invalid? ==> |result.errors| > 0
  ensures result.Valid? ==> |result.warnings| >= 0
{
  var warnings: seq<string> := [];
  if |input| == 0 {
    result := Invalid(["input is empty"]);
  } else if |input| > 10000 {
    result := Invalid(["input too long"]);
  } else {
    if |input| == 1 {
      warnings := ["input is short"];
    }
    result := Valid(warnings);
  }
}