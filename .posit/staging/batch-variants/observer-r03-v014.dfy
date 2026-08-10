datatype Result<T> = Success(value: T) | Failure(error: string)
datatype DataUpdateSub = DataUpdateSub(id: int, ev: string, prio: int)

predicate NoDupDataUpdate(subs: seq<DataUpdateSub>)
{
  forall i, j :: 0 <= i < j < |subs| ==> !(subs[i].id == subs[j].id && subs[i].ev == subs[j].ev)
}

method PublishDataUpdate(subs: seq<DataUpdateSub>, ev: string, filter: string) returns (d: int)
  requires NoDupDataUpdate(subs)
  ensures 0 <= d <= |subs|
{
  d := 0; var i := 0;
  while i < |subs|
    invariant 0 <= i <= |subs| && d <= i && NoDupDataUpdate(subs)
    decreases |subs| - i
  {
    if subs[i].ev == ev && (|filter| == 0 || ev == filter) { d := d + 1; }
    i := i + 1;
  }
}

method SubscribeDataUpdate(subs: seq<DataUpdateSub>, id: int, ev: string, prio: int) returns (r: Result<seq<DataUpdateSub>>)
  requires NoDupDataUpdate(subs)
  ensures r.Success? ==> |r.value| == |subs| + 1 && NoDupDataUpdate(r.value)
  ensures r.Failure? ==> r.error == "dup"
{
  var i := 0; var f := false;
  while i < |subs| && !f
    invariant 0 <= i <= |subs| && NoDupDataUpdate(subs)
    invariant !f ==> forall k :: 0 <= k < i ==> !(subs[k].id == id && subs[k].ev == ev)
    decreases |subs| - i
  {
    if subs[i].id == id && subs[i].ev == ev { f := true; }
    i := i + 1;
  }
  if f { r := Failure("dup"); } else { r := Success(subs + [DataUpdateSub(id, ev, prio)]); }
}

method UnsubscribeDataUpdate(subs: seq<DataUpdateSub>, id: int, ev: string) returns (r: Result<seq<DataUpdateSub>>)
  requires NoDupDataUpdate(subs)
  ensures r.Success? ==> |r.value| == |subs| - 1 && NoDupDataUpdate(r.value)
  ensures r.Failure? ==> r.error == "not found"
{
  var i := 0; var f := false; var idx := 0;
  while i < |subs| && !f
    invariant 0 <= i <= |subs| && NoDupDataUpdate(subs)
    invariant !f ==> forall k :: 0 <= k < i ==> !(subs[k].id == id && subs[k].ev == ev)
    invariant f ==> 0 <= idx < |subs| && subs[idx].id == id && subs[idx].ev == ev
    decreases |subs| - i
  {
    if subs[i].id == id && subs[i].ev == ev { f := true; idx := i; }
    i := i + 1;
  }
  if f { r := Success(subs[..idx] + subs[idx+1..]); } else { r := Failure("not found"); }
}