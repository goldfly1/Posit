datatype Result<T> = Success(value: T) | Failure(error: string)
datatype User = User(id: int, name: string, email: string)

predicate NoDuplicates(items: seq<User>)
{
  forall i, j :: 0 <= i < j < |items| ==> items[i].id != items[j].id
}

predicate IsSortedLex(items: seq<User>)
{
  forall i :: 0 <= i < |items| - 1 ==> items[i].id <= items[i+1].id
}

lemma NoDuplicatesAppendLemma(items: seq<User>, u: User)
  requires NoDuplicates(items)
  requires forall k :: 0 <= k < |items| ==> items[k].id != u.id
  ensures NoDuplicates(items + [u])
{
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
  if found {
    result := Failure("duplicate id");
  } else {
    NoDuplicatesAppendLemma(items, u);
    result := Success(items + [u]);
  }
}

lemma SortedConcatLemma(a: User, ys: seq<User>)
  requires IsSortedLex(ys)
  requires |ys| > 0 ==> a.id <= ys[0].id
  ensures IsSortedLex([a] + ys)
{
}

lemma InsertSortedLexFirstLemma(xs: seq<User>, u: User)
  requires IsSortedLex(xs) && |xs| > 0 && u.id > xs[0].id
  ensures InsertSortedLex(xs[1..], u)[0].id >= xs[0].id
  decreases |xs|
{
  if |xs| == 1 {
    assert InsertSortedLex(xs[1..], u)[0].id == u.id;
  } else if u.id <= xs[1].id {
    assert InsertSortedLex(xs[1..], u)[0].id == u.id;
  } else {
    assert InsertSortedLex(xs[1..], u)[0].id == xs[1].id;
  }
}

function InsertSortedLex(xs: seq<User>, u: User): seq<User>
  requires IsSortedLex(xs)
  ensures IsSortedLex(InsertSortedLex(xs, u))
  ensures |InsertSortedLex(xs, u)| == |xs| + 1
  decreases |xs|
{
  if |xs| == 0 then [u]
  else if u.id <= xs[0].id then 
    SortedConcatLemma(u, xs);
    [u] + xs
  else 
    InsertSortedLexFirstLemma(xs, u);
    SortedConcatLemma(xs[0], InsertSortedLex(xs[1..], u));
    [xs[0]] + InsertSortedLex(xs[1..], u)
}
```