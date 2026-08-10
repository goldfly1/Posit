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

method Add(items: seq<User>, entity: User) returns (result: Result<seq<User>>)
  requires NoDuplicates(items)
  ensures result.Success? ==> |result.value| == |items| + 1
  ensures result.Failure? ==> result.error == "duplicate id"
  ensures result.Success? ==> CountById(result.value, entity.id) == 1
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

method Size(items: seq<User>) returns (n: int)
  ensures n == |items|
  ensures n == Count(items)
  decreases |items|
{
  n := Count(items);
}