datatype Item = Item(id: int, name: string)

function GetIds(xs: seq<Item>): seq<int>
  decreases |xs|
{
  if |xs| == 0 then []
  else [xs[0].id] + GetIds(xs[1..])
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

  method GetAllIds() returns (ids: seq<int>)
    requires Valid()
    ensures ids == GetIds(items)
  {
    ids := GetIds(items);
  }
}