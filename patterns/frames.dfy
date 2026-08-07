// Pattern: Frames / Owned Heap
// Canonical framing for a class that owns mutable heap state (Repr).
// Use for classes that wrap arrays, linked structures, or other objects.
// Pattern source: Dafny standard-library examples (ghost predicate Valid + Repr).

class {{ComponentName}} {
  ghost var Repr: set<object>

  constructor()
    ensures Valid()
    ensures fresh(Repr)
  {
    Repr := {this};
  }

  ghost predicate Valid()
    reads this, Repr
    ensures Valid() ==> this in Repr
    decreases Repr, 0
  {
    && this in Repr
  }
}
