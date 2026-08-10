datatype Result<T> = Success(value: T) | Failure(error: string)

class LFUCache {
  var data: map<int, string>
  var freq: map<int, int>
  var ttl: map<int, int>
  var time: int
  var keys: seq<int>
  predicate Valid() reads this {
    |data| == |keys| &&
    (forall i :: 0 <= i < |keys| ==> keys[i] in data) &&
    (forall k :: k in data ==> k in keys) &&
    (forall k :: k in data ==> k in freq) &&
    (forall k :: k in data ==> k in ttl)
  }
  constructor() ensures Valid() { data := map[]; freq := map[]; ttl := map[]; time := 0; keys := []; }
  method Store(key: int, value: string, expire: int) requires Valid() modifies this ensures key in data ensures data[key] == value ensures Valid() {
    if key !in data { keys := keys + [key]; freq := freq[key := 1]; }
    data := data[key := value];
    ttl := ttl[key := time + expire];
  }
  method Lookup(key: int) returns (r: Result<string>) requires Valid() modifies this ensures r.Success? ==> key in data && r.value == data[key] ensures r.Failure? ==> key !in data {
    if key in data && key in ttl && ttl[key] > time {
      freq := freq[key := freq[key] + 1];
      r := Success(data[key]);
    } else {
      if key in data { Invalidate(key); }
      r := Failure("not found");
    }
  }
  method Invalidate(key: int) requires Valid() modifies this ensures key !in data ensures Valid() {
    data := map k | k in data && k != key :: data[k];
    freq := map k | k in freq && k != key :: freq[k];
    ttl := map k | k in ttl && k != key :: ttl[k];
    keys := RemoveKey(keys, key);
  }
  method Tick() requires Valid() modifies this ensures Valid() { time := time + 1; }
  function Contains(key: int): bool requires Valid() reads this { key in data && key in ttl && ttl[key] > time }
  function RemoveKey(s: seq<int>, key: int): seq<int> decreases s {
    if |s| == 0 then [] else if s[0] == key then RemoveKey(s[1..], key) else [s[0]] + RemoveKey(s[1..], key)
  }
}