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
  requires maxVal == 1000
  ensures result.Valid? || result.Invalid?
  ensures result.Invalid? ==> |result.errors| > 0
{
  if |input| == 0 {
    result := Invalid(["input is empty"]);
  } else if |input| > maxVal {
    result := Invalid(["input too long: expected at most " + IntToString(maxVal) + " chars"]);
  } else {
    result := Valid;
  }
}