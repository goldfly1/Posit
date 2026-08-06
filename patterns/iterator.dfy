// Pattern: Iterator
// Traverse a collection with position tracking.
// Pre-cut stub: customize the collection type and traversal order.

// Iterator state: position in sequence
datatype IteratorState<T> =
  | AtEnd
  | AtPosition(pos: int, value: T, remaining: seq<T>)

// Next: advance iterator one step
method Next<T>(state: IteratorState<T>) returns (next: IteratorState<T>)
  ensures next.AtEnd? || next.AtPosition?
  // approach: if at position, advance pos, get next value or AtEnd