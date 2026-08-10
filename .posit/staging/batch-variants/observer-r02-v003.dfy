datatype Result<T> = Success(value: T) | Failure(error: string)
datatype Subscription = Sub(subscriberId: int, eventName: string)

predicate NoDupSubs(subs: seq<Subscription>)
{
  forall i, j :: 0 <= i < j < |subs| ==>
    !(subs[i].subscriberId == subs[j].subscriberId && subs[i].eventName == subs[j].eventName)
}

method Publish(subs: seq<Subscription>, eventName: string) returns (delivered: int)
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
    if subs[i].eventName == eventName { delivered := delivered + 1; }
    i := i + 1;
  }
}

method Subscribe(subs: seq<Subscription>, sid: int, en: string) returns (r: Result<seq<Subscription>>)
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
  else { r := Success(subs + [Sub(sid, en)]); }
}

method Unsubscribe(subs: seq<Subscription>, sid: int, en: string) returns (r: Result<seq<Subscription>>)
  requires NoDupSubs(subs)
  ensures r.Success? ==> |r.value| == |subs| - 1
  ensures r.Success? ==> NoDupSubs(r.value)
  ensures r.Failure? ==> r.error == "not found"
  decreases |subs|
{
  var i := 0; var found := false; var idx := 0;
  while i < |subs| && !found
    invariant 0 <= i <= |subs|
    invariant found ==> 0 <= idx < |subs|
    invariant found ==> subs[idx].subscriberId == sid && subs[idx].eventName == en
    decreases |subs| - i
  {
    if subs[i].subscriberId == sid && subs[i].eventName == en { found := true; idx := i; }
    i := i + 1;
  }
  if found { r := Success(subs[..idx] + subs[idx + 1..]); }
  else { r := Failure("not found"); }
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