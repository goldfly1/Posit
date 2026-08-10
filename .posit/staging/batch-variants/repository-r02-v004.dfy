function ContainsInSeq(xs: seq<int>, x: int): bool
  decreases |xs|
{
  if |xs| == 0 then false
  else if xs[0] == x then true
  else ContainsInSeq(xs[1..], x)
}

class Repository {
  var items: seq<int>

  constructor()
    ensures |items| == 0
  {
    items := [];
  }

  method Add(x: int)
    modifies this
    ensures items == old(items) + [x]
  {
    items := items + [x];
  }

  method Size() returns (n: int)
    ensures n == |items|
  {
    n := |items|;
  }

  method Contains(x: int) returns (b: bool)
    ensures b == ContainsInSeq(items, x)
  {
    b := ContainsInSeq(items, x);
  }

  method Get(i: int) returns (x: int)
    requires 0 <= i < |items|
    ensures x == items[i]
  {
    x := items[i];
  }

  method Clear()
    modifies this
    ensures |items| == 0
  {
    items := [];
  }
}