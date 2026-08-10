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

  method RemoveById(id: int) returns (success: bool)
    requires Valid()
    modifies this
    ensures Valid()
    ensures success == (exists i :: 0 <= i < |old(items)| && old(items)[i].id == id)
  {
    var i := 0;
    success := false;
    while i < |items|
      invariant 0 <= i <= |items|
      invariant Valid()
      invariant !success ==> items == old(items)
      invariant !success ==> forall j :: 0 <= j < i ==> old(items)[j].id != id
      invariant success ==> (exists j :: 0 <= j < |old(items)| && old(items)[j].id == id)
      decreases |items| - i
    {
      if items[i].id == id {
        assert items == old(items);
        assert exists j :: 0 <= j < |old(items)| && old(items)[j].id == id;
        items := items[..i] + items[i+1..];
        success := true;
        i := |items|;
      } else {
        i := i + 1;
      }
    }
  }
}