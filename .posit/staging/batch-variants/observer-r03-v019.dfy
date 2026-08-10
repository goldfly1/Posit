datatype Event = Value(x: int) | Reset

class Observer {
  var history: seq<int>
  
  predicate Valid() reads this { true }
  
  constructor()
    ensures Valid()
  {
    history := [];
  }
  
  method Notify(e: Event)
    requires Valid()
    modifies this
    ensures Valid()
    ensures e.Value? ==> history == old(history) + [e.x]
    ensures e.Reset? ==> history == []
  {
    match e {
      case Value(x) => 
        history := history + [x];
      case Reset => 
        history := [];
    }
  }
  
  method Sum() returns (s: int)
    requires Valid()
    ensures |history| == 0 ==> s == 0
  {
    s := 0;
    var i := 0;
    while i < |history|
      invariant 0 <= i <= |history|
      invariant |history| == 0 ==> s == 0
      decreases |history| - i
    {
      s := s + history[i];
      i := i + 1;
    }
  }
  
  method Count() returns (c: int)
    requires Valid()
    ensures c == |history|
  {
    c := |history|;
  }
}

method {:axiom} TestObserver(o: Observer, e: Event)
  requires o.Valid()
  modifies o
  ensures o.Valid()