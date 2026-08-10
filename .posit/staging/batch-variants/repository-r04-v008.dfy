datatype Result<T> = Success(value: T) | Failure(error: string)

datatype User = User(id: string, name: string)

predicate NoDuplicates(items: seq<User>)
{
  forall i, j :: 0 <= i < j < |items| ==> items[i].id != items[j].id
}

lemma NoDuplicatesSubseq(items: seq<User>, idx: int)
  requires NoDuplicates(items)
  requires 0 <= idx < |items|
  ensures NoDuplicates(items[..idx] + items[idx+1..])
  decreases |items|
{
  forall k, l | 0 <= k < l < |items[..idx] + items[idx+1..]| {
    if l < idx {
      assert (items[..idx] + items[idx+1..])[k] == items[k];
      assert (items[..idx] + items[idx+1..])[l] == items[l];
    } else if k < idx && l >= idx {
      assert (items[..idx] + items[idx+1..])[k] == items[k];
      assert (items[..idx] + items[idx+1..])[l] == items[l + 1];
    } else {
      assert (items[..idx] + items[idx+1..])[k] == items[k + 1];
      assert (items[..idx] + items[idx+1..])[l] == items[l + 1];
    }
  }
}

method Add(items: seq<User>, entity: User) returns (result: Result<seq<User>>)
  requires NoDuplicates(items)
  ensures result.Success? ==> |result.value| == |items| + 1
  ensures result.Failure? ==> result.error == "duplicate id"
  decreases |items|
{
  var i := 0;
  var found := false;
  while i < |items| && !found
    invariant 0 <= i <= |items|
    invariant NoDuplicates(items)
    decreases |items| - i
  {
    if items[i].id == entity.id {
      found := true;
    }
    i := i + 1;
  }
  if found {
    result := Failure("duplicate id");
  } else {
    result := Success(items + [entity]);
  }
}

method Remove(items: seq<User>, id: string) returns (result: Result<seq<User>>)
  requires NoDuplicates(items)
  ensures result.Success? ==> |result.value| == |items| - 1
  ensures result.Success? ==> NoDuplicates(result.value)
  ensures result.Failure? ==> result.error == "not found"
  decreases |items|
{
  var i := 0;
  var found := false;
  var idx := 0;
  while i < |items| && !found
    invariant 0 <= i <= |items|
    invariant 0 <= idx <= i
    invariant found ==> idx < i && items[idx].id == id
    invariant !found ==> idx == 0
    decreases |items| - i
  {
    if items[i].id == id {
      found := true;
      idx := i;
    }
    i := i + 1;
  }
  if found {
    NoDuplicatesSubseq(items, idx);
    result := Success(items[..idx] + items[idx+1..]);
  } else {
    result := Failure("not found");
  }
}