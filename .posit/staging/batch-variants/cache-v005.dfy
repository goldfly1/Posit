class Cache {
  var entries: map<int, string>
  var ttl: map<int, int>
  var order: seq<int>
  var time: int
  const policy: string

  predicate Valid() reads this {
    (forall k :: k in entries ==> k in ttl) &&
    (forall k :: k in ttl ==> k in entries) &&
    |order| == |entries| &&
    (forall k :: k in entries ==> k in order) &&
    (forall k :: k in order ==> k in entries) &&
    (forall i, j :: 0 <= i < j < |order| ==> order[i] != order[j])
  }

  constructor(p: string)
    ensures Valid()
  {
    entries := map[];
    ttl := map[];
    order := [];
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
    if key !in entries {
      order := order + [key];
    }
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
      found := true; value := entries[key];
    } else {
      found := false; value := "";
      if key in entries {
        entries := entries - {key};
        ttl := ttl - {key};
        FilterSeqLemma(order, key);
        order := FilterSeq(order, key);
      }
    }
  }
}

function FilterSeq(s: seq<int>, k: int): seq<int>
  decreases |s|
{
  if |s| == 0 then []
  else if s[0] == k then s[1..]
  else [s[0]] + FilterSeq(s[1..], k)
}

lemma FilterSeqLemma(s: seq<int>, k: int)
  requires k in s
  requires forall i, j :: 0 <= i < j < |s| ==> s[i] != s[j]
  ensures |FilterSeq(s, k)| == |s| - 1
  ensures multiset(FilterSeq(s, k)) == multiset(s) - multiset{ k }
  decreases |s|
{
  if |s| == 0 {
  } else if s[0] == k {
    assert multiset(s) == multiset(s[1..]) + multiset{ k };
  } else {
    FilterSeqLemma(s[1..], k);
  }
}