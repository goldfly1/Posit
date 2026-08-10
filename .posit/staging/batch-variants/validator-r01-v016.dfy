datatype Result = Valid | Invalid(err: string)

method Validate(input: string, minVal: int, maxVal: int) returns (r: Result)
  requires minVal == 0
  requires maxVal == 10000
  ensures r.Valid? || r.Invalid?
{
  r := Valid;
  if |input| == 0 {
    r := Invalid("empty");
  } else if |input| > maxVal {
    r := Invalid("too long");
  }
}