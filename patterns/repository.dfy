// Pattern: Repository (Approach 3 — pre-written body with parameters)
// responsibility: Store items with ID uniqueness, lookup, and update
// test: Add([], Record(1, "test", "data")) returns Success([Record(1, "test", "data")])
// test: Add([Record(1, "a", "b")], Record(1, "c", "d")) returns Failure("duplicate id")
//
// Parameters:
//   idField: string — name of the ID field (for error messages)
//   allowUpdate: bool — whether Add replaces existing IDs (default false)

include "result.dfy"

datatype Entity =
  | Record(id: int, name: string, data: string)

// Invariant: no duplicate IDs
predicate NoDuplicates(items: seq<Entity>)
{
  forall i, j :: 0 <= i < j < |items| ==> items[i].id != items[j].id
}

// Add: insert if ID not present, fail if duplicate
method Add(items: seq<Entity>, entity: Entity) returns (result: Result<seq<Entity>>)
  requires NoDuplicates(items)
  ensures result.Success? ==> |result.value| == |items| + 1
  ensures result.Failure? ==> result.error == "duplicate id"
  decreases |items|
{
  var i := 0;
  var found := false;
  while i < |items| && !found
    invariant 0 <= i <= |items|
    invariant NoDuplicates(items)
    decreases |items| - i
  {
    if items[i].id == entity.id {
      found := true;
    }
    i := i + 1;
  }

  if found {
    result := Failure("duplicate id");
  } else {
    result := Success(items + [entity]);
  }
}

// Find: lookup by ID
method Find(items: seq<Entity>, id: int) returns (result: Result<Entity>)
  ensures result.Success? ==> result.value.id == id
  decreases |items|
{
  var i := 0;
  while i < |items|
    invariant 0 <= i <= |items|
    decreases |items| - i
  {
    if items[i].id == id {
      result := Success(items[i]);
      return;
    }
    i := i + 1;
  }
  result := Failure("not found");
}

// Remove: delete by ID
method Remove(items: seq<Entity>, id: int) returns (result: Result<seq<Entity>>)
  requires NoDuplicates(items)
  ensures result.Success? ==> |result.value| <= |items|
  decreases |items|
{
  var i := 0;
  var found := false;
  while i < |items| && !found
    invariant 0 <= i <= |items|
    decreases |items| - i
  {
    if items[i].id == id {
      found := true;
    }
    i := i + 1;
  }

  if found {
    var newItems := [];
    var j := 0;
    while j < |items|
      invariant 0 <= j <= |items|
      invariant |newItems| <= j
      decreases |items| - j
    {
      if items[j].id != id {
        newItems := newItems + [items[j]];
      }
      j := j + 1;
    }
    result := Success(newItems);
  } else {
    result := Failure("not found");
  }
}