function FilterSeq(xs: seq<int>, p: int -> bool): seq<int>
  decreases |xs|
{
  if |xs| == 0 then []
  else if p(xs[0]) then [xs[0]] + FilterSeq(xs[1..], p)
  else FilterSeq(xs[1..], p)
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

  method Filter(p: int -> bool) returns (res: seq<int>)
    requires Valid()
    ensures res == FilterSeq(items, p)
  {
    res := FilterSeq(items, p);
  }
}