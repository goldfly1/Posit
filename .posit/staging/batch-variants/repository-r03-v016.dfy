datatype Result<T> = Success(value: T) | Failure(error: string)
datatype Product = Product(id: int, name: string, price: int)

predicate NoDuplicates(items: seq<Product>)
{
  forall i, j :: 0 <= i < j < |items| ==> items[i].id != items[j].id
}

predicate IsSortedById(items: seq<Product>)
{
  forall i :: 0 <= i + 1 < |items| ==> items[i].id <= items[i+1].id
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

function InsertSortedById(xs: seq<Product>, p: Product): seq<Product>
  requires IsSortedById(xs)
  ensures IsSortedById(InsertSortedById(xs, p))
  ensures |InsertSortedById(xs, p)| == |xs| + 1
  decreases |xs|
{
  if |xs| == 0 then [p]
  else if p.id <= xs[0].id then [p] + xs
  else [xs[0]] + InsertSortedById(xs[1..], p)
}

function SortById(items: seq<Product>): seq<Product>
  ensures IsSortedById(SortById(items))
  ensures |SortById(items)| == |items|
  decreases |items|
{
  if |items| <= 1 then items
  else InsertSortedById(SortById(items[..|items|-1]), items[|items|-1])
}