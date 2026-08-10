datatype Result<T> = Success(value: T) | Failure(error: string)

method Store(cache: map<int, string>, ttl: map<int, int>, key: int, value: string, time: int) returns (newCache: map<int, string>, newTtl: map<int, int>)
  ensures key in newCache && newCache[key] == value
  ensures key in newTtl && newTtl[key] == time
  ensures forall k :: k in cache ==> k in newCache
  ensures forall k :: k != key ==> (k in newCache <==> k in cache)
  ensures forall k :: k != key ==> (k in newTtl <==> k in ttl)
  ensures forall k :: k in ttl && k != key ==> newTtl[k] == ttl[k]
{
  newCache := cache[key := value];
  newTtl := ttl[key := time];
}

method Lookup(cache: map<int, string>, key: int) returns (result: Result<string>)
  ensures result.Success? ==> key in cache && result.value == cache[key]
  ensures result.Failure? ==> key !in cache
{
  if key in cache { result := Success(cache[key]); } else { result := Failure("not found"); }
}

method Invalidate(cache: map<int, string>, ttl: map<int, int>, key: int) returns (newCache: map<int, string>, newTtl: map<int, int>)
  ensures key !in newCache
  ensures key !in newTtl
  ensures forall k :: k in cache && k != key ==> k in newCache
  ensures forall k :: k in ttl && k != key ==> k in newTtl
  ensures forall k :: k in newCache ==> k in cache && k != key && newCache[k] == cache[k]
  ensures forall k :: k in newTtl ==> k in ttl && k != key && newTtl[k] == ttl[k]
{
  newCache := map k | k in cache && k != key :: cache[k];
  newTtl := map k | k in ttl && k != key :: ttl[k];
}

function Contains(cache: map<int, string>, key: int): bool
{
  key in cache
}