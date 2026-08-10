datatype Result<T> = Success(value: T) | Failure(error: string)
datatype Subscription = Sub(subscriberId: int, eventName: string, priority: int)

predicate NoDupSubs(subs: seq<Subscription>)
{
  forall i, j :: 0 <= i < j < |subs| ==>
    !(subs[i].subscriberId == subs[j].subscriberId && subs[i].eventName == subs[j].eventName)
}

method PublishMessageByPriority(subs: seq<Subscription>, eventName: string, minPriority: int) returns (delivered: int)
  requires NoDupSubs(subs)
  ensures 0 <= delivered <= |subs|
  decreases |subs|
{
  delivered := 0; var i := 0;
  while i < |subs|
    invariant 0 <= i <= |subs|
    invariant delivered <= i
    decreases |subs| - i
  {
    if subs[i].eventName == eventName && subs[i].priority >= minPriority {
      delivered := delivered + 1;
    }
    i := i + 1;
  }
}

method SubscribeMessage(subs: seq<Subscription>, sid: int, en: string, prio: int) returns (r: Result<seq<Subscription>>)
  requires NoDupSubs(subs)
  ensures r.Success? ==> |r.value| == |subs| + 1
  ensures r.Success? ==> NoDupSubs(r.value)
  ensures r.Failure? ==> r.error == "duplicate"
  decreases |subs|
{
  var i := 0; var found := false;
  while i < |subs| && !found
    invariant 0 <= i <= |subs|
    invariant !found ==> forall k :: 0 <= k < i ==> !(subs[k].subscriberId == sid && subs[k].eventName == en)
    decreases |subs| - i
  {
    if subs[i].subscriberId == sid && subs[i].eventName == en { found := true; }
    i := i + 1;
  }
  if found { r := Failure("duplicate"); }
  else { r := Success(subs + [Sub(sid, en, prio)]); }
}

method CountSubscribers(subs: seq<Subscription>, eventName: string) returns (count: int)
  ensures 0 <= count <= |subs|
  decreases |subs|
{
  count := 0; var i := 0;
  while i < |subs|
    invariant 0 <= i <= |subs|
    invariant count <= i
    decreases |subs| - i
  {
    if subs[i].eventName == eventName { count := count + 1; }
    i := i + 1;
  }
}