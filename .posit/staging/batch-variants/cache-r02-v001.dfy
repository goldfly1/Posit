datatype Result<T> = Success(value: T) | Failure(error: string)

class LRUCache {
  var data: map<int, string>
  var order: seq<int>
  var ttl: map<int, int>
  var time: int
  predicate Valid() reads this { |data| == |order| && (forall k :: k in data ==> k in ttl) }
  constructor() ensures Valid() { data := map[]; order := []; ttl := map[]; time := 0; }
  method Store(key: int, value: string, expire: int) requires Valid() modifies this ensures key in data ensures data[key] == value ensures Valid() {
    if key in data { order := RemoveKey(order, key); }
    order := order + [key];
    data := data[key := value];
    ttl := ttl[key := time + expire];
  }
  method Lookup(key: int) returns (r: Result<string>) requires Valid() modifies this ensures r.Success? ==> key in data && r.value == data[key] ensures r.Failure? ==> key !in data {
    if key in data && key in ttl && ttl[key] > time {
      r := Success(data[key]);
    } else {
      if key in data { Invalidate(key); }
      r := Failure("not found");
    }
  }
  method Invalidate(key: int) requires Valid() modifies this ensures key !in data ensures Valid() {
    data := map k | k in data && k != key :: data[k];
    ttl := map k | k in ttl && k != key :: ttl[k];
    order := RemoveKey(order, key);
  }
  method Tick() requires Valid() modifies this ensures Valid() { time := time + 1; }
  function Contains(key: int): bool requires Valid() reads this { key in data && key in ttl && ttl[key] > time }
  function RemoveKey(s: seq<int>, key: int): seq<int> decreases s {
    if |s| == 0 then [] else if s[0] == key then RemoveKey(s[1..], key) else [s[0]] + RemoveKey(s[1..], key)
  }
}