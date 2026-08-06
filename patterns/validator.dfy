// Pattern: Validator
// Check input against rules, return ok or list of errors.
// Pre-cut stub: customize the validation rules and input type.

include "result.dfy"

datatype ValidationResult =
  | Valid
  | Invalid(errors: seq<string>)

// Validate: string -> ValidationResult
// Example: form validator, schema validator, constraint checker
method Validate(input: string) returns (result: ValidationResult)
  requires |input| > 0
  ensures result.Valid? ==> true  // valid means no errors
  ensures result.Invalid? ==> |result.errors| > 0  // invalid means at least one error
  // approach: check input against each rule, collect errors