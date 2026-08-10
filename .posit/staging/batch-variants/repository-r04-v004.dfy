datatype Result<T> = Success(value: T) | Failure(error: string)

datatype User = User(id: int, name: string)

predicate NoDuplicates(items: seq<User>)
{
  forall i, j :: 0 <= i < j < |items| ==> items[i].id != items[j].id
}

predicate SortedById(items: seq<User>)
{
  forall i :: 0 <= i < |items| - 1 ==> items[i].id <= items[i + 1].id
}

lemma SortedByIdTailLemma(items: seq<User>)
  requires SortedById(items)
  requires |items| > 0
  ensures SortedById(items[1..])
  decreases |items|
{
  if |items| > 1 {
    SortedByIdTailLemma(items[1..]);
  }
}

lemma SortedByIdConsLemma(e: User, rest: seq<User>)
  requires SortedById(rest)
  requires |rest| == 0 || e.id <= rest[0].id
  ensures SortedById([e] + rest)
{
}

lemma InsertSortedHeadGE(items: seq<User>, e: User, bound: int)
  requires SortedById(items)
  requires |items| > 0 ==> items[0].id >= bound
  requires e.id >= bound
  ensures InsertSorted(items, e)[0].id >= bound
  decreases |items|
{
  if |items| > 0 && items[0].id < e.id {
    SortedByIdTailLemma(items);
    InsertSortedHeadGE(items[1..], e, items[0].id);
  }
}

function InsertSorted(items: seq<User>, e: User): seq<User>
  requires SortedById(items)
  ensures |InsertSorted(items, e)| == |items| + 1
  ensures SortedById(InsertSorted(items, e))
  ensures multiset(InsertSorted(items, e)) == multiset(items) + multiset([e])
  decreases |items|
{
  if |items| == 0 then
    [e]
  else if e.id <= items[0].id then
    SortedByIdConsLemma(e, items);
    [e] + items
  else
    SortedByIdTailLemma(items);
    var rest := InsertSorted(items[1..], e);
    InsertSortedHeadGE(items[1..], e, items[0].id);
    SortedByIdConsLemma(items[0], rest);
    [items[0]] + rest
}

method Add(items: seq<User>, entity: User) returns (result: Result<seq<User>>)
  requires NoDuplicates(items)
  ensures result.Success? ==> |result.value| == |items| + 1
  ensures result.Failure? ==> result.error == "duplicate id"
{
  var i := 0;
  var found := false;
  while i < |items| && !found
    invariant 0 <= i <= |items|
    invariant !found ==> forall k :: 0 <= k < i ==> items[k].id != entity.id
    decreases |items| - i
  {
    if items[i].id == entity.id {
      found := true;
    } else {
      i := i + 1;
    }
  }
  if found {
    result := Failure("duplicate id");
  } else {
    result := Success(items + [entity]);
  }
}