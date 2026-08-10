datatype Result<T> = Success(value: T) | Failure(error: string)

class LRUCache {
  var data: map<int, string>
  var order: seq<int>
  var maxSize: int
  predicate Valid() reads this {
    |data| == |order| && |data| <= maxSize &&
    (forall i :: 0 <= i < |order| ==> order[i] in data) &&
    (forall k :: k in data ==> k in order)
  }
  constructor(maxSize: int) requires maxSize > 0 ensures Valid() { data := map[]; order := []; this.maxSize := maxSize; }
  method Store(key: int, value: string) requires Valid() modifies this ensures key in data ensures data[key] == value ensures Valid() {
    if key !in data {
      while |data| >= maxSize
        invariant Valid()
        modifies this
        decreases |data|
      {
        var evictKey := order[0];
        order := order[1..];
        data := map k | k in data && k != evictKey :: data[k];
      }
    } else {
      order := RemoveKey(order, key);
    }
    order := order + [key];
    data := data[key := value];
  }
  method Lookup(key: int) returns (r: Result<string>) requires Valid() ensures r.Success? ==> key in data && r.value == data[key] ensures r.Failure? ==> key !in data {
    if key in data { r := Success(data[key]); } else { r := Failure("not found"); }
  }
  method Invalidate(key: int) requires Valid() modifies this ensures key !in data ensures Valid() {
    data := map k | k in data && k != key :: data[k];
    order := RemoveKey(order, key);
  }
  function Contains(key: int): bool requires Valid() reads this { key in data }
  function RemoveKey(s: seq<int>, key: int): seq<int> decreases s {
    if |s| == 0 then [] else if s[0] == key then RemoveKey(s[1..], key) else [s[0]] + RemoveKey(s[1..], key)
  }
}