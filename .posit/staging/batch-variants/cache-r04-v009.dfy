datatype Pair = Pair(key: int, value: int)

class Cache {
  var items: seq<Pair>
  const capacity: int

  predicate Valid() reads this {
    capacity >= 1 &&
    |items| <= capacity &&
    (forall i, j :: 0 <= i < j < |items| ==> items[i].key != items[j].key)
  }

  constructor(cap: int)
    requires cap >= 1
    ensures Valid()
    ensures |items| == 0
    ensures capacity == cap
  {
    items := [];
    capacity := cap;
  }

  method Add(k: int, v: int)
    requires Valid()
    modifies this
    ensures Valid()
    ensures |items| >= 1
    ensures exists i :: 0 <= i < |items| && items[i].key == k && items[i].value == v
  {
    var i := 0;
    var found := false;
    while i < |items| && !found
      invariant 0 <= i <= |items|
      invariant !found ==> forall j :: 0 <= j < i ==> items[j].key != k
      invariant Valid()
      decreases if found then 0 else |items| - i
    {
      if items[i].key == k {
        items := items[..i] + [Pair(k, v)] + items[i+1..];
        found := true;
      } else {
        i := i + 1;
      }
    }
    if !found {
      if |items| == capacity {
        items := items[1..];
      }
      items := items + [Pair(k, v)];
    } else {
      assert items[i] == Pair(k, v);
    }
  }
}