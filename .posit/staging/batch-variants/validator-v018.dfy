datatype ValidationResult = Valid | Invalid(errors: seq<string>)

function IsNonEmpty(input: string): bool
  decreases |input|
{
  |input| > 0
}

function IsValidLength(input: string, maxVal: int): bool
  requires maxVal >= 0
  decreases |input|
{
  |input| <= maxVal
}

method Validate(input: string) returns (result: ValidationResult)
  ensures result.Valid? || result.Invalid?
  ensures result.Invalid? ==> |result.errors| > 0
{
  if !IsNonEmpty(input) {
    result := Invalid(["input is empty"]);
    return;
  }
  if !IsValidLength(input, 10000) {
    result := Invalid(["input too long"]);
    return;
  }
  result := Valid;
}