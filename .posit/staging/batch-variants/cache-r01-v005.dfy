datatype Result<T> = Success(value: T) | Failure(error: string)

function FilterKey(s: seq<int>, k: int): seq<int>
  decreases |s|
{
  if |s| == 0 then []
  else if s[0] == k then FilterKey(s[1..], k)
  else [s[0]] + FilterKey(s[1..], k)
}

method Store(cache: map<int, string>, expiry: map<int, int>, order: seq<int>,
             key: int, value: string, ttl: int, now: int)
  returns (newCache: map<int, string>, newExpiry: map<int, int>, newOrder: seq<int>)
  ensures key in newCache
  ensures newCache[key] == value
  ensures key in newExpiry
  ensures newExpiry[key] == now + ttl
  ensures forall k :: k in cache ==> k in newCache
  ensures key in newOrder
{
  newOrder := [key] + FilterKey(order, key);
  newCache := cache[key := value];
  newExpiry := expiry[key := now + ttl];
}

method Lookup(cache: map<int, string>, expiry: map<int, int>, key: int, now: int)
  returns (result: Result<string>)
  ensures result.Success? ==> key in cache && key in expiry && now < expiry[key]
  ensures result.Failure? ==> !(key in cache && key in expiry && now < expiry[key])
{
  if key in cache && key in expiry && now < expiry[key] {
    result := Success(cache[key]);
  } else {
    result := Failure("not found");
  }
}

method Invalidate(cache: map<int, string>, expiry: map<int, int>, order: seq<int>, key: int)
  returns (newCache: map<int, string>, newExpiry: map<int, int>, newOrder: seq<int>)
  ensures key !in newCache
  ensures key !in newExpiry
  ensures newOrder == FilterKey(order, key)
{
  newCache := map k | k in cache && k != key :: cache[k];
  newExpiry := map k | k in expiry && k != key :: expiry[k];
  newOrder := FilterKey(order, key);
}

method EvictFIFO(cache: map<int, string>, expiry: map<int, int>, order: seq<int>)
  returns (newCache: map<int, string>, newExpiry: map<int, int>, newOrder: seq<int>)
  requires |order| > 0
  ensures |newOrder| == |order| - 1
{
  var fifoKey := order[0];
  newCache := map k | k in cache && k != fifoKey :: cache[k];
  newExpiry := map k | k in expiry && k != fifoKey :: expiry[k];
  newOrder := order[1..];
}