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

  method Touch(k: K)
    requires Valid()
    requires exists i :: 0 <= i < |items| && items[i].key == k
    modifies this
    ensures Valid()
    ensures |items| == old(|items|)
    ensures items[|items|-1].key == k
  {
    var i := 0;
    var found := false;
    while i < |items| && !found
      invariant 0 <= i <= |items|
      invariant Valid()
      invariant !found ==> exists j :: i <= j < |items| && items[j].key == k
      invariant found ==> (0 <= i < |items| && items[i].key == k)
      decreases if found then 0 else |items| - i
    {
      if items[i].key == k {
        found := true;
      } else {
        i := i + 1;
      }
    }
    assert found;
    var p := items[i];
    items := items[..i] + items[i+1..] + [p];
  }
}