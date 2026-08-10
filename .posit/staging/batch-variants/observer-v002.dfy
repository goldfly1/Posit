datatype Result<T> = Success(value: T) | Failure(error: string)
datatype Subscription = Sub(subscriberId: int, eventName: string, priority: int)

const EventType := "click"

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
  while i < |subs| && !found
    invariant 0 <= i <= |subs|
    invariant NoDupSubs(subs)
    invariant !found ==> (forall k :: 0 <= k < i ==> !(subs[k].subscriberId == subscriberId && subs[k].eventName == EventType))
    decreases |subs| - i
  {
    if subs[i].subscriberId == subscriberId && subs[i].eventName == EventType {
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

method HighestPrioritySubscriber(subs: seq<Subscription>) returns (result: Result<int>)
  requires NoDupSubs(subs)
  ensures result.Success? ==> 0 <= result.value < |subs|
  decreases |subs|
{
  if |subs| == 0 {
    result := Failure("no subscribers");
  } else {
    var bestIdx := 0;
    var i := 1;
    while i < |subs|
      invariant 0 <= bestIdx < |subs|
      invariant 0 <= i <= |subs|
      invariant bestIdx < i
      invariant forall k :: 0 <= k < i ==> subs[k].priority <= subs[bestIdx].priority
      decreases |subs| - i
    {
      if subs[i].priority > subs[bestIdx].priority {
        bestIdx := i;
      }
      i := i + 1;
    }
    result := Success(bestIdx);
  }
}