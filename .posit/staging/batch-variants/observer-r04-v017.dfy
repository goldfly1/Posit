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

method CountActive(subs: seq<Subscription>) returns (count: int)
  requires NoDupSubs(subs)
  ensures 0 <= count <= |subs|
  decreases |subs|
{
  count := 0;
  var i := 0;
  while i < |subs|
    invariant 0 <= i <= |subs|
    invariant 0 <= count <= i
    invariant NoDupSubs(subs)
    decreases |subs| - i
  {
    if subs[i].active {
      count := count + 1;
    }
    i := i + 1;
  }
}