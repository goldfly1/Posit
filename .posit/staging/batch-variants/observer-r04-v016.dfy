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

method Unsubscribe(subs: seq<Subscription>, subscriberId: int, eventName: string) returns (newSubs: seq<Subscription>)
  requires NoDupSubs(subs)
  ensures NoDupSubs(newSubs)
  ensures |newSubs| <= |subs|
  decreases |subs|
{
  var i := 0;
  var res := [];
  while i < |subs|
    invariant 0 <= i <= |subs|
    invariant NoDupSubs(subs)
    invariant NoDupSubs(res)
    invariant |res| <= i
    invariant forall k :: 0 <= k < |res| ==> exists m :: 0 <= m < i && subs[m] == res[k]
    decreases |subs| - i
  {
    if subs[i].subscriberId == subscriberId && subs[i].eventName == eventName {
      // skip
    } else {
      res := res + [subs[i]];
    }
    i := i + 1;
  }
  newSubs := res;
}