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
    invariant !found ==> forall k :: 0 <= k < i ==> items[k].id != p.id
    invariant found ==> exists k :: 0 <= k < i && items[k].id == p.id
    decreases |items| - i
  {
    if items[i].id == p.id { found := true; }
    i := i + 1;
  }
  if found { result := Failure("duplicate id"); }
  else { result := Success(items + [p]); }
}

method Count(items: seq<Product>, id: int) returns (n: int)
  ensures 0 <= n <= |items|
  decreases |items|
{
  n := 0; var i := 0;
  while i < |items|
    invariant 0 <= i <= |items| && 0 <= n <= i
    decreases |items| - i
  {
    if items[i].id == id { n := n + 1; }
    i := i + 1;
  }
}