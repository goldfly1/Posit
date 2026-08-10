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

method Find(items: seq<User>, id: int) returns (result: Result<User>)
  ensures result.Success? ==> result.value.id == id
  ensures result.Failure? ==> result.error == "not found"
  decreases |items|
{
  var i := 0;
  while i < |items|
    invariant 0 <= i <= |items|
    decreases |items| - i
  {
    if items[i].id == id { result := Success(items[i]); return; }
    i := i + 1;
  }
  result := Failure("not found");
}