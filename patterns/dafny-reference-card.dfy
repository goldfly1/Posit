// Dafny Language Dictionary — 86 entries, 5 words average
// The model knows Dafny. This is the vocabulary list, not a tutorial.
// Used by: DafnyImplementationPhase, DafnyFixer, PseudocodeReducer.
// Crystallization check: every line of pseudocode must use tokens from here.

// ─── Types ───
// bool: true/false value type
// int: arbitrary-precision integer, supports +, -, *, /, %
// nat: non-negative integer (subset of int)
// real: real/rational number
// char: single character, compared with ==
// string: immutable sequence of characters, = seq<char>
// ord: ordinal type for well-founded ordering
// bv<N>: N-bit bit-vector, supports bit operations
// seq<T>: immutable sequence, |s| length, s[i] index, s[a..b] slice
// set<T>: immutable set, x in s test, union +, intersection *
// multiset<T>: set with counts, multiset[x] = count
// map<K,V>: immutable key-value map, m[k] lookup, k in m test
// imap<K,V>: infinite map (partial function)
// array<T>: mutable array, a[i] access, a[i := x] update
// array2<T>: 2D mutable array, a[i,j] access
// tuple: (a, b, c) — t.0, t.1, t.2 for access
// datatype: algebraic type: datatype Result = Success(v: int) | Failure(e: string)
// codatatype: lazy/infinite type: codatatype Stream = Cons(head: int, tail: Stream)
// class: reference type with fields, methods, constructor
// trait: interface/abstract class, types implement it
// iterator: generator type, yields values

// ─── Statements ───
// := : assignment
// var x := E: variable declaration + assignment
// if cond { } else { }: conditional branch
// if cond then A else B: conditional expression
// match e case C(x) => ... case D(y) => ...: pattern match
// while cond invariant I decreases D { }: loop with proof obligations
// for i := 0 to n do { }: counted loop
// break: exit loop
// continue: next iteration
// return: return from method
// print: output to console
// assert P: prove P or verification fails
// assume P: trust P without proof
// forall x :: P(x) requires R(x): universal quantifier expression
// exists x :: P(x): existential quantifier expression
// calc { }: step-by-step calculation proof
// s[i := x]: functional sequence update (new seq with element replaced)
// map[k := v]: functional map update
// x as int: type cast
// x is Foo: type test (returns bool)
// old(e): value of e in method pre-state
// fresh(o): object o was allocated in this method
// |s|: cardinality (length of seq, size of set)
// let x := E; B: let binding expression

// ─── Specifications ───
// requires P: precondition — caller must prove P
// ensures P: postcondition — method must prove P
// decreases E: termination metric — must decrease each iteration/recursion
// invariant P: loop invariant — preserved each iteration
// reads S: function reads only locations in S
// modifies S: method modifies only locations in S

// ─── Declarations ───
// method Name(params) returns (r: T): executable method, verified by Z3
// function Name(params): T: pure function, no side effects, used in proofs
// predicate Name(params): function returning bool
// lemma Name(params) requires P ensures Q: proof obligation — prove Q from P
// const x: T := E: compile-time constant
// constructor: class initializer, ensures Valid()
// datatype variant: Success(v: T) | Failure(e: string) — constructor pattern

// ─── Modules ───
// module M { }: namespace + encapsulation
// import M: bring M's exports into scope
// import opened M: bring M's names directly (no M. prefix)
// export reveals ... provides ...: access control for module
// abstract module M: module with unspecified bodies, proof by contract only
// refines: module M refines N — M replaces N's bodies

// ─── Attributes ───
// {:extern}: I/O portal — Z3 assumes contract, C# implements body
// {:axiom}: bodyless method — suppresses missing body warning
// {:autocontracts}: auto-generate requires/ensures from Valid() predicate
// {:compile}: include in compilation output
// {:fuel N}: unfolding depth for function definitions in proofs
// {:induction}: auto-generate induction proof structure
// {:timeLimit N}: Z3 time limit in seconds for this method
// {:verify false}: skip Z3 verification for this method
// {:transparent}: reveal function body to callers
// {:tailrecursion}: enable tail call optimization
// {:synthesize}: let Z3 synthesize the body

// ─── Stdlib (import Std.X) ───
// Seq.Map(f, s): apply function f to each element
// Seq.Filter(p, s): keep elements where predicate p holds
// Seq.Sort(s): sort by natural order
// Seq.Reverse(s): reverse sequence
// Seq.Flatten(ss): flatten seq of seqs into one seq
// Seq.Range(a, b): seq of ints from a to b-1
// Seq.IndexOf(s, x): first index of x in s, or -1
// Seq.Contains(s, x): true if x in s
// Seq.Empty(): empty sequence []
// Seq.Singleton(x): single-element sequence [x]
// Seq.Concat(a, b): a + b — sequence concatenation

// ─── Common pitfalls ───
// 1. Don't use reads on string/seq/int/bool — they are value types
// 2. Don't access s[i] without proving 0 <= i < |s|
// 3. Don't slice s[n..] without proving 0 <= n <= |s|
// 4. Don't recurse without decreases
// 5. Use := for assignment, not =
// 6. type is a reserved keyword — don't use as parameter name
// 7. String comparison: compare element-by-element, not with ==
// 8. Integer division / truncates toward zero
// 9. ghost variables only in proofs, not in compiled code
// 10. match must cover all datatype variants (or have else case)