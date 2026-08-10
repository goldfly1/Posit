datatype Result<T> = Success(value: T) | Failure(error: string)

datatype User = User(id: string, name: string)

predicate NoDuplicates(items: seq<User>)
{
  forall i, j :: 0 <= i < j < |items| ==> items[i].id != items[j].id
}

predicate SortedByName(items: seq<User>)
{
  forall i, j :: 0 <= i < j < |items| ==> items[i].name <= items[j].name
}

function InsertSorted(items: seq<User>, e: User): seq<User>
  requires SortedByName(items)
  ensures |InsertSorted(items, e)| == |items| + 1
  ensures SortedByName(InsertSorted(items, e))
  ensures multiset(InsertSorted(items, e)) == multiset(items) + multiset([e])
  decreases |items|
{
  if |items| == 0 then
    [e]
  else if e.name <= items[0].name then
    [e] + items
  else
    [items[0]] + InsertSorted(items[1..], e)
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

method Sort(items: seq<User>) returns (sorted: seq<User>)
  ensures |sorted| == |items|
  ensures SortedByName(sorted)
  ensures multiset(sorted) == multiset(items)
  decreases |items|
{
  sorted := [];
  var i := 0;
  while i < |items|
    invariant 0 <= i <= |items|
    invariant SortedByName(sorted)
    invariant multiset(sorted) == multiset(items[..i])
    decreases |items| - i
  {
    sorted := InsertSorted(sorted, items[i]);
    i := i + 1;
  }
}