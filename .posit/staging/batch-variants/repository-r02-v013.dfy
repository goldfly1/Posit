function MapSeq(xs: seq<int>, f: int -> int): seq<int>
  decreases |xs|
{
  if |xs| == 0 then []
  else [f(xs[0])] + MapSeq(xs[1..], f)
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

  method Map(f: int -> int) returns (res: seq<int>)
    requires Valid()
    ensures res == MapSeq(items, f)
  {
    res := MapSeq(items, f);
  }
}