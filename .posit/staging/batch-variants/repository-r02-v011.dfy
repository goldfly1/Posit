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

  method AddAll(xs: seq<int>)
    requires Valid()
    modifies this
    ensures Valid()
    ensures items == old(items) + xs
  {
    items := items + xs;
  }
}