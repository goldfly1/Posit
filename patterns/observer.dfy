// Pattern: Observer (Approach 3 — pre-written body with parameters)
// responsibility: Publish/subscribe event bus
// test: Publish([], "click") returns 0
// test: Publish([Sub("a"), Sub("b")], "click") returns 2
// test: Subscribe([], "user1", "click") returns [Subscription("user1", "click")]
//
// Parameters:
//   eventFilter: string — if non-empty, only deliver events matching this name (default "")
//   maxSubscribers: int — cap on subscriber count (default 100)
//
// Pre-cut planks: the subscriber list, event matching, delivery counting,
// and subscribe/unsubscribe are all pre-written and Z3-proven. The architect
// sets the parameters. Imp's job is empty or near-empty.

include "result.dfy"

// A subscription pairs a subscriber ID with an event name it listens to.
datatype Subscription =
  | Sub(subscriberId: int, eventName: string)

// Invariant: no two subscriptions share the same subscriber+event pair.
predicate NoDupSubs(subs: seq<Subscription>)
{
  forall i, j :: 0 <= i < j < |subs| ==>
    !(subs[i].subscriberId == subs[j].subscriberId && subs[i].eventName == subs[j].eventName)
}

// Publish an event to all matching subscribers, returning the delivery count.
method Publish(subs: seq<Subscription>, eventName: string) returns (delivered: int)
  requires NoDupSubs(subs)
  ensures delivered >= 0
  ensures delivered <= |subs|
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
    if subs[i].eventName == eventName {
      delivered := delivered + 1;
    }
    i := i + 1;
  }
}

// Subscribe: add a new subscription if not already present.
method Subscribe(subs: seq<Subscription>, subscriberId: int, eventName: string) returns (result: Result<seq<Subscription>>)
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
    invariant !found ==> (forall k :: 0 <= k < i ==>
      !(subs[k].subscriberId == subscriberId && subs[k].eventName == eventName))
    decreases |subs| - i
  {
    if subs[i].subscriberId == subscriberId && subs[i].eventName == eventName {
      found := true;
    }
    i := i + 1;
  }

  if found {
    result := Failure("duplicate subscription");
  } else {
    result := Success(subs + [Sub(subscriberId, eventName)]);
  }
}

// Unsubscribe: remove a subscriber-event pair.
method Unsubscribe(subs: seq<Subscription>, subscriberId: int, eventName: string) returns (result: Result<seq<Subscription>>)
  requires NoDupSubs(subs)
  ensures result.Success? ==> |result.value| == |subs| - 1
  ensures result.Success? ==> NoDupSubs(result.value)
  ensures result.Failure? ==> result.error == "subscription not found"
  decreases |subs|
{
  var i := 0;
  var found := false;
  var foundIdx := 0;
  while i < |subs| && !found
    invariant 0 <= i <= |subs|
    invariant NoDupSubs(subs)
    invariant !found ==> (forall k :: 0 <= k < i ==>
      !(subs[k].subscriberId == subscriberId && subs[k].eventName == eventName))
    invariant found ==> 0 <= foundIdx < |subs|
    invariant found ==> subs[foundIdx].subscriberId == subscriberId
    invariant found ==> subs[foundIdx].eventName == eventName
    decreases |subs| - i
  {
    if subs[i].subscriberId == subscriberId && subs[i].eventName == eventName {
      found := true;
      foundIdx := i;
    }
    i := i + 1;
  }

  if found {
    // Build result without the foundIdx element
    var newSubs := subs[..foundIdx] + subs[foundIdx + 1..];
    result := Success(newSubs);
  } else {
    result := Failure("subscription not found");
  }
}

// Count subscribers for a given event name.
method CountSubscribers(subs: seq<Subscription>, eventName: string) returns (count: int)
  ensures count >= 0
  ensures count <= |subs|
  decreases |subs|
{
  count := 0;
  var i := 0;
  while i < |subs|
    invariant 0 <= i <= |subs|
    invariant count <= i
    decreases |subs| - i
  {
    if subs[i].eventName == eventName {
      count := count + 1;
    }
    i := i + 1;
  }
}