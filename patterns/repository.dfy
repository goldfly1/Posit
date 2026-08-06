// Pattern: Repository
// Store items with ID uniqueness and lookup.
// Pre-cut stub: customize the entity type and key type.
// Depends on: result.dfy (for Result type)

include "result.dfy"

datatype Entity =
  | Record(id: int, name: string, data: string)

// Invariant: no duplicate IDs
predicate NoDuplicates(items: seq<Entity>)
{
  forall i, j :: 0 <= i < j < |items| ==> items[i].id != items[j].id
}

// Add: insert if ID not present
method {:axiom} Add(items: seq<Entity>, entity: Entity) returns (result: Result<seq<Entity>>)
  requires NoDuplicates(items)
  ensures result.Success? ==> NoDuplicates(result.value)
  ensures result.Success? ==> |result.value| == |items| + 1
  ensures result.Failure? ==> result.error == "duplicate id"
  // approach: check if ID exists, if not append, if so return failure