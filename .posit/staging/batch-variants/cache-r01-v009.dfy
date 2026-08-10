datatype Result<T> = Success(value: T) | Failure(error: string)

method Store(cache: map<int, string>, freq: map<int, int>, expiry: map<int, int>,
             key: int, value: string, ttl: int, now: int)
  returns (newCache: map<int, string>, newFreq: map<int, int>, newExpiry: map<int, int>)
  ensures key in newCache
  ensures newCache[key] == value
  ensures key in newExpiry
  ensures newExpiry[key] == now + ttl
  ensures forall k :: k in cache ==> k in newCache
  ensures key in newFreq
  ensures newFreq[key] == if key in freq then freq[key] + 1 else 1
{
  newCache := cache[key := value];
  newExpiry := expiry[key := now + ttl];
  if key in freq {
    newFreq := freq[key := freq[key] + 1];
  } else {
    newFreq := freq[key := 1];
  }
}

method Lookup(cache: map<int, string>, freq: map<int, int>, expiry: map<int, int>,
              key: int, now: int)
  returns (result: Result<string>, newFreq: map<int, int>)
  requires forall k :: k in cache <==> k in expiry
  ensures result.Success? ==> key in cache && key in expiry && now < expiry[key]
  ensures result.Failure? ==> newFreq == freq
{
  if key in cache && key in freq && key in expiry && now < expiry[key] {
    result := Success(cache[key]);
    newFreq := freq[key := freq[key] + 1];
  } else {
    result := Failure("not found");
    newFreq := freq;
  }
}

method Invalidate(cache: map<int, string>, freq: map<int, int>, expiry: map<int, int>, key: int)
  returns (newCache: map<int, string>, newFreq: map<int, int>, newExpiry: map<int, int>)
  ensures key !in newCache
  ensures key !in newFreq
  ensures key !in newExpiry
{
  newCache := map k | k in cache && k != key :: cache[k];
  newFreq := map k | k in freq && k != key :: freq[k];
  newExpiry := map k | k in expiry && k != key :: expiry[k];
}

function MinFreqKey(keys: seq<int>, freq: map<int, int>): int
  requires |keys| > 0
  requires forall k :: k in keys ==> k in freq
  decreases |keys|
{
  if |keys| == 1 then keys[0]
  else
    var restMin := MinFreqKey(keys[1..], freq);
    if freq[keys[0]] <= freq[restMin] then keys[0] else restMin
}

method EvictLFU(cache: map<int, string>, freq: map<int, int>, expiry: map<int, int>,
                keys: seq<int>)
  returns (newCache: map<int, string>, newFreq: map<int, int>, newExpiry: map<int, int>)
  requires |keys| > 0
  requires forall k :: k in keys <==> k in freq
  requires forall k :: k in keys <==> k in cache
  requires forall k :: k in keys <==> k in expiry
  ensures |newFreq| == |freq| - 1
{
  var evictKey := MinFreqKey(keys, freq);
  newCache := map k | k in cache && k != evictKey :: cache[k];
  newFreq := map k | k in freq && k != evictKey :: freq[k];
  newExpiry := map k | k in expiry && k != evictKey :: expiry[k];
}