function ContainsInSeq(xs: seq<int>, x: int): bool
  decreases |xs|
{
  if |xs| == 0 then false
  else if xs[0] == x then true
  else ContainsInSeq(xs[1..], x)
}

class Repository {
  var items: seq<int>

  predicate Valid()
    reads this
  {
    true
  }

  constructor()
    ensures Valid()
    ensures |items| == 0
  {
    items := [];
  }

  method Add(x: int)
    requires Valid()
    modifies this
    ensures Valid()
    ensures items == old(items) + [x]
  {
    items := items + [x];
  }

  method RemoveAt(i: int)
    requires Valid()
    requires 0 <= i < |items|
    modifies this
    ensures Valid()
    ensures |items| == |old(items)| - 1
    ensures forall j :: 0 <= j < i ==> items[j] == old(items)[j]
    ensures forall j :: i <= j < |items| ==> items[j] == old(items)[j + 1]
  {
    items := items[..i] + items[i+1..];
  }
}