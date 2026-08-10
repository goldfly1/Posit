datatype Result<T> = Success(value: T) | Failure(error: string)
datatype User = User(id: string, name: string, email: string)

predicate NoDuplicates(items: seq<User>)
{
  forall i, j :: 0 <= i < j < |items| ==> items[i].id != items[j].id
}

predicate IsSortedLex(items: seq<User>)
{
  forall i :: 0 <= i + 1 < |items| ==> items[i].id <= items[i+1].id
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
    decreases |items| - i
  {
    if items[i].id == u.id { found := true; }
    i := i + 1;
  }
  if found { result := Failure("duplicate id"); }
  else { result := Success(items + [u]); }
}

function InsertSortedLex(xs: seq<User>, u: User): seq<User>
  requires IsSortedLex(xs)
  ensures IsSortedLex(InsertSortedLex(xs, u))
  ensures |InsertSortedLex(xs, u)| == |xs| + 1
  decreases |xs|
{
  if |xs| == 0 then [u]
  else if u.id <= xs[0].id then [u] + xs
  else [xs[0]] + InsertSortedLex(xs[1..], u)
}

function SortLex(items: seq<User>): seq<User>
  ensures IsSortedLex(SortLex(items))
  ensures |SortLex(items)| == |items|
  decreases |items|
{
  if |items| <= 1 then items
  else InsertSortedLex(SortLex(items[..|items|-1]), items[|items|-1])
}