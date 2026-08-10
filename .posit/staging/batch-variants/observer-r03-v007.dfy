datatype Result<T> = Success(value: T) | Failure(error: string)
datatype StateChangeSub = StateChangeSub(id: int, ev: string)

predicate NoDupStateChange(subs: seq<StateChangeSub>)
{
  forall i, j :: 0 <= i < j < |subs| ==> !(subs[i].id == subs[j].id && subs[i].ev == subs[j].ev)
}

method PublishStateChange(subs: seq<StateChangeSub>, ev: string) returns (d: int)
  requires NoDupStateChange(subs)
  ensures 0 <= d <= |subs|
{
  d := 0; var i := 0;
  while i < |subs|
    invariant 0 <= i <= |subs| && d <= i && NoDupStateChange(subs)
    decreases |subs| - i
  {
    if subs[i].ev == ev { d := d + 1; }
    i := i + 1;
  }
}

method SubscribeStateChange(subs: seq<StateChangeSub>, id: int, ev: string) returns (r: Result<seq<StateChangeSub>>)
  requires NoDupStateChange(subs)
  ensures r.Success? ==> |r.value| == |subs| + 1 && NoDupStateChange(r.value)
  ensures r.Failure? ==> r.error == "dup"
{
  var i := 0; var f := false;
  while i < |subs| && !f
    invariant 0 <= i <= |subs| && NoDupStateChange(subs)
    invariant !f ==> forall k :: 0 <= k < i ==> !(subs[k].id == id && subs[k].ev == ev)
    decreases |subs| - i
  {
    if subs[i].id == id && subs[i].ev == ev { f := true; }
    i := i + 1;
  }
  if f { r := Failure("dup"); } else { r := Success(subs + [StateChangeSub(id, ev)]); }
}