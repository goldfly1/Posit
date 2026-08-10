datatype Result<T> = Success(value: T) | Failure(error: string)

method Store(cache: map<int, string>, ttl: map<int, int>, size: map<int, int>, key: int, value: string, time: int, sz: int, maxEntries: int) returns (newCache: map<int, string>, newTtl: map<int, int>, newSize: map<int, int>)
  requires |cache| <= maxEntries
  requires |cache| < maxEntries || key in cache
  ensures key in newCache && newCache[key] == value
  ensures key in newTtl && newTtl[key] == time
  ensures key in newSize && newSize[key] == sz
  ensures |newCache| <= maxEntries
  ensures forall k :: k in cache ==> k in newCache
  ensures forall k :: k != key ==> (k in newCache <==> k in cache)
  ensures forall k :: k != key ==> (k in newTtl <==> k in ttl)
  ensures forall k :: k != key ==> (k in newSize <==> k in size)
  ensures forall k :: k in ttl && k != key ==> newTtl[k] == ttl[k]
  ensures forall k :: k in size && k != key ==> newSize[k] == size[k]
{
  newCache := cache[key := value];
  newTtl := ttl[key := time];
  newSize := size[key := sz];
}

method Lookup(cache: map<int, string>, key: int) returns (result: Result<string>)
  ensures result.Success? ==> key in cache && result.value == cache[key]
  ensures result.Failure? ==> key !in cache
{
  if key in cache { result := Success(cache[key]); } else { result := Failure("not found"); }
}

method Invalidate(cache: map<int, string>, ttl: map<int, int>, size: map<int, int>, key: int) returns (newCache: map<int, string>, newTtl: map<int, int>, newSize: map<int, int>)
  ensures key !in newCache
  ensures key !in newTtl
  ensures key !in newSize
  ensures forall k :: k in cache && k != key ==> k in newCache
  ensures forall k :: k in ttl && k != key ==> k in newTtl
  ensures forall k :: k in size && k != key ==> k in newSize
  ensures forall k :: k in newCache ==> k in cache && k != key && newCache[k] == cache[k]
  ensures forall k :: k in newTtl ==> k in ttl && k != key && newTtl[k] == ttl[k]
  ensures forall k :: k in newSize ==> k in size && k != key && newSize[k] == size[k]
{
  newCache := map k | k in cache && k != key :: cache[k];
  newTtl := map k | k in ttl && k != key :: ttl[k];
  newSize := map k | k in size && k != key :: size[k];
}