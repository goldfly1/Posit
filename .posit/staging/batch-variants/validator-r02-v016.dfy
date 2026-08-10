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

method CheckNonEmpty(input: string) returns (err: string)
  ensures err == "" ==> |input| > 0
  ensures err != "" ==> |input| == 0
{
  if |input| == 0 { err := "input is empty"; } else { err := ""; }
}

method CheckMaxLength(input: string) returns (err: string)
  ensures err == "" ==> |input| <= 10000
  ensures err != "" ==> |input| > 10000
{
  if |input| > 10000 {
    err := "input too long: expected at most " + IntToString(10000) + " chars";
  } else {
    err := "";
  }
}

method Validate(input: string) returns (result: ValidationResult)
  ensures result.Valid? || result.Invalid?
  ensures result.Valid? ==> 0 < |input| <= 10000
  ensures result.Invalid? ==> |result.errors| > 0
{
  var errors: seq<string> := [];
  var e1 := CheckNonEmpty(input);
  if e1 != "" { errors := errors + [e1]; }
  var e2 := CheckMaxLength(input);
  if e2 != "" { errors := errors + [e2]; }
  if |errors| == 0 {
    result := Valid;
  } else {
    assert |errors| > 0;
    result := Invalid(errors);
  }
}