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
      var fifoKey := order[0];
      newOrder := order[1..] + [key];
      newCache := (map k | k in cache && k != fifoKey :: cache[k])[key := value];
    } else {
      newOrder := order + [key];
      newCache := cache[key := value];
    }
  }
}

method Lookup(cache: map<int, string>, key: int)
  returns (result: Result<string>)
  ensures result.Success? ==> key in cache && result.value == cache[key]
  ensures result.Failure? ==> key !in cache
{
  if key in cache {
    result := Success(cache[key]);
  } else {
    result := Failure("not found");
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