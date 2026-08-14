// Cut-out: row-validator
// Pattern: validator (conforms to validator pattern signatures)
// Domain: data processing
// Params: none (fully self-contained)
// responsibility: validate that all rows have the same number of fields
// test: ValidateRows([["a","b"],["c","d"]]) returns Valid
// test: ValidateRows([["a","b"],["c"]]) returns Invalid

// ValidationResult datatype
datatype ValidationResult =
  | Valid
  | Invalid(errors: seq<string>)

// Validate that all rows have the same field count
method ValidateRows(rows: seq<seq<string>>) returns (result: ValidationResult)
  requires |rows| >= 0
  decreases |rows|
{
  var errors := [];
  if |rows| == 0 {
    result := Valid;
    return;
  }
  
  var expected := |rows[0]|;
  var i := 1;
  while i < |rows|
    invariant 0 <= i <= |rows|
    invariant |errors| >= 0
    decreases |rows| - i
  {
    if |rows[i]| != expected {
      errors := errors + ["field count mismatch"];
    }
    i := i + 1;
  }
  
  if |errors| == 0 {
    result := Valid;
  } else {
    result := Invalid(errors);
  }
}