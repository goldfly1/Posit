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

  method RemoveAt(i: int)
    modifies this
    requires 0 <= i < |items|
    ensures |items| == |old(items)| - 1
    ensures forall j :: 0 <= j < i ==> items[j] == old(items)[j]
    ensures forall j :: i <= j < |items| ==> items[j] == old(items)[j + 1]
  {
    items := items[..i] + items[i+1..];
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
}