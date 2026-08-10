function IndexOfSeq(xs: seq<int>, x: int): int
  decreases |xs|
{
  if |xs| == 0 then -1
  else if xs[0] == x then 0
  else
    var r := IndexOfSeq(xs[1..], x);
    if r == -1 then -1 else 1 + r
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

  method IndexOf(x: int) returns (i: int)
    requires Valid()
    ensures i == IndexOfSeq(items, x)
  {
    i := IndexOfSeq(items, x);
  }
}