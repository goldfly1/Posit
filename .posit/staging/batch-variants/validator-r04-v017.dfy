datatype ValidationResult = Valid | Invalid(errors: seq<string>)

function CharFromInt(n: int): char
  requires 0 <= n <= 9
{
  ['0', '1', '2', '3', '4', '5', '6', '7', '8', '9'][n]
}

function IntToString(n: int): string
  requires n >= 0
  decreases n
{
  if n < 10 then [CharFromInt(n)]
  else IntToString(n / 10) + [CharFromInt(n % 10)]
}

method Validate(input: string, minVal: int, maxVal: int) returns (result: ValidationResult)
  requires minVal == 0
  requires maxVal == 10000
  ensures result.Valid? || result.Invalid?
  ensures result.Invalid? ==> |result.errors| > 0
{
  var errors := [];
  if |input| == 0 {
    errors := errors + ["input is empty"];
  }
  if |input| > maxVal {
    errors := errors + ["input too long: expected at most " + IntToString(maxVal) + " chars"];
  }
  if |errors| == 0 {
    result := Valid;
  } else {
    result := Invalid(errors);
  }
}