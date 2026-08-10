datatype Result<T> = Success(value: T) | Failure(error: string)
datatype Subscription = Sub(subscriberId: int, eventName: string, active: bool, priority: int)

predicate NoDupSubs(subs: seq<Subscription>)
{
  forall i, j :: 0 <= i < j < |subs| ==>
    !(subs[i].subscriberId == subs[j].subscriberId && subs[i].eventName == subs[j].eventName)
}

predicate ValidPriorities(subs: seq<Subscription>)
{
  forall i :: 0 <= i < |subs| ==> subs[i].priority >= 0
}

method SetPriority(subs: seq<Subscription>, subscriberId: int, eventName: string, newPriority: int) returns (result: Result<seq<Subscription>>)
  requires NoDupSubs(subs)
  requires ValidPriorities(subs)
  requires newPriority >= 0
  ensures result.Success? ==> |result.value| == |subs|
  ensures result.Success? ==> NoDupSubs(result.value)
  ensures result.Success? ==> ValidPriorities(result.value)
  ensures result.Failure? ==> result.error == "not found"
  decreases |subs|
{
  var i := 0;
  var found := false;
  var newSubs := [];
  while i < |subs|
    invariant 0 <= i <= |subs|
    invariant NoDupSubs(subs)
    invariant ValidPriorities(subs)
    invariant NoDupSubs(newSubs)
    invariant ValidPriorities(newSubs)
    invariant |newSubs| == i
    invariant forall k :: 0 <= k < |newSubs| ==> 
      (newSubs[k].subscriberId == subs[k].subscriberId && newSubs[k].eventName == subs[k].eventName)
    decreases |subs| - i
  {
    if subs[i].subscriberId == subscriberId && subs[i].eventName == eventName {
      found := true;
      newSubs := newSubs + [Sub(subscriberId, eventName, subs[i].active, newPriority)];
    } else {
      newSubs := newSubs + [subs[i]];
    }
    i := i + 1;
  }
  if found {
    result := Success(newSubs);
  } else {
    result := Failure("not found");
  }
}