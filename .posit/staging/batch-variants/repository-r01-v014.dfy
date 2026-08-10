datatype Result<T> = Success(value: T) | Failure(error: string)
datatype Product = Product(id: int, name: string, price: int)

predicate NoDuplicates(items: seq<Product>) {
  forall i, j :: 0 <= i < j < |items| ==> items[i].id != items[j].id
}

method Add(items: seq<Product>, p: Product) returns (result: Result<seq<Product>>)
  requires NoDuplicates(items)
  ensures result.Success? ==> |result.value| == |items| + 1
  ensures result.Failure? ==> result.error == "duplicate id"
  decreases |items|
{
  var i := 0; var found := false;
  while i < |items| && !found
    invariant 0 <= i <= |items| invariant NoDuplicates(items) decreases |items| - i
  { if items[i].id == p.id { found := true; } i := i + 1; }
  if found { result := Failure("duplicate id"); } else { result := Success(items + [p]); }
}

method Find(items: seq<Product>, id: int) returns (result: Result<Product>)
  ensures result.Success? ==> result.value.id == id
  ensures result.Failure? ==> result.error == "not found"
  decreases |items|
{
  var i := 0;
  while i < |items| invariant 0 <= i <= |items| decreases |items| - i {
    if items[i].id == id { result := Success(items[i]); return; }
    i := i + 1;
  }
  result := Failure("not found");
}

method Update(items: seq<Product>, p: Product) returns (result: Result<seq<Product>>)
  requires NoDuplicates(items)
  ensures result.Success? ==> |result.value| == |items|
  ensures result.Failure? ==> result.error == "not found"
  decreases |items|
{
  var i := 0; var found := false; var newItems := [];
  while i < |items|
    invariant 0 <= i <= |items| invariant |newItems| == i decreases |items| - i
  {
    if items[i].id == p.id { newItems := newItems + [p]; found := true; }
    else { newItems := newItems + [items[i]]; }
    i := i + 1;
  }
  if found { result := Success(newItems); } else { result := Failure("not found"); }
}

method Remove(items: seq<Product>, id: int) returns (result: Result<seq<Product>>)
  requires NoDuplicates(items)
  ensures result.Success? ==> |result.value| <= |items|
  ensures result.Failure? ==> result.error == "not found"
  decreases |items|
{
  var i := 0; var found := false; var newItems := [];
  while i < |items|
    invariant 0 <= i <= |items| invariant |newItems| <= i decreases |items| - i
  {
    if items[i].id == id {
      found := true;
    } else {
      newItems := newItems + [items[i]];
    }
    i := i + 1;
  }
  if found { result := Success(newItems); } else { result := Failure("not found"); }
}