datatype Result<T> = Success(value: T) | Failure(error: string)
datatype MessageSub = MessageSub(id: int, ev: string)

predicate NoDupMessage(subs: seq<MessageSub>)
{
  forall i, j :: 0 <= i < j < |subs| ==> !(subs[i].id == subs[j].id && subs[i].ev == subs[j].ev)
}

method PublishMessage(subs: seq<MessageSub>, ev: string) returns (d: int)
  requires NoDupMessage(subs)
  ensures 0 <= d <= |subs|
{
  d := 0; var i := 0;
  while i < |subs|
    invariant 0 <= i <= |subs| && d <= i && NoDupMessage(subs)
    decreases |subs| - i
  {
    if subs[i].ev == ev { d := d + 1; }
    i := i + 1;
  }
}

method SubscribeMessage(subs: seq<MessageSub>, id: int, ev: string) returns (r: Result<seq<MessageSub>>)
  requires NoDupMessage(subs)
  ensures r.Success? ==> |r.value| == |subs| + 1 && NoDupMessage(r.value)
  ensures r.Failure? ==> r.error == "dup"
{
  var i := 0; var f := false;
  while i < |subs| && !f
    invariant 0 <= i <= |subs| && NoDupMessage(subs)
    invariant !f ==> forall k :: 0 <= k < i ==> !(subs[k].id == id && subs[k].ev == ev)
    decreases |subs| - i
  {
    if subs[i].id == id && subs[i].ev == ev { f := true; }
    i := i + 1;
  }
  if f { r := Failure("dup"); } else { r := Success(subs + [MessageSub(id, ev)]); }
}