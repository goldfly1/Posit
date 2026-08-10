function ContainsInSeq(xs: seq<int>, x: int): bool
  decreases |xs|
{
  if |xs| == 0 then false
  else if xs[0] == x then true
  else ContainsInSeq(xs[1..], x)
}

function SeqSum(xs: seq<int>): int
  decreases |xs|
{
  if |xs| == 0 then 0
  else xs[0] + SeqSum(xs[1..])
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

  method Sum() returns (s: int)
    requires Valid()
    ensures s == SeqSum(items)
  {
    s := SeqSum(items);
  }
}