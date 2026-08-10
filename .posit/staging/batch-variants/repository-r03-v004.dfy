datatype Result<T> = Success(value: T) | Failure(error: string)
datatype User = User(id: int, name: string, email: string)

predicate NoDuplicates(items: seq<User>)
{
  forall i, j :: 0 <= i < j < |items| ==> items[i].id != items[j].id
}

predicate IsSorted(items: seq<User>)
{
  forall i :: 0 <= i < |items| - 1 ==> items[i].id <= items[i+1].id
}

method Add(items: seq<User>, u: User) returns (result: Result<seq<User>>)
  requires NoDuplicates(items)
  ensures result.Success? ==> |result.value| == |items| + 1 && NoDuplicates(result.value)
  ensures result.Failure? ==> result.error == "duplicate id"
  decreases |items|
{
  var i := 0; var found := false;
  while i < |items| && !found
    invariant 0 <= i <= |items| && NoDuplicates(items)
    invariant !found ==> forall k :: 0 <= k < i ==> items[k].id != u.id
    invariant found ==> exists k :: 0 <= k < i && items[k].id == u.id
    decreases |items| - i
  {
    if items[i].id == u.id { found := true; }
    i := i + 1;
  }
  if found { result := Failure("duplicate id"); }
  else { result := Success(items + [u]); }
}

function InsertSorted(xs: seq<User>, u: User): seq<User>
  requires IsSorted(xs)
  ensures IsSorted(InsertSorted(xs, u))
  ensures |InsertSorted(xs, u)| == |xs| + 1
  decreases |xs|
{
  if |xs| == 0 then [u]
  else if u.id <= xs[0].id then [u] + xs
  else [xs[0]] + InsertSorted(xs[1..], u)
}

function SortBy(items: seq<User>): seq<User>
  ensures IsSorted(SortBy(items))
  ensures |SortBy(items)| == |items|
  decreases |items|
{
  if |items| <= 1 then items
  else InsertSorted(SortBy(items[..|items|-1]), items[|items|-1])
}