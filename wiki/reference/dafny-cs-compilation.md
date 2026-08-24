# Dafny C# Compilation Reference

How Dafny types, methods, and extern declarations compile to C#.
This is the reference for the C# target language code generation.

## Type Mapping (Dafny → C#)

| Dafny type | C# target type | Companion class |
|------------|---------------|-----------------|
| `int` | `BigInteger` | none |
| `real` | `BigRational` | none |
| `bool` | `bool` | none |
| `char` (--unicode-char=false) | `char` | none |
| `char` (--unicode-char=true) | `Dafny.Rune` | none |
| `ORDINAL` | `BigInteger` | none |
| bitvectors | `byte`, `ushort`, `uint`, `ulong`, or `BigInteger` | none |
| `string` (= `seq<char>`) | `ISequence<char>` or `ISequence<Rune>` | `Sequence<T>` |
| `seq<T>` | `ISequence<T>` | `Sequence<T>` |
| `set<T>` | `ISet<T>` | `Set<T>` |
| `multiset<T>` | `IMultiset<T>` | `Multiset<T>` |
| `map<K,V>` | `IMap<K,V>` | `Map<K,V>` |
| `array<T>` | `T[]` | none |
| `array2<T>` | `T[,]` | none |
| datatype `D` | interface or class `D` | class `D` |
| trait `Tr` | interface `_Companion_Tr` | none |
| class `Cl` | `Cl` | none |
| `T ~> U` (partial function) | `System.Func<T,U>` | none |
| subset type `S` of `B` | `B` (same as base) | `S` |
| newtype `NT` of `B` | `B` (same as base) | `NT` |
| type parameter `T` | `T` | none |

## Key Type Details

### string = seq<char>
Dafny `string` is an alias for `seq<char>`. In C#:
- With `--unicode-char=false`: `string` → `ISequence<char>`, `char` → `char`
- With `--unicode-char=true`: `string` → `ISequence<Rune>`, `char` → `Dafny.Rune`

### seq<string> = seq<seq<char>>
`seq<string>` in Dafny translates to `ISequence<ISequence<char>>` or `ISequence<ISequence<Rune>>` in C#.
This is a 2D sequence — a sequence of sequences.

### seq<seq<string>> = seq<seq<seq<char>>>
Translates to `ISequence<ISequence<ISequence<char/Rune>>>` — 3D sequence.

## ISequence<T> API (C# runtime)

The `ISequence<T>` interface is the C# representation of Dafny `seq<T>`.

Key properties and methods:
- `.Count` — int property for length (NOT `.Length`)
- `.Select(i)` — element at index i (THIS IS the indexer, NOT LINQ)
- `.CloneAsArray()` — returns `T[]` copy
- `.Contains(g)` — bool membership check
- `.Take(n)` / `.Drop(n)` — subsequence operations
- Implements `IEnumerable<T>` so LINQ works

### String Conversions

```csharp
// string → ISequence<Rune> (Dafny string)
Dafny.Sequence<Dafny.Rune>.UnicodeFromString(s)

// ISequence<Rune> → string
new string(seq.Select(r => (char)r.Value).ToArray())

// ISequence<ISequence<Rune>> → string (join rows with newline)
string.Join("\n", seq.Select(row => new string(row.Select(r => (char)r.Value).ToArray())))

// string[] → ISequence<Rune> (join lines into one string)
Dafny.Sequence<Dafny.Rune>.UnicodeFromString(string.Join("\n", arr))

// string[] → ISequence<ISequence<Rune>> (array → seq of seqs)
Dafny.Sequence<Dafny.ISequence<Dafny.Rune>>.FromArray(
    arr.Select(s => Dafny.Sequence<Dafny.Rune>.UnicodeFromString(s)).ToArray())
```

## Extern Declarations (Calling C# from Dafny)

`{:extern}` on a Dafny method means the body is in C#, not Dafny.

### Static C# method
```dafny
method {:extern "Demo", "p"} p()
```
- First arg: fully-qualified C# class name
- Second arg: C# method name
- No body in Dafny — implementation is in C#

### Non-static C# method
```dafny
module {:extern "demo"} M {
  method {:extern "demo.Demo", "newDemo"} newDemo() returns (r: Demo)
  class {:extern "Demo"} Demo {
    method {:extern "demo.Demo", "p"} p()
  }
}
```

### Module-level extern
```dafny
module {:extern "MyNamespace"} M { ... }
```
The module's C# namespace is `MyNamespace`.

## Default Values and Type Descriptors

The compiler generates `Default()` methods for auto-init types:
- `int`: `Dafny.Helpers.INT` (type descriptor, `Default()` returns 0)
- `real`: `Dafny.Helpers.REAL`
- `bool`: `Dafny.Helpers.BOOL`
- `char`: `Dafny.Helpers.CHAR`
- Collections: `Sequence<T>._TypeDescriptor()` (returns empty sequence)
- Datatypes: `D._TypeDescriptor(typeDescriptors, ...)` (returns grounding constructor)

## Dafny.Rune

When `--unicode-char=true` (default in Dafny 4.x):
- `char` → `Dafny.Rune` (struct wrapping an int)
- `string` → `ISequence<Dafny.Rune>`
- `Dafny.Rune` is a struct with `.Value` property returning the int code point
- Construct via `new Dafny.Rune(int)` — validates Unicode range
- Convert to char: `(char)rune.Value`

## Common C# Translation Patterns

### Loop with invariant
```dafny
while i < |lines|
  invariant 0 <= i <= |lines|
  decreases |lines| - i
{ ... }
```
Translates to a C# `while` loop. Invariants are NOT in C# output (verification only).

### Method with requires/ensures
```dafny
method AnalyzeLogs(lines: seq<string>, filterLevel: string) returns (count: int)
  requires |lines| >= 0
  ensures count >= 0
{ ... }
```
Translates to a C# method. `requires`/`ensures` are NOT in C# output.
`int` becomes `BigInteger` in C#.

### Function (pure expression)
```dafny
function ExtractLevel(line: string): string
{ ... pure expression ... }
```
Translates to a C# method (not a property). Can be called from `requires`/`ensures`.

### Datatype
```dafny
datatype Result = Success(value: int) | Error(msg: string)
```
Translates to C# class hierarchy with `Success` and `Error` subclasses.
Properties are `dtor_value`, `dtor_msg` (prefix `dtor_` on translated properties).