datatype Result<T> = Success(value: T) | Failure(error: string)

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

method Store(cache: map<int, string>, freq: map<int, int>, order: seq<int>,
             key: int, value: string, maxEntries: int)
  returns (newCache: map<int, string>, newFreq: map<int, int>)
  requires maxEntries > 0
  requires |cache| <= maxEntries
  requires forall k :: k in cache <==> k in freq
  requires forall k :: k in cache <==> k in order
  ensures key in newCache
  ensures newCache[key] == value
  ensures |newCache| <= maxEntries
  ensures forall k :: k in newCache <==> k in newFreq
  ensures key in newFreq
  ensures newFreq[key] == 1 || (key in cache && newFreq[key] == freq[key] + 1)
{
  if key in cache {
    newCache := cache[key := value];
    newFreq := freq[key := freq[key] + 1];
  } else {
    if |cache| >= maxEntries {
      var evictKey := MinFreqKey(order, freq);
      newCache := (map k | k in cache && k != evictKey :: cache[k])[key := value];
      newFreq := (map k | k in freq && k != evictKey :: freq[k])[key := 1];
    } else {
      newCache := cache[key := value];
      newFreq := freq[key := 1];
    }
  }
}

method Lookup(cache: map<int, string>, freq: map<int, int>, key: int)
  returns (result: Result<string>, newFreq: map<int, int>)
  requires forall k :: k in cache <==> k in freq
  ensures result.Success? ==> key in cache && result.value == cache[key]
  ensures result.Failure? ==> key !in cache && newFreq == freq
{
  if key in cache {
    result := Success(cache[key]);
    newFreq := freq[key := freq[key] + 1];
  } else {
    result := Failure("not found");
    newFreq := freq;
  }
}

method Invalidate(cache: map<int, string>, freq: map<int, int>, key: int)
  returns (newCache: map<int, string>, newFreq: map<int, int>)
  ensures key !in newCache
  ensures key !in newFreq
  ensures forall k :: k in cache && k != key ==> k in newCache
{
  newCache := map k | k in cache && k != key :: cache[k];
  newFreq := map k | k in freq && k != key :: freq[k];
}