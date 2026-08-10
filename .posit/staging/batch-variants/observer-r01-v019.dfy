class Observer {
  var lastEvent: int
  var notified: bool

  predicate Valid() reads this { lastEvent >= 0 }

  constructor()
    ensures Valid()
    ensures !notified
    ensures lastEvent == 0
  {
    lastEvent := 0;
    notified := false;
  }

  method Notify(e: int)
    requires Valid()
    requires e >= 0
    modifies this
    ensures Valid()
    ensures lastEvent == e
    ensures notified
  {
    lastEvent := e;
    notified := true;
  }
}

class Subject {
  var observers: seq<Observer>
  var lastEvent: int

  predicate Valid() reads this, set o <- observers
  {
    lastEvent >= 0 &&
    (forall o <- observers :: o != null && o.Valid()) &&
    (forall i, j :: 0 <= i < j < |observers| ==> observers[i] != observers[j])
  }

  constructor()
    ensures Valid()
  {
    observers := [];
    lastEvent := 0;
  }

  method Register(o: Observer)
    requires o != null && o.Valid()
    requires Valid()
    modifies this
    ensures Valid()
    ensures o in observers
    ensures |observers| == old(|observers|) + 1
  {
    observers := observers + [o];
  }

  method Unregister(o: Observer)
    requires Valid()
    requires o in observers
    modifies this
    ensures Valid()
    ensures o !in observers
    ensures |observers| == old(|observers|) - 1
  {
    var i := 0;
    var found := false;
    while i < |observers| && !found
      invariant 0 <= i <= |observers|
      invariant !found ==> forall k :: 0 <= k < i ==> observers[k] != o
      invariant Valid()
      invariant forall k, j :: 0 <= k < j < |observers| ==> observers[k] != observers[j]
      invariant exists k :: 0 <= k < |observers| && observers[k] == o
      decreases |observers| - i
    {
      if observers[i] == o {
        found := true;
      } else {
        i := i + 1;
      }
    }
    // found must be true because o is in observers
    assert found;
    assert 0 <= i < |observers|;
    assert observers[i] == o;
    observers := observers[..i] + observers[i+1..];
  }

  method NotifyAll(e: int)
    requires Valid()
    requires e >= 0
    modifies this, set o <- observers
    ensures Valid()
    ensures lastEvent == e
  {
    lastEvent := e;
    var i := 0;
    while i < |observers|
      invariant 0 <= i <= |observers|
      invariant Valid()
      invariant forall k, j :: 0 <= k < j < |observers| ==> observers[k] != observers[j]
      invariant forall k :: 0 <= k < |observers| ==> observers[k] != null
      invariant lastEvent == e
      decreases |observers| - i
    {
      observers[i].Notify(e);
      i := i + 1;
    }
  }
}