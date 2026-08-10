datatype Result<T> = Success(value: T) | Failure(error: string)

function FilterKey(s: seq<int>, k: int): seq<int>
  decreases |s|
{
  if |s| == 0 then []
  else if s[0] == k then FilterKey(s[1..], k)
  else [s[0]] + FilterKey(s[1..], k)
}

method Store(cache: map<int, string>, sizes: map<int, int>, order: seq<int>,
             key: int, value: string)
  returns (newCache: map<int, string>, newSizes: map<int, int>, newOrder: seq<int>)
  ensures key in newCache
  ensures newCache[key] == value
  ensures key in newSizes
  ensures newSizes[key] == |value|
  ensures forall k :: k in cache ==> k in newCache
  ensures key in newOrder
{
  newOrder := [key] + FilterKey(order, key);
  newCache := cache[key := value];
  newSizes := sizes[key := |value|];
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

method Invalidate(cache: map<int, string>, sizes: map<int, int>, order: seq<int>, key: int)
  returns (newCache: map<int, string>, newSizes: map<int, int>, newOrder: seq<int>)
  ensures key !in newCache
  ensures key !in newSizes
  ensures newOrder == FilterKey(order, key)
{
  newCache := map k | k in cache && k != key :: cache[k];
  newSizes := map k | k in sizes && k != key :: sizes[k];
  newOrder := FilterKey(order, key);
}

method EvictLRU(cache: map<int, string>, sizes: map<int, int>, order: seq<int>)
  returns (newCache: map<int, string>, newSizes: map<int, int>, newOrder: seq<int>)
  requires |order| > 0
  ensures newOrder == FilterKey(order, order[|order| - 1])
  ensures order[|order| - 1] !in newOrder
{
  var lruKey := order[|order| - 1];
  newCache := map k | k in cache && k != lruKey :: cache[k];
  newSizes := map k | k in sizes && k != lruKey :: sizes[k];
  newOrder := FilterKey(order, lruKey);
}