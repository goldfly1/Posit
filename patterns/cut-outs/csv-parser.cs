// Dafny program csv-parser.dfy compiled into C#
// To recompile, you will need the libraries
//     System.Runtime.Numerics.dll System.Collections.Immutable.dll
// but the 'dotnet' tool in .NET should pick those up automatically.
// Optionally, you may want to include compiler switches like
//     /debug /nowarn:162,164,168,183,219,436,1717,1718

using System;
using System.Numerics;
using System.Collections;
[assembly: DafnyAssembly.DafnySourceAttribute(@"// dafny 4.11.0.0
// Command-line arguments: translate cs C:\Users\goldf\Posit\patterns\cut-outs\csv-parser.dfy --no-verify --allow-external-contracts --allow-warnings --test-assumptions Externs --translate-standard-library false
// csv-parser.dfy

method ParseLine(input: string, delimiter: string) returns (fields: seq<string>)
  requires |input| >= 0
  requires |delimiter| == 1
  ensures |fields| >= 1
  decreases |input|
{
  fields := [];
  var currentField := """";
  var i := 0;
  var delim := delimiter[0];
  while i < |input|
    invariant 0 <= i <= |input|
    invariant |fields| >= 0
    decreases |input| - i
  {
    if input[i] == delim {
      fields := fields + [currentField];
      currentField := """";
    } else {
      currentField := currentField + [input[i]];
    }
    i := i + 1;
  }
  fields := fields + [currentField];
}

method ParseLines(lines: seq<string>, delimiter: string) returns (rows: seq<seq<string>>)
  requires |delimiter| == 1
  ensures |rows| == |lines|
  decreases |lines|
{
  rows := [];
  var i := 0;
  while i < |lines|
    invariant 0 <= i <= |lines|
    invariant |rows| == i
    decreases |lines| - i
  {
    var fields := ParseLine(lines[i], delimiter);
    rows := rows + [fields];
    i := i + 1;
  }
}

method CountFields(input: string, delimiter: string) returns (count: int)
  requires |input| >= 0
  requires |delimiter| == 1
  ensures count >= 1
  decreases |input|
{
  count := 1;
  var i := 0;
  var delim := delimiter[0];
  while i < |input|
    invariant 0 <= i <= |input|
    invariant count >= 1
    decreases |input| - i
  {
    if input[i] == delim {
      count := count + 1;
    }
    i := i + 1;
  }
}
")]

namespace Dafny {
  internal class ArrayHelpers {
    public static T[] InitNewArray1<T>(T z, BigInteger size0) {
      int s0 = (int)size0;
      T[] a = new T[s0];
      for (int i0 = 0; i0 < s0; i0++) {
        a[i0] = z;
      }
      return a;
    }
  }
} // end of namespace Dafny
internal static class FuncExtensions {
  public static Func<UResult> DowncastClone<TResult, UResult>(this Func<TResult> F, Func<TResult, UResult> ResConv) {
    return () => ResConv(F());
  }
  public static Func<U, UResult> DowncastClone<T, TResult, U, UResult>(this Func<T, TResult> F, Func<U, T> ArgConv, Func<TResult, UResult> ResConv) {
    return arg => ResConv(F(ArgConv(arg)));
  }
}
// end of class FuncExtensions
namespace _module {

  public partial class __default {
    public static Dafny.ISequence<Dafny.ISequence<Dafny.Rune>> ParseLine(Dafny.ISequence<Dafny.Rune> input, Dafny.ISequence<Dafny.Rune> delimiter)
    {
      Dafny.ISequence<Dafny.ISequence<Dafny.Rune>> fields = Dafny.Sequence<Dafny.ISequence<Dafny.Rune>>.Empty;
      fields = Dafny.Sequence<Dafny.ISequence<Dafny.Rune>>.FromElements();
      Dafny.ISequence<Dafny.Rune> _0_currentField;
      _0_currentField = Dafny.Sequence<Dafny.Rune>.UnicodeFromString("");
      BigInteger _1_i;
      _1_i = BigInteger.Zero;
      Dafny.Rune _2_delim;
      _2_delim = (delimiter).Select(BigInteger.Zero);
      while ((_1_i) < (new BigInteger((input).Count))) {
        if (((input).Select(_1_i)) == (_2_delim)) {
          fields = Dafny.Sequence<Dafny.ISequence<Dafny.Rune>>.Concat(fields, Dafny.Sequence<Dafny.ISequence<Dafny.Rune>>.FromElements(_0_currentField));
          _0_currentField = Dafny.Sequence<Dafny.Rune>.UnicodeFromString("");
        } else {
          _0_currentField = Dafny.Sequence<Dafny.Rune>.Concat(_0_currentField, Dafny.Sequence<Dafny.Rune>.FromElements((input).Select(_1_i)));
        }
        _1_i = (_1_i) + (BigInteger.One);
      }
      fields = Dafny.Sequence<Dafny.ISequence<Dafny.Rune>>.Concat(fields, Dafny.Sequence<Dafny.ISequence<Dafny.Rune>>.FromElements(_0_currentField));
      return fields;
    }
    public static Dafny.ISequence<Dafny.ISequence<Dafny.ISequence<Dafny.Rune>>> ParseLines(Dafny.ISequence<Dafny.ISequence<Dafny.Rune>> lines, Dafny.ISequence<Dafny.Rune> delimiter)
    {
      Dafny.ISequence<Dafny.ISequence<Dafny.ISequence<Dafny.Rune>>> rows = Dafny.Sequence<Dafny.ISequence<Dafny.ISequence<Dafny.Rune>>>.Empty;
      rows = Dafny.Sequence<Dafny.ISequence<Dafny.ISequence<Dafny.Rune>>>.FromElements();
      BigInteger _0_i;
      _0_i = BigInteger.Zero;
      while ((_0_i) < (new BigInteger((lines).Count))) {
        Dafny.ISequence<Dafny.ISequence<Dafny.Rune>> _1_fields;
        Dafny.ISequence<Dafny.ISequence<Dafny.Rune>> _out0;
        _out0 = __default.ParseLine((lines).Select(_0_i), delimiter);
        _1_fields = _out0;
        rows = Dafny.Sequence<Dafny.ISequence<Dafny.ISequence<Dafny.Rune>>>.Concat(rows, Dafny.Sequence<Dafny.ISequence<Dafny.ISequence<Dafny.Rune>>>.FromElements(_1_fields));
        _0_i = (_0_i) + (BigInteger.One);
      }
      return rows;
    }
    public static BigInteger CountFields(Dafny.ISequence<Dafny.Rune> input, Dafny.ISequence<Dafny.Rune> delimiter)
    {
      BigInteger count = BigInteger.Zero;
      count = BigInteger.One;
      BigInteger _0_i;
      _0_i = BigInteger.Zero;
      Dafny.Rune _1_delim;
      _1_delim = (delimiter).Select(BigInteger.Zero);
      while ((_0_i) < (new BigInteger((input).Count))) {
        if (((input).Select(_0_i)) == (_1_delim)) {
          count = (count) + (BigInteger.One);
        }
        _0_i = (_0_i) + (BigInteger.One);
      }
      return count;
    }
  }
} // end of namespace _module
