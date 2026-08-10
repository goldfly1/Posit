datatype Result<T> = Success(value: T) | Failure(error: string)
datatype User = User(id: int, name: string)

predicate NoDuplicates(items: seq<User>)
{
  forall i, j :: 0 <= i < j < |items| ==> items[i].id != items[j].id
}

method Add(items: seq<User>, entity: User) returns (result: Result<seq<User>>)
  requires NoDuplicates(items)
  ensures result.Success?
  ensures result.Success? ==> |result.value| == |items| || |result.value| == |items| + 1
{
  var i := 0;
  var found := false;
  while i < |items| && !found
    invariant 0 <= i <= |items|
    invariant NoDuplicates(items)
    invariant !found ==> (forall k :: 0 <= k < i ==> items[k].id != entity.id)
    invariant found ==> i < |items|
    invariant found ==> items[i].id == entity.id
    decreases |items| - i + (if found then 0 else 1)
  {
    if items[i].id == entity.id {
      found := true;
    } else {
      i := i + 1;
    }
  }
  if found {
    assert 0 <= i < |items|;
    assert 0 <= i+1 <= |items|;
    result := Success(items[..i] + [entity] + items[i+1..]);
  } else {
    result := Success(items + [entity]);
  }
}

method Remove(items: seq<User>, id: int) returns (result: Result<seq<User>>)
  requires NoDuplicates(items)
  ensures result.Success? ==> |result.value| <= |items|
  ensures result.Failure? ==> result.error == "not found"
{
  var i := 0;
  var found := false;
  while i < |items| && !found
    invariant 0 <= i <= |items|
    invariant NoDuplicates(items)
    invariant !found ==> (forall k :: 0 <= k < i ==> items[k].id != id)
    invariant found ==> i < |items|
    invariant found ==> items[i].id == id
    decreases |items| - i + (if found then 0 else 1)
  {
    if items[i].id == id {
      found := true;
    } else {
      i := i + 1;
    }
  }
  if found {
    assert 0 <= i < |items|;
    assert 0 <= i+1 <= |items|;
    result := Success(items[..i] + items[i+1..]);
  } else {
    result := Failure("not found");
  }
}