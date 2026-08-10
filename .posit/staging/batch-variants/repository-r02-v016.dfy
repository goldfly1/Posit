datatype Item = Item(id: int, name: string)

function FindItemById(xs: seq<Item>, id: int): (item: Item)
  requires exists i :: 0 <= i < |xs| && xs[i].id == id
  ensures item.id == id
  decreases |xs|
{
  if xs[0].id == id then
    xs[0]
  else
    assert exists j :: 0 <= j < |xs[1..]| && xs[1..][j].id == id by {
      var j :| 0 <= j < |xs| && xs[j].id == id;
      assert j >= 1;
      assert xs[1..][j-1] == xs[j];
    }
    FindItemById(xs[1..], id)
}

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

  method FindById(id: int) returns (item: Item)
    requires Valid()
    requires exists i :: 0 <= i < |items| && items[i].id == id
    ensures item.id == id
  {
    item := FindItemById(items, id);
  }
}