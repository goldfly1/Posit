class Cache {
  var entries: map<int, string>
  var order: seq<int>
  const maxEntries: int
  const policy: string

  predicate Valid() reads this {
    maxEntries > 0 &&
    |entries| <= maxEntries &&
    |order| == |entries| &&
    (forall k :: k in entries ==> k in order) &&
    (forall k :: k in order ==> k in entries) &&
    (forall i, j :: 0 <= i < j < |order| ==> order[i] != order[j])
  }

  constructor(maxE: int, p: string)
    requires maxE > 0
    ensures Valid()
  {
    entries := map[];
    order := [];
    maxEntries := maxE;
    policy := p;
  }

  method Store(key: int, value: string)
    requires Valid()
    modifies this
    ensures Valid()
    ensures key in entries
    ensures entries[key] == value
  {
    if key !in entries {
      if |entries| == maxEntries {
        var evict := order[0];
        entries := entries - {evict};
        order := order[1..] + [key];
      } else {
        order := order + [key];
      }
    }
    entries := entries[key := value];
  }

  method Lookup(key: int) returns (found: bool, value: string)
    requires Valid()
    ensures found ==> key in entries && value == entries[key]
    ensures !found ==> key !in entries
  {
    if key in entries {
      found := true; value := entries[key];
    } else {
      found := false; value := "";
    }
  }
}