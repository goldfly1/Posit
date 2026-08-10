datatype Pair<K, V> = Pair(key: K, value: V)

class Cache<K(==), V(==)> {
  var items: seq<Pair<K, V>>
  const capacity: int

  predicate Valid() reads this { capacity >= 1 && |items| <= capacity }

  constructor(cap: int)
    requires cap >= 1
    ensures Valid()
    ensures |items| == 0
    ensures capacity == cap
  {
    items := [];
    capacity := cap;
  }

  method Contains(k: K) returns (b: bool)
    requires Valid()
    ensures Valid()
    ensures b == (exists i :: 0 <= i < |items| && items[i].key == k)
  {
    b := false;
    var i := 0;
    while i < |items|
      invariant 0 <= i <= |items|
      invariant b == (exists j :: 0 <= j < i && items[j].key == k)
      decreases |items| - i
    {
      if items[i].key == k {
        b := true;
      }
      i := i + 1;
    }
  }

  method EvictIfNeeded()
    requires Valid()
    modifies this
    ensures Valid()
    ensures |items| <= capacity - 1
  {
    if |items| == capacity {
      items := items[1..];
    }
  }

  method Add(k: K, v: V)
    requires Valid()
    modifies this
    ensures Valid()
    ensures old(|items|) <= |items| <= old(|items|) + 1
    ensures |items| >= 1
    ensures items[|items|-1] == Pair(k, v)
  {
    EvictIfNeeded();
    items := items + [Pair(k, v)];
  }
}