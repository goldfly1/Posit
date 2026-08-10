datatype Result<T> = Success(value: T) | Failure(error: string)

function FilterKey(s: seq<int>, k: int): seq<int>
  decreases |s|
{
  if |s| == 0 then []
  else if s[0] == k then FilterKey(s[1..], k)
  else [s[0]] + FilterKey(s[1..], k)
}

method Store(cache: map<int, string>, order: seq<int>, key: int, value: string)
  returns (newCache: map<int, string>, newOrder: seq<int>)
  ensures key in newCache
  ensures newCache[key] == value
  ensures forall k :: k in cache ==> k in newCache
  ensures key in newOrder
{
  newOrder := [key] + FilterKey(order, key);
  newCache := cache[key := value];
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
  ensures forall k :: k in cache && k != key ==> k in newCache
{
  newCache := map k | k in cache && k != key :: cache[k];
  newOrder := FilterKey(order, key);
}

method EvictFIFO(cache: map<int, string>, order: seq<int>)
  returns (newCache: map<int, string>, newOrder: seq<int>)
  requires |order| > 0
  ensures |newOrder| == |order| - 1
{
  var fifoKey := order[0];
  newCache := map k | k in cache && k != fifoKey :: cache[k];
  newOrder := order[1..];
}

function Contains(cache: map<int, string>, key: int): bool
{
  key in cache
}

function Size(cache: map<int, string>): int
{
  |cache|
}