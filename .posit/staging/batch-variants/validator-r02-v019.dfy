datatype Status = Valid | Invalid(reason: string)

function IsSorted(xs: seq<int>): bool
  decreases |xs|
{
  forall i, j :: 0 <= i <= j < |xs| ==> xs[i] <= xs[j]
}

function CountPositive(xs: seq<int>): int
  decreases |xs|
{
  if |xs| == 0 then 0
  else if xs[0] > 0 then 1 + CountPositive(xs[1..])
  else CountPositive(xs[1..])
}

method FindMax(xs: seq<int>) returns (max: int)
  requires |xs| > 0
  ensures forall i :: 0 <= i < |xs| ==> xs[i] <= max
  ensures exists i :: 0 <= i < |xs| && xs[i] == max
{
  max := xs[0];
  var i := 1;
  while i < |xs|
    invariant 0 <= i <= |xs|
    invariant forall k :: 0 <= k < i ==> xs[k] <= max
    invariant exists k :: 0 <= k < i && xs[k] == max
    decreases |xs| - i
  {
    if xs[i] > max {
      max := xs[i];
    }
    i := i + 1;
  }
}

method Validate(xs: seq<int>) returns (status: Status)
  requires |xs| >= 0
  ensures match status
    case Valid => IsSorted(xs) && CountPositive(xs) <= |xs|
    case Invalid(_) => true
{
  if IsSorted(xs) {
    var pos := CountPositive(xs);
    if pos <= |xs| {
      status := Valid;
    } else {
      status := Invalid("too many positives");
    }
  } else {
    status := Invalid("not sorted");
  }
}