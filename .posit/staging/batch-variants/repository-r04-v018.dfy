datatype Result<T> = Success(value: T) | Failure(error: string)

datatype Product = Product(id: string, name: string, price: int)

predicate NoDuplicates(items: seq<Product>)
{
  forall i, j :: 0 <= i < j < |items| ==> items[i].id != items[j].id
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

method Find(items: seq<Product>, id: string) returns (result: Result<Product>)
  ensures result.Success? ==> result.value.id == id
  decreases |items|
{
  var i := 0;
  while i < |items|
    invariant 0 <= i <= |items|
    decreases |items| - i
  {
    if items[i].id == id {
      result := Success(items[i]);
      return;
    }
    i := i + 1;
  }
  result := Failure("not found");
}