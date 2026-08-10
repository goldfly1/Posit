datatype Result<T> = Success(value: T) | Failure(error: string)

datatype Product = Product(id: string, name: string)

predicate NoDuplicates(items: seq<Product>)
{
  forall i, j :: 0 <= i < j < |items| ==> items[i].id != items[j].id
}

predicate SortedByName(items: seq<Product>)
{
  forall i :: 0 <= i < |items| - 1 ==> |items[i].name| <= |items[i + 1].name|
}

lemma SortedByNameTailLemma(items: seq<Product>)
  requires SortedByName(items)
  requires |items| > 0
  ensures SortedByName(items[1..])
  decreases |items|
{
  if |items| > 1 {
    SortedByNameTailLemma(items[1..]);
  }
}

lemma SortedByNameConsLemma(e: Product, rest: seq<Product>)
  requires SortedByName(rest)
  requires |rest| == 0 || |e.name| <= |rest[0].name|
  ensures SortedByName([e] + rest)
{
}

lemma InsertSortedHeadGE(items: seq<Product>, e: Product, bound: int)
  requires SortedByName(items)
  requires |items| > 0 ==> |items[0].name| >= bound
  requires |e.name| >= bound
  ensures |InsertSorted(items, e)[0].name| >= bound
  decreases |items|
{
  if |items| > 0 && |items[0].name| < |e.name| {
    SortedByNameTailLemma(items);
    InsertSortedHeadGE(items[1..], e, |items[0].name|);
  }
}

function InsertSorted(items: seq<Product>, e: Product): seq<Product>
  requires SortedByName(items)
  ensures |InsertSorted(items, e)| == |items| + 1
  ensures SortedByName(InsertSorted(items, e))
  ensures multiset(InsertSorted(items, e)) == multiset(items) + multiset([e])
  decreases |items|
{
  if |items| == 0 then
    [e]
  else if |e.name| <= |items[0].name| then
    SortedByNameConsLemma(e, items);
    [e] + items
  else
    SortedByNameTailLemma(items);
    var rest := InsertSorted(items[1..], e);
    InsertSortedHeadGE(items[1..], e, |items[0].name|);
    SortedByNameConsLemma(items[0], rest);
    [items[0]] + rest
}

method Add(items: seq<Product>, entity: Product) returns (result: Result<seq<Product>>)
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