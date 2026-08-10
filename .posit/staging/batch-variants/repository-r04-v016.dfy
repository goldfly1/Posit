datatype Result<T> = Success(value: T) | Failure(error: string)

datatype Product = Product(id: int, name: string, price: int)

predicate NoDuplicates(items: seq<Product>)
{
  forall i, j :: 0 <= i < j < |items| ==> items[i].id != items[j].id
}

predicate SortedByPrice(items: seq<Product>)
{
  forall i, j :: 0 <= i < j < |items| ==> items[i].price <= items[j].price
}

function InsertSorted(items: seq<Product>, e: Product): seq<Product>
  requires SortedByPrice(items)
  ensures |InsertSorted(items, e)| == |items| + 1
  ensures SortedByPrice(InsertSorted(items, e))
  ensures multiset(InsertSorted(items, e)) == multiset(items) + multiset([e])
  decreases |items|
{
  if |items| == 0 then
    [e]
  else if e.price <= items[0].price then
    [e] + items
  else
    [items[0]] + InsertSorted(items[1..], e)
}

method Add(items: seq<Product>, entity: Product) returns (result: Result<seq<Product>>)
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

method Sort(items: seq<Product>) returns (sorted: seq<Product>)
  ensures |sorted| == |items|
  ensures SortedByPrice(sorted)
  ensures multiset(sorted) == multiset(items)
  decreases |items|
{
  sorted := [];
  var i := 0;
  while i < |items|
    invariant 0 <= i <= |items|
    invariant SortedByPrice(sorted)
    invariant multiset(sorted) == multiset(items[..i])
    decreases |items| - i
  {
    sorted := InsertSorted(sorted, items[i]);
    i := i + 1;
  }
}