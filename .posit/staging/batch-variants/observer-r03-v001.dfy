datatype Result<T> = Success(value: T) | Failure(error: string)
datatype ClickSub = ClickSub(id: int, ev: string, prio: int)

predicate NoDupClick(subs: seq<ClickSub>)
{
  forall i, j :: 0 <= i < j < |subs| ==> !(subs[i].id == subs[j].id && subs[i].ev == subs[j].ev)
}

method PublishClick(subs: seq<ClickSub>, ev: string, filter: string) returns (d: int)
  requires NoDupClick(subs)
  ensures 0 <= d <= |subs|
{
  d := 0; var i := 0;
  while i < |subs|
    invariant 0 <= i <= |subs| && d <= i && NoDupClick(subs)
    decreases |subs| - i
  {
    if subs[i].ev == ev && (|filter| == 0 || ev == filter) { d := d + 1; }
    i := i + 1;
  }
}

method SubscribeClick(subs: seq<ClickSub>, id: int, ev: string, prio: int) returns (r: Result<seq<ClickSub>>)
  requires NoDupClick(subs)
  ensures r.Success? ==> |r.value| == |subs| + 1 && NoDupClick(r.value)
  ensures r.Failure? ==> r.error == "dup"
{
  var i := 0; var f := false;
  while i < |subs| && !f
    invariant 0 <= i <= |subs| && NoDupClick(subs)
    invariant !f ==> forall k :: 0 <= k < i ==> !(subs[k].id == id && subs[k].ev == ev)
    decreases |subs| - i
  {
    if subs[i].id == id && subs[i].ev == ev { f := true; }
    i := i + 1;
  }
  if f { r := Failure("dup"); } else { r := Success(subs + [ClickSub(id, ev, prio)]); }
}