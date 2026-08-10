datatype Result<T> = Success(value: T) | Failure(error: string)
datatype Product = Product(id: int, name: string, price: int)

predicate NoDuplicates(items: seq<Product>)
{
  forall i, j :: 0 <= i < j < |items| ==> items[i].id != items[j].id
}

method Add(items: seq<Product>, p: Product) returns (result: Result<seq<Product>>)
  requires NoDuplicates(items)
  ensures result.Success? ==> |result.value| == |items| + 1 && NoDuplicates(result.value)
  ensures result.Failure? ==> result.error == "duplicate id"
  decreases |items|
{
  var i := 0; var found := false;
  while i < |items| && !found
    invariant 0 <= i <= |items| && NoDuplicates(items)
    decreases |items| - i
  {
    if items[i].id == p.id { found := true; }
    i := i + 1;
  }
  if found { result := Failure("duplicate id"); }
  else { result := Success(items + [p]); }
}

method Remove(items: seq<Product>, id: int) returns (result: Result<seq<Product>>)
  requires NoDuplicates(items)
  ensures result.Success? ==> |result.value| == |items| - 1 && NoDuplicates(result.value)
  ensures result.Failure? ==> result.error == "not found"
  decreases |items|
{
  var i := 0; var idx := -1;
  while i < |items|
    invariant 0 <= i <= |items| && NoDuplicates(items)
    invariant idx < 0 || (0 <= idx < |items| && items[idx].id == id)
    decreases |items| - i
  {
    if items[i].id == id { idx := i; }
    i := i + 1;
  }
  if idx < 0 { result := Failure("not found"); }
  else { result := Success(items[..idx] + items[idx+1..]); }
}