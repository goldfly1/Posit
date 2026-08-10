datatype Result<T> = Success(value: T) | Failure(error: string)

datatype Product = Product(id: int, name: string, price: int)

predicate NoDuplicates(items: seq<Product>)
{
  forall i, j :: 0 <= i < j < |items| ==> items[i].id != items[j].id
}

function Count(items: seq<Product>): int
  ensures Count(items) == |items|
  decreases |items|
{
  if |items| == 0 then 0 else 1 + Count(items[1..])
}

function CountByPriceRange(items: seq<Product>, lo: int, hi: int): int
  requires lo <= hi
  ensures 0 <= CountByPriceRange(items, lo, hi) <= |items|
  decreases |items|
{
  if |items| == 0 then 0
  else if lo <= items[0].price <= hi then 1 + CountByPriceRange(items[1..], lo, hi)
  else CountByPriceRange(items[1..], lo, hi)
}

method Add(items: seq<Product>, entity: Product) returns (result: Result<seq<Product>>)
  requires NoDuplicates(items)
  ensures result.Success? ==> |result.value| == |items| + 1
  ensures result.Failure? ==> result.error == "duplicate id"
  ensures result.Success? ==> Count(result.value) == |items| + 1
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

method Size(items: seq<Product>) returns (n: int)
  ensures n == |items|
  ensures n == Count(items)
  decreases |items|
{
  n := Count(items);
}