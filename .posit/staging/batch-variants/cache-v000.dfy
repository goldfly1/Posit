class Cache {
  var entries: map<int, string>
  var order: seq<int>
  const policy: string

  predicate Valid() reads this {
    |order| == |entries| &&
    (forall k :: k in entries ==> k in order) &&
    (forall k :: k in order ==> k in entries) &&
    (forall i, j :: 0 <= i < j < |order| ==> order[i] != order[j])
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
    if key in entries {
      RemoveFirstLemma(order, key);
      order := RemoveFirst(order, key) + [key];
    } else {
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
      found := true;
      value := entries[key];
    } else {
      found := false;
      value := "";
    }
  }
}

function RemoveFirst(s: seq<int>, k: int): seq<int>
  decreases |s|
{
  if |s| == 0 then []
  else if s[0] == k then s[1..]
  else [s[0]] + RemoveFirst(s[1..], k)
}

lemma RemoveFirstLemma(s: seq<int>, k: int)
  requires k in s
  requires forall i, j :: 0 <= i < j < |s| ==> s[i] != s[j]
  ensures |RemoveFirst(s, k)| == |s| - 1
  ensures multiset(RemoveFirst(s, k)) == multiset(s) - multiset{ k }
  decreases |s|
{
  if |s| == 0 {
  } else if s[0] == k {
    assert multiset(s) == multiset(s[1..]) + multiset{ k };
  } else {
    RemoveFirstLemma(s[1..], k);
  }
}