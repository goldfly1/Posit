datatype Pair = Pair(key: int, value: int)

class Cache {
  var items: seq<Pair>
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

  method Add(k: int, v: int)
    requires Valid()
    modifies this
    ensures Valid()
    ensures old(|items|) <= |items| <= old(|items|) + 1
    ensures |items| >= 1
    ensures items[|items|-1] == Pair(k, v)
  {
    if |items| == capacity {
      items := items[1..];
    }
    items := items + [Pair(k, v)];
  }
}