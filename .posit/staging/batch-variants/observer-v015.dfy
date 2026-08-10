datatype Result<T> = Success(value: T) | Failure(error: string)
datatype Subscription = Sub(subscriberId: int, eventName: string, priority: int)

const EventType := "timer"

predicate NoDupSubs(subs: seq<Subscription>)
{
  forall i, j :: 0 <= i < j < |subs| ==>
    !(subs[i].subscriberId == subs[j].subscriberId && subs[i].eventName == subs[j].eventName)
}

method Publish(subs: seq<Subscription>, filterSubscriber: int) returns (delivered: int)
  requires NoDupSubs(subs)
  ensures 0 <= delivered <= |subs|
  decreases |subs|
{
  delivered := 0;
  var i := 0;
  while i < |subs|
    invariant 0 <= i <= |subs|
    invariant delivered <= i
    invariant NoDupSubs(subs)
    decreases |subs| - i
  {
    if subs[i].eventName == EventType && (filterSubscriber < 0 || subs[i].subscriberId == filterSubscriber) {
      delivered := delivered + 1;
    }
    i := i + 1;
  }
}

method Subscribe(subs: seq<Subscription>, subscriberId: int, priority: int) returns (result: Result<seq<Subscription>>)
  requires NoDupSubs(subs)
  ensures result.Success? ==> |result.value| == |subs| + 1
  ensures result.Success? ==> NoDupSubs(result.value)
  ensures result.Failure? ==> result.error == "duplicate subscription"
  decreases |subs|
{
  var i := 0;
  var found := false;
  while i < |subs|
    invariant 0 <= i <= |subs|
    invariant NoDupSubs(subs)
    invariant !found ==> (forall k :: 0 <= k < i ==> !(subs[k].subscriberId == subscriberId && subs[k].eventName == EventType))
    decreases |subs| - i
  {
    if !found && subs[i].subscriberId == subscriberId && subs[i].eventName == EventType {
      found := true;
    }
    i := i + 1;
  }
  if found {
    result := Failure("duplicate subscription");
  } else {
    result := Success(subs + [Sub(subscriberId, EventType, priority)]);
  }
}

method Unsubscribe(subs: seq<Subscription>, subscriberId: int) returns (result: Result<seq<Subscription>>)
  requires NoDupSubs(subs)
  ensures result.Success? ==> |result.value| == |subs| - 1
  ensures result.Success? ==> NoDupSubs(result.value)
  ensures result.Failure? ==> result.error == "not found"
  decreases |subs|
{
  var i := 0;
  var found := -1;
  while i < |subs|
    invariant 0 <= i <= |subs|
    invariant -1 <= found <= i
    invariant NoDupSubs(subs)
    invariant found >= 0 ==> (subs[found].subscriberId == subscriberId && subs[found].eventName == EventType)
    invariant found < 0 ==> (forall k :: 0 <= k < i ==> !(subs[k].subscriberId == subscriberId && subs[k].eventName == EventType))
    decreases |subs| - i
  {
    if found < 0 && subs[i].subscriberId == subscriberId && subs[i].eventName == EventType {
      found := i;
    }
    i := i + 1;
  }
  if found >= 0 {
    var newSubs := subs[..found] + subs[found+1..];
    result := Success(newSubs);
  } else {
    result := Failure("not found");
  }
}