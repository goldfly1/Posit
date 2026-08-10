datatype Result<T> = Success(value: T) | Failure(error: string)

method Store(cache: map<int, string>, freq: map<int, int>, key: int, value: string) returns (newCache: map<int, string>, newFreq: map<int, int>)
  ensures key in newCache && newCache[key] == value
  ensures key in newFreq
  ensures forall k :: k in cache ==> k in newCache
  ensures forall k :: k != key ==> (k in newCache <==> k in cache)
  ensures forall k :: k != key ==> (k in newFreq <==> k in freq)
  ensures forall k :: k in freq && k != key ==> newFreq[k] == freq[k]
{
  newCache := cache[key := value];
  if key in freq {
    newFreq := freq[key := freq[key] + 1];
  } else {
    newFreq := freq[key := 1];
  }
}

method Lookup(cache: map<int, string>, key: int) returns (result: Result<string>)
  ensures result.Success? ==> key in cache && result.value == cache[key]
  ensures result.Failure? ==> key !in cache
{
  if key in cache { result := Success(cache[key]); } else { result := Failure("not found"); }
}

method Invalidate(cache: map<int, string>, freq: map<int, int>, key: int) returns (newCache: map<int, string>, newFreq: map<int, int>)
  ensures key !in newCache
  ensures key !in newFreq
  ensures forall k :: k in cache && k != key ==> k in newCache
  ensures forall k :: k in freq && k != key ==> k in newFreq
  ensures forall k :: k in newCache ==> k in cache && k != key && newCache[k] == cache[k]
  ensures forall k :: k in newFreq ==> k in freq && k != key && newFreq[k] == freq[k]
{
  newCache := map k | k in cache && k != key :: cache[k];
  newFreq := map k | k in freq && k != key :: freq[k];
}

function Contains(cache: map<int, string>, key: int): bool
{
  key in cache
}