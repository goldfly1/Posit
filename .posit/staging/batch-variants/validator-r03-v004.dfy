datatype ValidationResult = Valid | Invalid(error: string)

method Validate(input: string) returns (result: ValidationResult)
  ensures result.Valid? || result.Invalid?
{
  if |input| == 0 {
    result := Invalid("input is empty");
  } else if |input| > 50 {
    result := Invalid("input too long");
  } else {
    result := Valid;
  }
}