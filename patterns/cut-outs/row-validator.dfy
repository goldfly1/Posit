// Cut-out: row-validator
// Pattern: validator (conforms to validator pattern signatures)
// Domain: data processing
// Params: none (fully self-contained)
// responsibility: validate that all rows have the same number of fields
// test: ValidateRows([["a","b"],["c","d"]]) returns (rows, true)
// test: ValidateRows([["a","b"],["c"]]) returns (rows, false)

// ValidationResult datatype
datatype ValidationResult =
  | Valid
  | Invalid(errors: seq<string>)

// Validate that all rows have the same field count.
// Returns the rows AND the verdict — data rides alongside, not instead of.
// The chain continues with the rows (first return value).
method ValidateRows(rows: seq<seq<string>>) returns (outRows: seq<seq<string>>, isValid: bool)
  requires |rows| >= 0
  ensures outRows == rows
  decreases |rows|
{
  outRows := rows;
  if |rows| == 0 {
    isValid := true;
    return;
  }
  
  var expected := |rows[0]|;
  var i := 1;
  var allValid := true;
  while i < |rows|
    invariant 0 <= i <= |rows|
    decreases |rows| - i
  {
    if |rows[i]| != expected {
      allValid := false;
    }
    i := i + 1;
  }
  
  isValid := allValid;
}