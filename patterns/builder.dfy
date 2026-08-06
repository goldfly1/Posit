// Pattern: Builder
// Accumulate parts, build with invariants on the final result.
// Pre-cut stub: customize the part type, builder state, and invariants.

// Builder accumulates strings, builds a joined result
// Build: seq<string> -> string
// Example: request builder, query builder, message builder
method Build(parts: seq<string>) returns (result: string)
  requires |parts| > 0
  ensures |result| >= 0  // result is non-empty if all parts are non-empty
  // approach: iterate parts, join with separator, return assembled string