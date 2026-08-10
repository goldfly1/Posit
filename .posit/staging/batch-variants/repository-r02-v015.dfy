datatype Item = Item(id: int, name: string)

class Repository {
  var items: seq<Item>

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

  method Add(item: Item)
    requires Valid()
    modifies this
    ensures Valid()
    ensures items == old(items) + [item]
  {
    items := items + [item];
  }

  method Size() returns (n: int)
    requires Valid()
    ensures n == |items|
  {
    n := |items|;
  }
}