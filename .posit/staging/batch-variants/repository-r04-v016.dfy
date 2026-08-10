datatype Result<T> = Success(value: T) | Failure(error: string)

datatype Product = Product(id: int, name: string, price: int)

predicate NoDuplicates(items: seq<Product>)
{
  forall i, j :: 0 <= i < j < |items| ==> items[i].id != items[j].id
}

predicate SortedByPrice(items: seq<Product>)
{
  forall i :: 0 <= i < |items| - 1 ==> items[i].price <= items[i + 1].price
}

lemma SortedByPriceTailLemma(items: seq<Product>)
  requires SortedByPrice(items)
  requires |items| > 0
  ensures SortedByPrice(items[1..])
  decreases |items|
{
  if |items| > 1 {
    SortedByPriceTailLemma(items[1..]);
  }
}

lemma SortedByPriceConsLemma(e: Product, rest: seq<Product>)
  requires SortedByPrice(rest)
  requires |rest| == 0 || e.price <= rest[0].price
  ensures SortedByPrice([e] + rest)
{
}

lemma InsertSortedHeadGE(items: seq<Product>, e: Product, bound: int)
  requires SortedByPrice(items)
  requires |items| > 0 ==> items[0].price >= bound
  requires e.price >= bound
  ensures InsertSorted(items, e)[0].price >= bound
  decreases |items|
{
  if |items| > 0 && items[0].price < e.price {
    SortedByPriceTailLemma(items);
    InsertSortedHeadGE(items[1..], e, items[0].price);
  }
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
    SortedByPriceConsLemma(e, items);
    [e] + items
  else
    SortedByPriceTailLemma(items);
    var rest := InsertSorted(items[1..], e);
    InsertSortedHeadGE(items[1..], e, items[0].price);
    SortedByPriceConsLemma(items[0], rest);
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