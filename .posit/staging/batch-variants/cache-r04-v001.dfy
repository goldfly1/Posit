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
}