datatype Result<T> = Success(value: T) | Failure(error: string)

class LFUCache {
  var data: map<int, string>
  var freq: map<int, int>
  var keys: seq<int>
  var maxSize: int
  predicate Valid() reads this {
    |data| == |keys| && |data| <= maxSize &&
    (forall i :: 0 <= i < |keys| ==> keys[i] in data) &&
    (forall k :: k in data ==> k in keys) &&
    (forall k :: k in data ==> k in freq)
  }
  constructor(maxSize: int) requires maxSize > 0 ensures Valid() { data := map[]; freq := map[]; keys := []; this.maxSize := maxSize; }
  method Store(key: int, value: string) requires Valid() modifies this ensures key in data ensures data[key] == value ensures Valid() {
    if key !in data {
      while |data| >= maxSize
        invariant Valid()
        modifies this
        decreases |data|
      {
        var evictKey := MinFreqKey(keys, freq);
        keys := RemoveKey(keys, evictKey);
        data := map k | k in data && k != evictKey :: data[k];
        freq := map k | k in freq && k != evictKey :: freq[k];
      }
      keys := keys + [key];
      freq := freq[key := 1];
    }
    data := data[key := value];
  }
  method Lookup(key: int) returns (r: Result<string>) requires Valid() modifies this ensures r.Success? ==> key in data && r.value == data[key] ensures r.Failure? ==> key !in data {
    if key in data { freq := freq[key := freq[key] + 1]; r := Success(data[key]); } else { r := Failure("not found"); }
  }
  method Invalidate(key: int) requires Valid() modifies this ensures key !in data ensures Valid() {
    data := map k | k in data && k != key :: data[k];
    freq := map k | k in freq && k != key :: freq[k];
    keys := RemoveKey(keys, key);
  }
  function Contains(key: int): bool requires Valid() reads this { key in data }
  function MinFreqKey(s: seq<int>, f: map<int, int>): int
    requires |s| > 0
    requires forall i :: 0 <= i < |s| ==> s[i] in f
    decreases s
  {
    if |s| == 1 then s[0]
    else
      var restMin := MinFreqKey(s[1..], f);
      if f[s[0]] <= f[restMin] then s[0] else restMin
  }
  function RemoveKey(s: seq<int>, key: int): seq<int> decreases s {
    if |s| == 0 then [] else if s[0] == key then RemoveKey(s[1..], key) else [s[0]] + RemoveKey(s[1..], key)
  }
}