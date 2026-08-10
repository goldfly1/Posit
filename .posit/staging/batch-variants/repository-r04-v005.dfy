datatype Result<T> = Success(value: T) | Failure(error: string)

datatype User = User(id: int, name: string)

predicate NoDuplicates(items: seq<User>)
{
  forall i, j :: 0 <= i < j < |items| ==> items[i].id != items[j].id
}

function Count(items: seq<User>): int
  ensures Count(items) == |items|
  decreases |items|
{
  if |items| == 0 then 0 else 1 + Count(items[1..])
}

function CountById(items: seq<User>, id: int): int
  ensures 0 <= CountById(items, id) <= |items|
  decreases |items|
{
  if |items| == 0 then 0
  else if items[0].id == id then 1 + CountById(items[1..], id)
  else CountById(items[1..], id)
}

lemma CountByIdZero(items: seq<User>, id: int)
  requires forall k :: 0 <= k < |items| ==> items[k].id != id
  ensures CountById(items, id) == 0
  decreases |items|
{
  if |items| > 0 {
    CountByIdZero(items[1..], id);
  }
}

lemma CountByIdAppend(items: seq<User>, e: User, id: int)
  ensures CountById(items + [e], id) == CountById(items, id) + (if e.id == id then 1 else 0)
  decreases |items|
{
  if |items| > 0 {
    assert (items + [e])[0] == items[0];
    assert (items + [e])[1..] == items[1..] + [e];
    CountByIdAppend(items[1..], e, id);
  }
}

method Add(items: seq<User>, entity: User) returns (result: Result<seq<User>>)
  requires NoDuplicates(items)
  ensures result.Success? ==> |result.value| == |items| + 1
  ensures result.Failure? ==> result.error == "duplicate id"
  ensures result.Success? ==> CountById(result.value, entity.id) == 1
{
  var i := 0;
  var found := false;
  while i < |items| && !found
    invariant 0 <= i <= |items|
    invariant NoDuplicates(items)
    invariant !found ==> forall k :: 0 <= k < i ==> items[k].id != entity.id
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
    CountByIdZero(items, entity.id);
    CountByIdAppend(items, entity, entity.id);
    result := Success(items + [entity]);
  }
}

method Size(items: seq<User>) returns (n: int)
  ensures n == |items|
  ensures n >= 0
{
  n := Count(items);
}