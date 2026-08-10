datatype Pair = Pair(key: int, value: int)

class Cache {
  var items: seq<Pair>

  constructor()
    ensures |items| == 0
  {
    items := [];
  }

  method Add(k: int, v: int)
    modifies this
    ensures |items| == old(|items|) + 1
    ensures items[|items|-1] == Pair(k, v)
  {
    items := items + [Pair(k, v)];
  }

  method Get(k: int) returns (v: int)
    requires exists i :: 0 <= i < |items| && items[i].key == k
    ensures exists i :: 0 <= i < |items| && items[i].key == k && items[i].value == v
  {
    var i := 0;
    while i < |items|
      invariant 0 <= i <= |items|
      invariant exists j :: i <= j < |items| && items[j].key == k
      decreases |items| - i
    {
      if items[i].key == k {
        v := items[i].value;
        return;
      }
      i := i + 1;
    }
  }

  method Update(k: int, v: int)
    requires exists i :: 0 <= i < |items| && items[i].key == k
    modifies this
    ensures exists i :: 0 <= i < |items| && items[i].key == k && items[i].value == v
    ensures |items| == old(|items|)
  {
    var i := 0;
    while i < |items|
      invariant 0 <= i <= |items|
      invariant exists j :: i <= j < |items| && items[j].key == k
      decreases |items| - i
    {
      if items[i].key == k {
        items := items[..i] + [Pair(k, v)] + items[i+1..];
        assert items[i] == Pair(k, v);
        return;
      }
      i := i + 1;
    }
  }
}