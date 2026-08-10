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

method EvictFIFO(cache: map<int, string>, sizes: map<int, int>, order: seq<int>)
  returns (newCache: map<int, string>, newSizes: map<int, int>, newOrder: seq<int>)
  requires |order| > 0
  ensures |newOrder| == |order| - 1
{
  var fifoKey := order[0];
  newCache := map k | k in cache && k != fifoKey :: cache[k];
  newSizes := map k | k in sizes && k != fifoKey :: sizes[k];
  newOrder := order[1..];
}

function TotalSize(keys: seq<int>, sizes: map<int, int>): int
  requires forall k :: k in keys ==> k in sizes
  decreases |keys|
{
  if |keys| == 0 then 0
  else sizes[keys[0]] + TotalSize(keys[1..], sizes)
}