datatype Result<T> = Success(value: T) | Failure(error: string)
datatype Product = Product(id: int, name: string, price: int)

predicate NoDuplicates(items: seq<Product>)
{
  forall i, j :: 0 <= i < j < |items| ==> items[i].id != items[j].id
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
    result := Failure("duplicate id");
  } else {
    result := Success(items + [entity]);
  }
}