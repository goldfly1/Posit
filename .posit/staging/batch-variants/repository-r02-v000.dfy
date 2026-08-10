class Repository {
  var items: seq<int>

  constructor()
    ensures |items| == 0
  {
    items := [];
  }

  method Add(x: int)
    modifies this
    ensures items == old(items) + [x]
  {
    items := items + [x];
  }
}