datatype ValidationResult = Valid | Invalid(errors: seq<string>)

function IntToString(n: int): string
  requires n >= 0
  decreases n
{
  if n < 10 then [CharFromInt(n)]
  else IntToString(n / 10) + [CharFromInt(n % 10)]
}

function CharFromInt(n: int): char
  requires 0 <= n <= 9
{
  ['0','1','2','3','4','5','6','7','8','9'][n]
}

method Validate(input: string) returns (result: ValidationResult)
  ensures result.Valid? || result.Invalid?
  ensures result.Valid? ==> 0 < |input| <= 100
  ensures result.Invalid? ==> |result.errors| > 0
{
  var errors: seq<string> := [];
  if |input| == 0 {
    errors := errors + ["input is empty"];
  }
  if |input| > 100 {
    errors := errors + ["input too long: expected at most " + IntToString(100) + " chars"];
  }
  if |errors| == 0 {
    result := Valid;
  } else {
    assert |errors| > 0;
    result := Invalid(errors);
  }
}