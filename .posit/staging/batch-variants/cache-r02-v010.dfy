datatype Result<T> = Success(value: T) | Failure(error: string)

class LFUCache {
  var data: map<int, string>
  var freq: map<int, int>
  var keys: seq<int>
  predicate Valid() reads this {
    |data| == |keys| &&
    (forall i :: 0 <= i < |keys| ==> keys[i] in data) &&
    (forall k :: k in data ==> k in keys) &&
    (forall k :: k in data ==> k in freq)
  }
  constructor() ensures Valid() { data := map[]; freq := map[]; keys := []; }
  method Store(key: int, value: string) requires Valid() modifies this ensures key in data ensures data[key] == value ensures Valid() {
    if key !in data { keys := keys + [key]; freq := freq[key := 1]; }
    data := data[key := value];
  }
  method Lookup(key: int) returns (r: Result<string>) requires Valid() modifies this ensures r.Success? ==> key in data && r.value == data[key] ensures r.Failure? ==> key !in data {
    if key in data {
      freq := freq[key := freq[key] + 1];
      r := Success(data[key]);
    } else { r := Failure("not found"); }
  }
  method Invalidate(key: int) requires Valid() modifies this ensures key !in data ensures Valid() {
    data := map k | k in data && k != key :: data[k];
    freq := map k | k in freq && k != key :: freq[k];
    keys := RemoveKey(keys, key);
  }
  method Size() returns (sz: int) requires Valid() ensures sz == |data| { sz := |data|; }
  function Contains(key: int): bool requires Valid() reads this { key in data }
  function RemoveKey(s: seq<int>, key: int): seq<int> decreases s {
    if |s| == 0 then [] else if s[0] == key then RemoveKey(s[1..], key) else [s[0]] + RemoveKey(s[1..], key)
  }
}