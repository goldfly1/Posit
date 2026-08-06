// Pattern: Transformer
// Input -> stages -> output pipeline.
// Pre-cut stub: customize the stages and input/output types.

// Transform: seq<string> -> seq<string>
// Example: data mapper, ETL step, format converter
method Transform(input: seq<string>) returns (output: seq<string>)
  requires |input| > 0
  ensures |output| >= 0
  // approach: apply each stage in sequence, accumulate output