datatype Result<T> = Success(value: T) | Failure(error: string)

function FilterKey(s: seq<int>, k: int): seq<int>
  decreases |s|
{
  if |s| == 0 then []
  else if s[0] == k then FilterKey(s[1..], k)
  else [s[0]] + FilterKey(s[1..], k)
}

method Store(cache: map<int, string>, order: seq<int>, key: int, value: string, maxEntries: int)
  returns (newCache: map<int, string>, newOrder: seq<int>)
  requires maxEntries > 0
  requires |cache| <= maxEntries
  requires |order| <= maxEntries
  requires forall k :: k in order ==> k in cache
  ensures key in newCache
  ensures newCache[key] == value
  ensures forall k :: k in cache ==> k in newCache
  ensures |newCache| <= maxEntries
  ensures |newOrder| <= maxEntries
  ensures key in newOrder
{
  if key in cache {
    newOrder := [key] + FilterKey(order, key);
    newCache := cache[key := value];
  } else {
    if |order| >= maxEntries {
      var lruKey := order[|order| - 1];
      newOrder := [key] + order[..|order| - 1];
      newCache := (map k | k in cache && k != lruKey :: cache[k])[key := value];
    } else {
      newOrder := [key] + order;
      newCache := cache[key := value];
    }
  }
}

method Lookup(cache: map<int, string>, order: seq<int>, key: int)
  returns (result: Result<string>, newOrder: seq<int>)
  ensures result.Success? ==> key in cache && result.value == cache[key] && key in newOrder
  ensures result.Failure? ==> key !in cache && newOrder == order
{
  if key in cache {
    result := Success(cache[key]);
    newOrder := [key] + FilterKey(order, key);
  } else {
    result := Failure("not found");
    newOrder := order;
  }
}

method Invalidate(cache: map<int, string>, order: seq<int>, key: int)
  returns (newCache: map<int, string>, newOrder: seq<int>)
  ensures key !in newCache
  ensures newOrder == FilterKey(order, key)
{
  newCache := map k | k in cache && k != key :: cache[k];
  newOrder := FilterKey(order, key);
}

function Contains(cache: map<int, string>, key: int): bool
{
  key in cache
}

function Size(cache: map<int, string>): int
{
  |cache|
}