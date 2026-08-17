// Dafny Reference Card — extracted from verified stdlib
// Include this in prompts to prevent common errors.

// ─── Strings are value types, NOT objects ──────────────────────────────
// WRONG: function Foo(s: string) reads s { ... }     // ERROR: reads on value
// RIGHT: function Foo(s: string) { ... }              // No reads needed

// ─── Seq indexing requires bounds proof ───────────────────────────────
function First<T>(xs: seq<T>): (x: T)
  requires |xs| > 0        // MUST prove non-empty before accessing xs[0]
{
  xs[0]
}

function Last<T>(xs: seq<T>): (x: T)
  requires |xs| > 0
{
  xs[|xs| - 1]
}

// ─── Slicing ──────────────────────────────────────────────────────────
// s[..n] — first n elements, requires 0 <= n <= |s|
// s[n..] — elements from n onward, requires 0 <= n <= |s|
// s[a..b] — elements a..b, requires 0 <= a <= b <= |s|
// ALWAYS prove bounds before slicing. Z3 will reject s[idx+1..] if it
// can't prove idx+1 <= |s|.

// ─── Recursion MUST have decreases ─────────────────────────────────────
function CountOccurrences(s: string, c: char): int
  decreases |s|          // MUST supply decreases for recursive functions
{
  if |s| == 0 then 0
  else if s[0] == c then 1 + CountOccurrences(s[1..], c)
  else CountOccurrences(s[1..], c)
}

// ─── Loop invariants ───────────────────────────────────────────────────
method SumArray(arr: seq<int>) returns (total: int)
  requires forall i :: 0 <= i < |arr| ==> arr[i] >= 0
  ensures total >= 0
{
  total := 0;
  var i := 0;
  while i < |arr|
    invariant 0 <= i <= |arr|       // bounds invariant
    invariant total >= 0            // accumulated property
    decreases |arr| - i             // termination metric
  {
    total := total + arr[i];
    i := i + 1;
  }
}

// ─── datatype (enums and records) ─────────────────────────────────────
datatype Color = Red | Green | Blue(value: int)
datatype Result<T> = Success(value: T) | Failure(error: string)

// Pattern matching on datatypes:
function IsSuccess<T>(r: Result<T>): bool
{
  match r
  case Success(_) => true
  case Failure(_) => false
}

// ─── Class with invariant ──────────────────────────────────────────────
class Counter {
  var count: int
  predicate Valid() reads this { count >= 0 }

  constructor()
    ensures Valid()
  {
    count := 0;
  }

  method Inc()
    requires Valid()
    modifies this
    ensures Valid()
    ensures count == old(count) + 1
  {
    count := count + 1;
  }
}

// ─── {:extern} for I/O portals ─────────────────────────────────────────
// Z3 assumes the contract. C# implements the body in a partial class.
method {:extern} ReadFile(path: string) returns (content: string)
  requires |path| > 0
  ensures |content| >= 0

// ─── {:axiom} for bodyless methods ────────────────────────────────────
// Suppresses warnings on bodyless methods (abstract specs).
method {:axiom} Process(input: string) returns (output: string)
  requires |input| > 0
  ensures |output| >= 0

// ─── Common pitfalls ──────────────────────────────────────────────────
// 1. Don't use reads on string/seq/int/bool — they are value types
// 2. Don't access s[i] without proving 0 <= i < |s|
// 3. Don't slice s[n..] without proving 0 <= n <= |s|
// 4. Don't recurse without decreases
// 5. Don't assert without proving from invariants
// 6. Use := for assignment, not =
// 7. type is a reserved keyword — don't use as parameter name
// 8. char comparison: use == (works), string comparison: use SeqEqual or
//    compare element-by-element

// ─── Seq composition (NO cut-out needed for these) ────────────────────
// Concatenation: rows1 + rows2 joins two sequences
method MergeRows(rows1: seq<seq<string>>, rows2: seq<seq<string>>) returns (merged: seq<seq<string>>)
  ensures merged == rows1 + rows2
{
  merged := rows1 + rows2;
}

// Append one element: seq + [element]
method AddRow(rows: seq<seq<string>>, row: seq<string>) returns (result: seq<seq<string>>)
  ensures result == rows + [row]
{
  result := rows + [row];
}

// String concatenation: s1 + s2
// Length: |s|
// Element access: s[i] (requires 0 <= i < |s|)
// Slice: s[a..b] (requires 0 <= a <= b <= |s|)
// Empty seq: [] (with type annotation: var x: seq<string> := [])