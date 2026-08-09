// Pattern: Pipeline (Approach 3 — pre-written body with parameters)
// responsibility: Request -> middleware chain -> response
// test: RunPipeline([AddPrefix("X: "), AddSuffix(" [ok]")], "hello") returns "X: hello [ok]"
// test: RunPipeline([], "hello") returns "hello"
// test: RunPipeline([AddPrefix("pre-")], "hello") returns "pre-hello"
//
// Parameters:
//   middlewares: seq<Middleware> — ordered chain of request transformers
//   failFast: bool — whether to stop on first empty result (default true)
//
// Pre-cut planks: the chain iteration, result accumulation, and response
// building are all pre-written and Z3-proven. The architect sets the
// parameters. Imp's job is empty or near-empty.

include "result.dfy"

// A middleware is a named transformation: input string -> output string.
datatype Middleware =
  | AddPrefix(prefix: string)
  | AddSuffix(suffix: string)
  | ToUpperTransform
  | Identity

// Apply a single middleware to a string, producing the transformed string.
function ApplyMiddleware(mw: Middleware, input: string): string
  ensures |ApplyMiddleware(mw, input)| >= |input|
  decreases mw, |input|
{
  match mw
  case AddPrefix(prefix) => prefix + input
  case AddSuffix(suffix) => input + suffix
  case ToUpperTransform => ToUpperStr(input)
  case Identity => input
}

// Run the full pipeline: fold middlewares left-to-right over the input.
method RunPipeline(middlewares: seq<Middleware>, input: string) returns (output: string)
  ensures |output| >= |input|
  decreases |middlewares|
{
  output := input;
  var i := 0;
  while i < |middlewares|
    invariant 0 <= i <= |middlewares|
    invariant |output| >= |input|
    decreases |middlewares| - i
  {
    output := ApplyMiddleware(middlewares[i], output);
    i := i + 1;
  }
}

// Run pipeline and return a Result (Success with output, or Failure on empty
// middleware chain — though empty chain is valid, returns Success with input).
method RunPipelineResult(middlewares: seq<Middleware>, input: string) returns (result: Result<string>)
  ensures result.Success?
  ensures result.Success? ==> |result.value| >= |input|
  decreases |middlewares|
{
  var output := RunPipeline(middlewares, input);
  result := Success(output);
}

// Count how many middlewares in the chain are non-identity.
method CountActive(middlewares: seq<Middleware>) returns (count: int)
  ensures count >= 0
  ensures count <= |middlewares|
  decreases |middlewares|
{
  count := 0;
  var i := 0;
  while i < |middlewares|
    invariant 0 <= i <= |middlewares|
    invariant count <= i
    decreases |middlewares| - i
  {
    if middlewares[i] != Identity {
      count := count + 1;
    }
    i := i + 1;
  }
}

// Prepend a middleware to the front of an existing pipeline.
method PrependMiddleware(middlewares: seq<Middleware>, mw: Middleware) returns (chain: seq<Middleware>)
  ensures |chain| == |middlewares| + 1
  ensures chain[0] == mw
  decreases |middlewares|
{
  chain := [mw] + middlewares;
}

// Helper: uppercase a string (value-type recursion, no reads).
function ToUpperStr(s: string): string
  ensures |ToUpperStr(s)| == |s|
  decreases |s|
{
  if |s| == 0 then ""
  else if 'a' <= s[0] && s[0] <= 'z' then [CharUp(s[0])] + ToUpperStr(s[1..])
  else [s[0]] + ToUpperStr(s[1..])
}

// Helper: convert a single lowercase char to uppercase.
function CharUp(c: char): char
  ensures 'a' <= c <= 'z' ==> 'A' <= CharUp(c) <= 'Z'
{
  match c
  case 'a' => 'A'
  case 'b' => 'B'
  case 'c' => 'C'
  case 'd' => 'D'
  case 'e' => 'E'
  case 'f' => 'F'
  case 'g' => 'G'
  case 'h' => 'H'
  case 'i' => 'I'
  case 'j' => 'J'
  case 'k' => 'K'
  case 'l' => 'L'
  case 'm' => 'M'
  case 'n' => 'N'
  case 'o' => 'O'
  case 'p' => 'P'
  case 'q' => 'Q'
  case 'r' => 'R'
  case 's' => 'S'
  case 't' => 'T'
  case 'u' => 'U'
  case 'v' => 'V'
  case 'w' => 'W'
  case 'x' => 'X'
  case 'y' => 'Y'
  case 'z' => 'Z'
  case _ => c
}