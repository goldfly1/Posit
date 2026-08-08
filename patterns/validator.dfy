// Pattern: Validator (Approach 3 — pre-written body with parameters)
// responsibility: Validate input against rules, collect errors
// test: Validate("abc") returns Valid
// test: Validate("") returns Invalid(["input is empty"])
//
// Parameters:
//   minLength: int — minimum required length (default 1)
//   maxLength: int — maximum allowed length (default 1000)
//   requiredChars: string — characters that must be present (default "")

include "result.dfy"

datatype ValidationResult =
  | Valid
  | Invalid(errors: seq<string>)

// Validate a string input against length constraints
method Validate(input: string, minLength: int, maxLength: int) returns (result: ValidationResult)
  requires minLength >= 0
  requires maxLength >= minLength
  ensures result.Valid? || result.Invalid?
  ensures result.Invalid? ==> |result.errors| > 0
  decreases |input|
{
  var errors := [];

  if |input| < minLength {
    errors := errors + ["input too short: expected at least " + IntToString(minLength) + " chars"];
  }
  if |input| > maxLength {
    errors := errors + ["input too long: expected at most " + IntToString(maxLength) + " chars"];
  }

  if |errors| == 0 {
    result := Valid;
  } else {
    assert |errors| > 0;
    result := Invalid(errors);
  }
}

// Check if a string contains at least one of the required characters
method ContainsRequired(input: string, required: string) returns (found: bool)
  ensures found ==> exists i, j :: 0 <= i < |input| && 0 <= j < |required| && input[i] == required[j]
  decreases |input|, |required|
{
  found := false;
  ghost var wi := 0;
  ghost var wj := 0;
  var i := 0;
  while i < |input| && !found
    invariant 0 <= i <= |input|
    invariant found ==> 0 <= wi < |input| && 0 <= wj < |required| && input[wi] == required[wj]
    decreases |input| - i
  {
    var j := 0;
    while j < |required| && !found
      invariant 0 <= j <= |required|
      invariant found ==> 0 <= wi < |input| && 0 <= wj < |required| && input[wi] == required[wj]
      decreases |required| - j
    {
      if input[i] == required[j] {
        found := true;
        wi := i;
        wj := j;
      }
      j := j + 1;
    }
    i := i + 1;
  }
}

// Helper: check if a character appears in a string
function ContainsChar(input: string, c: char): bool
  decreases |input|
{
  if |input| == 0 then false
  else if input[0] == c then true
  else ContainsChar(input[1..], c)
}

// Helper: convert int to string (minimal, for error messages)
function IntToString(n: int): string
  requires n >= 0
  decreases n
{
  if n < 10 then [CharFromInt(n)]
  else IntToString(n / 10) + [CharFromInt(n % 10)]
}

// Helper: convert int to char (digits 0-9)
function CharFromInt(n: int): char
  requires 0 <= n <= 9
{
  ['0', '1', '2', '3', '4', '5', '6', '7', '8', '9'][n]
}