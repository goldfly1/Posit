class Cache {
  var entries: map<int, string>
  var ttl: map<int, int>
  var time: int
  const policy: string

  predicate Valid() reads this {
    (forall k :: k in entries ==> k in ttl) &&
    (forall k :: k in ttl ==> k in entries)
  }

  constructor(p: string)
    ensures Valid()
  {
    entries := map[];
    ttl := map[];
    time := 0;
    policy := p;
  }

  method Store(key: int, value: string)
    requires Valid()
    modifies this
    ensures Valid()
    ensures key in entries
    ensures entries[key] == value
  {
    time := time + 1;
    entries := entries[key := value];
    ttl := ttl[key := time + 100];
  }

  method Lookup(key: int) returns (found: bool, value: string)
    requires Valid()
    modifies this
    ensures Valid()
    ensures found ==> key in entries && value == entries[key]
    ensures !found ==> key !in entries
  {
    if key in entries && ttl[key] >= time {
      found := true;
      value := entries[key];
    } else {
      found := false;
      value := "";
      if key in entries {
        entries := entries - {key};
        ttl := ttl - {key};
      }
    }
  }
}