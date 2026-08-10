datatype Event = Message
datatype Subscription = Sub(id: int, event: Event, priority: int)
datatype Result<T> = Success(value: T) | Failure(error: string)

predicate NoDupSubs(subs: seq<Subscription>)
{
  forall i, j :: 0 <= i < j < |subs| ==> !(subs[i].id == subs[j].id && subs[i].event == subs[j].event)
}

method Publish(subs: seq<Subscription>, e: Event) returns (delivered: int)
  requires NoDupSubs(subs)
  ensures 0 <= delivered <= |subs|
{
  delivered := 0;
  var i := 0;
  while i < |subs|
    invariant 0 <= i <= |subs|
    invariant delivered <= i
    invariant NoDupSubs(subs)
    decreases |subs| - i
  {
    if subs[i].event == e {
      delivered := delivered + 1;
    }
    i := i + 1;
  }
}

method Subscribe(subs: seq<Subscription>, id: int, e: Event, p: int) returns (r: Result<seq<Subscription>>)
  requires NoDupSubs(subs)
  ensures r.Success? ==> NoDupSubs(r.value)
  ensures r.Success? ==> |r.value| == |subs| + 1
  ensures r.Failure? ==> r.error == "duplicate"
{
  var i := 0;
  var found := false;
  while i < |subs| && !found
    invariant 0 <= i <= |subs|
    invariant NoDupSubs(subs)
    invariant !found ==> (forall k :: 0 <= k < i ==> !(subs[k].id == id && subs[k].event == e))
    decreases |subs| - i
  {
    if subs[i].id == id && subs[i].event == e {
      found := true;
    }
    i := i + 1;
  }
  if found {
    r := Failure("duplicate");
  } else {
    r := Success(subs + [Sub(id, e, p)]);
  }
}

method Unsubscribe(subs: seq<Subscription>, id: int, e: Event) returns (r: Result<seq<Subscription>>)
  requires NoDupSubs(subs)
  ensures r.Success? ==> NoDupSubs(r.value)
  ensures r.Success? ==> |r.value| == |subs| - 1
  ensures r.Failure? ==> r.error == "not found"
{
  var i := 0;
  var found := false;
  var idx := 0;
  while i < |subs| && !found
    invariant 0 <= i <= |subs|
    invariant NoDupSubs(subs)
    invariant !found ==> (forall k :: 0 <= k < i ==> !(subs[k].id == id && subs[k].event == e))
    invariant found ==> 0 <= idx < |subs|
    invariant found ==> subs[idx].id == id && subs[idx].event == e
    decreases |subs| - i
  {
    if subs[i].id == id && subs[i].event == e {
      found := true;
      idx := i;
    }
    i := i + 1;
  }
  if found {
    r := Success(subs[..idx] + subs[idx + 1..]);
  } else {
    r := Failure("not found");
  }
}