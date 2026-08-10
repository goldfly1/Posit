class Cache {
  var entries: map<int, string>
  var freq: map<int, int>
  const maxSize: int
  const policy: string

  predicate Valid() reads this {
    maxSize > 0 &&
    |entries| <= maxSize &&
    (forall k :: k in entries ==> k in freq) &&
    (forall k :: k in freq ==> k in entries)
  }

  constructor(maxS: int, p: string)
    requires maxS > 0
    ensures Valid()
  {
    entries := map[];
    freq := map[];
    maxSize := maxS;
    policy := p;
  }

  method Store(key: int, value: string)
    requires Valid()
    modifies this
    ensures Valid()
    ensures key in entries
    ensures entries[key] == value
  {
    if key in entries {
      entries := entries[key := value];
      freq := freq[key := freq[key] + 1];
    } else {
      if |entries| == maxSize {
        var evict :| evict in entries;
        entries := entries - {evict};
        freq := freq - {evict};
      }
      entries := entries[key := value];
      freq := freq[key := 1];
    }
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