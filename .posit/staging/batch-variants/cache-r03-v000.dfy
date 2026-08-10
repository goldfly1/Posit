datatype Result<T> = Success(value: T) | Failure(error: string)

method Store(cache: map<int, string>, key: int, value: string) returns (newCache: map<int, string>)
  ensures key in newCache
  ensures newCache[key] == value
  ensures forall k :: k in cache ==> k in newCache
{
  newCache := cache[key := value];
}

method Lookup(cache: map<int, string>, key: int) returns (result: Result<string>)
  ensures result.Success? ==> key in cache && result.value == cache[key]
  ensures result.Failure? ==> key !in cache
{
  if key in cache { result := Success(cache[key]); } else { result := Failure("not found"); }
}

method Invalidate(cache: map<int, string>, key: int) returns (newCache: map<int, string>)
  ensures key !in newCache
  ensures forall k :: k in cache && k != key ==> k in newCache
{
  newCache := map k | k in cache && k != key :: cache[k];
}

method InvalidateAll(cache: map<int, string>) returns (newCache: map<int, string>)
  ensures |newCache| == 0
  ensures forall k :: k !in newCache
{
  newCache := map[];
}

function Contains(cache: map<int, string>, key: int): bool
{
  key in cache
}

function Size(cache: map<int, string>): int
{
  |cache|
}