datatype Result<T> = Success(value: T) | Failure(error: string)
datatype User = User(id: int, name: string, email: string)

predicate NoDuplicates(items: seq<User>)
{
  forall i, j :: 0 <= i < j < |items| ==> items[i].id != items[j].id
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

method Update(items: seq<User>, u: User) returns (result: Result<seq<User>>)
  requires NoDuplicates(items)
  ensures result.Success? ==> |result.value| == |items| && NoDuplicates(result.value)
  ensures result.Failure? ==> result.error == "not found"
  decreases |items|
{
  var i := 0; var idx := -1;
  while i < |items|
    invariant 0 <= i <= |items| && NoDuplicates(items)
    invariant idx < 0 || (0 <= idx < |items| && items[idx].id == u.id)
    decreases |items| - i
  {
    if items[i].id == u.id { idx := i; }
    i := i + 1;
  }
  if idx < 0 { result := Failure("not found"); }
  else { result := Success(items[..idx] + [u] + items[idx+1..]); }
}