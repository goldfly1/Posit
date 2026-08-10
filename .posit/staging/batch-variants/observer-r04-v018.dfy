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

method GetSubscribers(subs: seq<Subscription>, eventName: string) returns (ids: seq<int>)
  requires NoDupSubs(subs)
  ensures |ids| <= |subs|
  decreases |subs|
{
  var i := 0;
  ids := [];
  while i < |subs|
    invariant 0 <= i <= |subs|
    invariant NoDupSubs(subs)
    invariant |ids| <= i
    decreases |subs| - i
  {
    if subs[i].active && subs[i].eventName == eventName {
      ids := ids + [subs[i].subscriberId];
    }
    i := i + 1;
  }
}