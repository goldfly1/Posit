datatype Pair = Pair(key: int, value: int)

class Cache {
  var items: seq<Pair>

  constructor()
    ensures |items| == 0
  {
    items := [];
  }
}