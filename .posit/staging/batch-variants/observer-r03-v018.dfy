datatype Result<T> = Success(value: T) | Failure(error: string)
datatype TimerSub = TimerSub(id: int, ev: string, prio: int)

predicate NoDupTimer(subs: seq<TimerSub>)
{
  forall i, j :: 0 <= i < j < |subs| ==> !(subs[i].id == subs[j].id && subs[i].ev == subs[j].ev)
}

method PublishTimer(subs: seq<TimerSub>, ev: string, filter: string) returns (d: int)
  requires NoDupTimer(subs)
  ensures 0 <= d <= |subs|
{
  d := 0; var i := 0;
  while i < |subs|
    invariant 0 <= i <= |subs| && d <= i && NoDupTimer(subs)
    decreases |subs| - i
  {
    if subs[i].ev == ev && (|filter| == 0 || ev == filter) { d := d + 1; }
    i := i + 1;
  }
}

method SubscribeTimer(subs: seq<TimerSub>, id: int, ev: string, prio: int) returns (r: Result<seq<TimerSub>>)
  requires NoDupTimer(subs)
  ensures r.Success? ==> |r.value| == |subs| + 1 && NoDupTimer(r.value)
  ensures r.Failure? ==> r.error == "dup"
{
  var i := 0; var f := false;
  while i < |subs| && !f
    invariant 0 <= i <= |subs| && NoDupTimer(subs)
    invariant !f ==> forall k :: 0 <= k < i ==> !(subs[k].id == id && subs[k].ev == ev)
    decreases |subs| - i
  {
    if subs[i].id == id && subs[i].ev == ev { f := true; }
    i := i + 1;
  }
  if f { r := Failure("dup"); } else { r := Success(subs + [TimerSub(id, ev, prio)]); }
}

method UnsubscribeTimer(subs: seq<TimerSub>, id: int, ev: string) returns (r: Result<seq<TimerSub>>)
  requires NoDupTimer(subs)
  ensures r.Success? ==> |r.value| == |subs| - 1 && NoDupTimer(r.value)
  ensures r.Failure? ==> r.error == "not found"
{
  var i := 0; var f := false; var idx := 0;
  while i < |subs| && !f
    invariant 0 <= i <= |subs| && NoDupTimer(subs)
    invariant !f ==> forall k :: 0 <= k < i ==> !(subs[k].id == id && subs[k].ev == ev)
    invariant f ==> 0 <= idx < |subs| && subs[idx].id == id && subs[idx].ev == ev
    decreases |subs| - i
  {
    if subs[i].id == id && subs[i].ev == ev { f := true; idx := i; }
    i := i + 1;
  }
  if f { r := Success(subs[..idx] + subs[idx+1..]); } else { r := Failure("not found"); }
}