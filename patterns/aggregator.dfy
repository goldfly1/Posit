// Pattern: Aggregator
// Fold/reduce over a collection to produce a summary value.
// Pre-cut stub: customize the collection type, accumulator, and fold operation.

// Aggregate: seq<int> -> int
// Example: sum, count, average, min/max, histogram
method {:axiom} Aggregate(values: seq<int>) returns (total: int)
  requires |values| > 0
  ensures total == Sum(values)  // postcondition references the fold
  // approach: iterate values, accumulate into total

// Helper: sum of a sequence (used in postcondition)
function Sum(values: seq<int>): int
  decreases |values|
{
  if |values| == 0 then 0
  else values[0] + Sum(values[1..])
}