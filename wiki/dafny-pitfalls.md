# DON'T LET THIS HAPPEN TO YOU — Dafny Pitfalls

These are hard rules of the Dafny language. They will ALWAYS fail.
No judgment calls — just don't do these things.

## 1. `function` with imperative code → must be `method`

`function` is a pure expression. No `var`, no `:=`, no `while`, no `if/else` with assignment, no `return`.

**BAD:**
```dafny
function SplitBySpace(s: string): seq<string>
{
  var idx := IndexOfSpace(s);  // ILLEGAL in function
  if idx < 0 then [s] else [s[..idx]] + SplitBySpace(s[(idx+1)..])
}
```

**GOOD:**
```dafny
method SplitBySpace(s: string) returns (parts: seq<string>)
{
  // imperative code is fine in a method
  var idx := IndexOfSpace(s);
  if idx < 0 { parts := [s]; }
  else { parts := [s[..idx]] + SplitBySpace(s[(idx+1)..]); }
}
```

Rule: if it has `var`, `:=`, `while`, or multi-statement body → `method`.
If it's a single pure expression (e.g. `v * 9.0 / 5.0 + 32.0`) → `function`.

## 2. `while` without `invariant` + `decreases` → Z3 always rejects

Every `while` loop MUST have both:
```dafny
while i < |lines|
  invariant 0 <= i <= |lines|
  decreases |lines| - i
{
  // ...
}
```

Keep invariants SIMPLE. `0 <= i <= |lines|` is good.
Don't try to capture the full mathematical relationship in the invariant —
Z3 can't prove complex invariants from the loop body alone.

## 3. Method call in `requires`/`ensures` → must be `function`

Contracts (`requires`, `ensures`) can only call `function`s, not `method`s.

**BAD:**
```dafny
method Process(data: seq<string>)
  ensures IsValid(data)  // IsValid must be a function, not a method
```

If you need a helper in a contract, write it as `function`.

## 4. Set comprehension without type → always parse error

**BAD:** `{j | 0 <= j < n && P(j)}`
**GOOD:** `{j: int | 0 <= j < n && P(j)}`

Always declare the variable type in set comprehensions.

## 5. C#-isms → not valid Dafny

These are C# syntax that Dafny rejects:
- `(char)x` → use `char(x)` or Dafny character syntax
- `new string[]` → Dafny has no `new`. Use `seq<T>` or arrays differently
- `for (int i = 0; i < n; i++)` → use `while i < n` with `invariant` + `decreases`
- `s.Length` → use `|s|` for sequences
- `s[i].ToString()` → use a helper function

## 6. Method called in expression context → must be `function`

Methods cannot appear in expressions (string concatenation, conditions, etc.).

**BAD:**
```dafny
summary := [[level, IntToString(count)]]  // if IntToString is a method
```

If `IntToString` is a `method`, this fails. Either:
- Make it a `function` (if it's a pure expression), or
- Call it in a separate `var` statement first:
```dafny
var countStr: string;
IntToString(count, countStr);  // method call as statement
summary := summary + [[level, countStr]];
```

## 7. `map[K]V` syntax → must be `map[K, V]`

**BAD:** `map[string, int]` written as `map[string]int`
**GOOD:** `map<string, int>`

Map uses comma between key and value type, all in angle brackets.

## 8. `seq[T]` syntax → must be `seq<T>`

**BAD:** `seq[string]` written as `seq[string]` with square brackets
**GOOD:** `seq<string>`

Sequences use angle brackets, not square brackets.

## 9. Simple invariants are better than complex ones

Z3 proves simple invariants. Complex invariants that try to capture
the full mathematical state of the loop usually fail.

**BAD:** `invariant filteredCount == |{j: int | 0 <= j < i && IsLevel(lines[j], filterLevel)}|`
(Z3 can't prove this — it requires reasoning about the set comprehension
that the loop body doesn't directly establish.)

**GOOD:** `invariant 0 <= i <= |lines|`
Let the postcondition (`ensures`) capture the mathematical relationship.
The invariant just needs to prove the loop terminates safely.

## 10. Don't reinvent string splitting — use simple loops

The Dafny stdlib doesn't have a `Split` function. When you need to split
a string by a delimiter, write a simple `method` with a `while` loop:

```dafny
method SplitByDelimiter(s: string, delim: char) returns (parts: seq<string>)
  ensures |parts| >= 1
  decreases |s|
{
  parts := [];
  var start := 0;
  var i := 0;
  while i < |s|
    invariant 0 <= start <= i <= |s|
    invariant start <= i
    decreases |s| - i
  {
    if s[i] == delim {
      parts := parts + [s[start..i]];
      start := i + 1;
    }
    i := i + 1;
  }
  if start <= |s| {
    parts := parts + [s[start..]];
  }
}
```

Don't try to do this recursively in a `function` — it won't verify.