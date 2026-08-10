class Cache {
  var entries: map<int, string>
  var freq: map<int, int>
  var ttl: map<int, int>
  var time: int
  const policy: string

  predicate Valid() reads this {
    (forall k :: k in entries ==> k in freq) &&
    (forall k :: k in freq ==> k in entries) &&
    (forall k :: k in entries ==> k in ttl) &&
    (forall k :: k in ttl ==> k in entries)
  }

  constructor(p: string)
    ensures Valid()
  {
    entries := map[];
    freq := map[];
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
    freq := freq[key := if key in freq then freq[key] + 1 else 1];
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
      found := true; value := entries[key];
      freq := freq[key := freq[key] + 1];
    } else {
      found := false; value := "";
      if key in entries {
        entries := entries - {key};
        freq := freq - {key};
        ttl := ttl - {key};
      }
    }
  }
}