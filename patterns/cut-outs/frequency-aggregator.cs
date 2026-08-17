// Dafny program frequency-aggregator.dfy compiled into C#
// To recompile, you will need the libraries
//     System.Runtime.Numerics.dll System.Collections.Immutable.dll
// but the 'dotnet' tool in .NET should pick those up automatically.
// Optionally, you may want to include compiler switches like
//     /debug /nowarn:162,164,168,183,219,436,1717,1718

using System;
using System.Numerics;
using System.Collections;
[assembly: DafnyAssembly.DafnySourceAttribute(@"// dafny 4.11.0.0
// Command-line arguments: translate cs --no-verify --solver-path C:\Users\goldf\.dotnet\tools\z3\bin\z3.exe patterns/cut-outs/frequency-aggregator.dfy
// frequency-aggregator.dfy


module FrequencyAggregator {
  function IntToString(n: int): (s: string)
    requires n >= 0
    decreases n
  {
    if n < 10 then
      [""0123456789""[n]]
    else
      IntToString(n / 10) + [""0123456789""[n % 10]]
  }

  method CountFrequency(words: seq<string>) returns (result: seq<seq<string>>)
    requires |words| >= 0
    ensures |result| >= 0
    decreases |words|
  {
    var unique: seq<string> := [];
    var i := 0;
    while i < |words|
      invariant 0 <= i <= |words|
      invariant |unique| >= 0
      decreases |words| - i
    {
      var found := false;
      var j := 0;
      while j < |unique|
        invariant 0 <= j <= |unique|
        decreases |unique| - j
      {
        if words[i] == unique[j] {
          found := true;
        }
        j := j + 1;
      }
      if !found {
        unique := unique + [words[i]];
      }
      i := i + 1;
    }
    var counts: seq<int> := [];
    i := 0;
    while i < |unique|
      invariant 0 <= i <= |unique|
      invariant |counts| == i
      invariant forall k: int {:trigger counts[k]} :: 0 <= k < |counts| ==> counts[k] >= 0
      decreases |unique| - i
    {
      var count := 0;
      var j := 0;
      while j < |words|
        invariant 0 <= j <= |words|
        decreases |words| - j
      {
        if unique[i] == words[j] {
          count := count + 1;
        }
        j := j + 1;
      }
      counts := counts + [count];
      i := i + 1;
    }
    i := 0;
    while i < |counts|
      invariant 0 <= i <= |counts|
      invariant |counts| == |unique|
      invariant forall k: int {:trigger counts[k]} :: 0 <= k < |counts| ==> counts[k] >= 0
      decreases |counts| - i
    {
      var maxIdx := i;
      var j := i + 1;
      while j < |counts|
        invariant i + 1 <= j <= |counts|
        invariant 0 <= maxIdx < |counts|
        decreases |counts| - j
      {
        if counts[j] > counts[maxIdx] {
          maxIdx := j;
        }
        j := j + 1;
      }
      var tmpCount := counts[i];
      counts := counts[i := counts[maxIdx]];
      counts := counts[maxIdx := tmpCount];
      var tmpWord := unique[i];
      unique := unique[i := unique[maxIdx]];
      unique := unique[maxIdx := tmpWord];
      i := i + 1;
    }
    result := [];
    i := 0;
    while i < |unique|
      invariant 0 <= i <= |unique|
      invariant |result| == i
      invariant |counts| == |unique|
      invariant forall k: int {:trigger counts[k]} :: 0 <= k < |counts| ==> counts[k] >= 0
      decreases |unique| - i
    {
      result := result + [[IntToString(counts[i]), unique[i]]];
      i := i + 1;
    }
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
namespace FrequencyAggregator {

  public partial class __default {
    public static Dafny.ISequence<Dafny.Rune> IntToString(BigInteger n) {
      Dafny.ISequence<Dafny.Rune> _0___accumulator = Dafny.Sequence<Dafny.Rune>.FromElements();
    TAIL_CALL_START: ;
      if ((n) < (new BigInteger(10))) {
        return Dafny.Sequence<Dafny.Rune>.Concat(Dafny.Sequence<Dafny.Rune>.FromElements((Dafny.Sequence<Dafny.Rune>.UnicodeFromString("0123456789")).Select(n)), _0___accumulator);
      } else {
        _0___accumulator = Dafny.Sequence<Dafny.Rune>.Concat(Dafny.Sequence<Dafny.Rune>.FromElements((Dafny.Sequence<Dafny.Rune>.UnicodeFromString("0123456789")).Select(Dafny.Helpers.EuclideanModulus(n, new BigInteger(10)))), _0___accumulator);
        BigInteger _in0 = Dafny.Helpers.EuclideanDivision(n, new BigInteger(10));
        n = _in0;
        goto TAIL_CALL_START;
      }
    }
    public static Dafny.ISequence<Dafny.ISequence<Dafny.ISequence<Dafny.Rune>>> CountFrequency(Dafny.ISequence<Dafny.ISequence<Dafny.Rune>> words)
    {
      Dafny.ISequence<Dafny.ISequence<Dafny.ISequence<Dafny.Rune>>> result = Dafny.Sequence<Dafny.ISequence<Dafny.ISequence<Dafny.Rune>>>.Empty;
      Dafny.ISequence<Dafny.ISequence<Dafny.Rune>> _0_unique;
      _0_unique = Dafny.Sequence<Dafny.ISequence<Dafny.Rune>>.FromElements();
      BigInteger _1_i;
      _1_i = BigInteger.Zero;
      while ((_1_i) < (new BigInteger((words).Count))) {
        bool _2_found;
        _2_found = false;
        BigInteger _3_j;
        _3_j = BigInteger.Zero;
        while ((_3_j) < (new BigInteger((_0_unique).Count))) {
          if (((words).Select(_1_i)).Equals((_0_unique).Select(_3_j))) {
            _2_found = true;
          }
          _3_j = (_3_j) + (BigInteger.One);
        }
        if (!(_2_found)) {
          _0_unique = Dafny.Sequence<Dafny.ISequence<Dafny.Rune>>.Concat(_0_unique, Dafny.Sequence<Dafny.ISequence<Dafny.Rune>>.FromElements((words).Select(_1_i)));
        }
        _1_i = (_1_i) + (BigInteger.One);
      }
      Dafny.ISequence<BigInteger> _4_counts;
      _4_counts = Dafny.Sequence<BigInteger>.FromElements();
      _1_i = BigInteger.Zero;
      while ((_1_i) < (new BigInteger((_0_unique).Count))) {
        BigInteger _5_count;
        _5_count = BigInteger.Zero;
        BigInteger _6_j;
        _6_j = BigInteger.Zero;
        while ((_6_j) < (new BigInteger((words).Count))) {
          if (((_0_unique).Select(_1_i)).Equals((words).Select(_6_j))) {
            _5_count = (_5_count) + (BigInteger.One);
          }
          _6_j = (_6_j) + (BigInteger.One);
        }
        _4_counts = Dafny.Sequence<BigInteger>.Concat(_4_counts, Dafny.Sequence<BigInteger>.FromElements(_5_count));
        _1_i = (_1_i) + (BigInteger.One);
      }
      _1_i = BigInteger.Zero;
      while ((_1_i) < (new BigInteger((_4_counts).Count))) {
        BigInteger _7_maxIdx;
        _7_maxIdx = _1_i;
        BigInteger _8_j;
        _8_j = (_1_i) + (BigInteger.One);
        while ((_8_j) < (new BigInteger((_4_counts).Count))) {
          if (((_4_counts).Select(_8_j)) > ((_4_counts).Select(_7_maxIdx))) {
            _7_maxIdx = _8_j;
          }
          _8_j = (_8_j) + (BigInteger.One);
        }
        BigInteger _9_tmpCount;
        _9_tmpCount = (_4_counts).Select(_1_i);
        _4_counts = Dafny.Sequence<BigInteger>.Update(_4_counts, _1_i, (_4_counts).Select(_7_maxIdx));
        _4_counts = Dafny.Sequence<BigInteger>.Update(_4_counts, _7_maxIdx, _9_tmpCount);
        Dafny.ISequence<Dafny.Rune> _10_tmpWord;
        _10_tmpWord = (_0_unique).Select(_1_i);
        _0_unique = Dafny.Sequence<Dafny.ISequence<Dafny.Rune>>.Update(_0_unique, _1_i, (_0_unique).Select(_7_maxIdx));
        _0_unique = Dafny.Sequence<Dafny.ISequence<Dafny.Rune>>.Update(_0_unique, _7_maxIdx, _10_tmpWord);
        _1_i = (_1_i) + (BigInteger.One);
      }
      result = Dafny.Sequence<Dafny.ISequence<Dafny.ISequence<Dafny.Rune>>>.FromElements();
      _1_i = BigInteger.Zero;
      while ((_1_i) < (new BigInteger((_0_unique).Count))) {
        result = Dafny.Sequence<Dafny.ISequence<Dafny.ISequence<Dafny.Rune>>>.Concat(result, Dafny.Sequence<Dafny.ISequence<Dafny.ISequence<Dafny.Rune>>>.FromElements(Dafny.Sequence<Dafny.ISequence<Dafny.Rune>>.FromElements(FrequencyAggregator.__default.IntToString((_4_counts).Select(_1_i)), (_0_unique).Select(_1_i))));
        _1_i = (_1_i) + (BigInteger.One);
      }
      return result;
    }
  }
} // end of namespace FrequencyAggregator
namespace _module {

} // end of namespace _module
