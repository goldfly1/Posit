// Pattern: Parser
// Parse a string input into a typed result.
// Pre-cut stub: customize the input/output types and contract.

include "result.dfy"

// Parse: string -> Result<seq<string>>
// Example: CSV line parser, JSON value parser, config parser
method Parse(input: string) returns (result: Result<seq<string>>)
  requires |input| > 0
  ensures result.Success? ==> |result.value| >= 1
  ensures result.Failure? ==> |result.error| > 0
  // approach: iterate input chars, split on delimiter, build sequence