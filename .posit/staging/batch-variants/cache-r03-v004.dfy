datatype Result<T> = Success(value: T) | Failure(error: string)

method Store(cache: map<int, string>, order: seq<int>, key: int, value: string) returns (newCache: map<int, string>, newOrder: seq<int>)
  ensures key in newCache && newCache[key] == value
  ensures forall k :: k in cache ==> k in newCache
  ensures key in newOrder[..]
  ensures |newOrder| <= |order| + 1
{
  newCache := cache[key := value];
  if key in order[..] {
    newOrder := order;
  } else {
    newOrder := order + [key];
  }
}

method Lookup(cache: map<int, string>, key: int) returns (result: Result<string>)
  ensures result.Success? ==> key in cache && result.value == cache[key]
  ensures result.Failure? ==> key !in cache
{
  if key in cache { result := Success(cache[key]); } else { result := Failure("not found"); }
}

method Invalidate(cache: map<int, string>, order: seq<int>, key: int) returns (newCache: map<int, string>, newOrder: seq<int>)
  ensures key !in newCache
  ensures key !in newOrder[..]
  ensures forall k :: k in cache && k != key ==> k in newCache
{
  newCache := map k | k in cache && k != key :: cache[k];
  newOrder := [];
  var i := 0;
  while i < |order|
    invariant 0 <= i <= |order|
    invariant key !in newOrder[..]
    invariant forall j :: 0 <= j < i && order[j] != key ==> order[j] in newOrder[..]
    decreases |order| - i
  {
    if order[i] != key {
      newOrder := newOrder + [order[i]];
    }
    i := i + 1;
  }
}

function Contains(cache: map<int, string>, key: int): bool
{
  key in cache
}