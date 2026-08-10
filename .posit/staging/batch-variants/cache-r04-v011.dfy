datatype Option<T> = Some(value: T) | None
datatype Pair = Pair(key: int, value: int)

class Cache {
  var items: seq<Pair>
  const capacity: int

  predicate Valid() reads this {
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
    ensures exists i :: 0 <= i < |items| && items[i].key == k && items[i].value == v
  {
    var i := 0;
    var found := false;
    while i < |items| && !found
      invariant 0 <= i <= |items|
      invariant !found ==> forall j :: 0 <= j < i ==> items[j].key != k
      invariant Valid()
      decreases |items| - i
    {
      if items[i].key == k {
        items[i] := Pair(k, v);
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
    }
  }

  method Get(k: int) returns (res: Option<int>)
    requires Valid()
    ensures Valid()
    ensures res == None ==> forall i :: 0 <= i < |items| ==> items[i].key != k
    ensures res == Some(v) ==> exists i :: 0 <= i < |items| && items[i].key == k && items[i].value == v
  {
    var i := 0;
    res := None;
    while i < |items|
      invariant 0 <= i <= |items|
      invariant Valid()
      invariant res == None ==> forall j :: 0 <= j < i ==> items[j].key != k
      decreases |items| - i
    {
      if items[i].key == k {
        res := Some(items[i].value);
        i := |items|; 
      } else {
        i := i + 1;
      }
    }
  }

  method Remove(k: int)
    requires Valid()
    modifies this
    ensures Valid()
    ensures forall i :: 0 <= i < |items| ==> items[i].key != k
  {
    var i := 0;
    var found := false;
    while i < |items| && !found
      invariant 0 <= i <= |items|
      invariant Valid()
      invariant !found ==> forall j :: 0 <= j < i ==> items[j].key != k
      decreases |items| - i
    {
      if items[i].key == k {
        items := items[..i] + items[i+1..];
        found := true;
      } else {
        i := i + 1;
      }
    }
  }
}