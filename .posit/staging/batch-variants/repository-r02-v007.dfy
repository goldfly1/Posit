function ContainsInSeq(xs: seq<int>, x: int): bool
  decreases |xs|
{
  if |xs| == 0 then false
  else if xs[0] == x then true
  else ContainsInSeq(xs[1..], x)
}

function SeqMin(xs: seq<int>): int
  requires |xs| > 0
  decreases |xs|
{
  if |xs| == 1 then xs[0]
  else
    var rest := SeqMin(xs[1..]);
    if xs[0] < rest then xs[0] else rest
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

  method Min() returns (m: int)
    requires Valid()
    requires |items| > 0
    ensures m == SeqMin(items)
  {
    m := SeqMin(items);
  }
}