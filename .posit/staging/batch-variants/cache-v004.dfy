class Cache {
  var entries: map<int, string>
  var order: seq<int>
  const policy: string

  predicate Valid() reads this {
    |order| == |entries| &&
    (forall k :: k in entries ==> k in order) &&
    (forall k :: k in order ==> k in entries)
  }

  constructor(p: string)
    ensures Valid()
  {
    entries := map[];
    order := [];
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
      order := order + [key];
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