datatype Result<T> = Success(value: T) | Failure(error: string)
datatype Subscription = Sub(subscriberId: int, eventName: string)

predicate NoDupSubs(subs: seq<Subscription>)
{
  forall i, j :: 0 <= i < j < |subs| ==>
    !(subs[i].subscriberId == subs[j].subscriberId && subs[i].eventName == subs[j].eventName)
}

function DefaultEvent(): string { "message" }

method Publish(subs: seq<Subscription>, eventName: string) returns (delivered: int)
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
    if subs[i].eventName == eventName { delivered := delivered + 1; }
    i := i + 1;
  }
}

method Subscribe(subs: seq<Subscription>, subscriberId: int, eventName: string) returns (result: Result<seq<Subscription>>)
  requires NoDupSubs(subs)
  ensures result.Success? ==> |result.value| == |subs| + 1
  ensures result.Success? ==> NoDupSubs(result.value)
  ensures result.Failure? ==> result.error == "duplicate"
  decreases |subs|
{
  var i := 0;
  var found := false;
  while i < |subs| && !found
    invariant 0 <= i <= |subs|
    invariant NoDupSubs(subs)
    invariant !found ==> (forall k :: 0 <= k < i ==> !(subs[k].subscriberId == subscriberId && subs[k].eventName == eventName))
    decreases |subs| - i
  {
    if subs[i].subscriberId == subscriberId && subs[i].eventName == eventName { found := true; }
    i := i + 1;
  }
  if found { result := Failure("duplicate"); }
  else { result := Success(subs + [Sub(subscriberId, eventName)]); }
}

method CountSubscribers(subs: seq<Subscription>, eventName: string) returns (count: int)
  ensures 0 <= count <= |subs|
  decreases |subs|
{
  count := 0;
  var i := 0;
  while i < |subs|
    invariant 0 <= i <= |subs|
    invariant count <= i
    decreases |subs| - i
  {
    if subs[i].eventName == eventName { count := count + 1; }
    i := i + 1;
  }
}