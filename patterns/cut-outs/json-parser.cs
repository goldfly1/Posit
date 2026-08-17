// Dafny program json-parser.dfy compiled into C#
// To recompile, you will need the libraries
//     System.Runtime.Numerics.dll System.Collections.Immutable.dll
// but the 'dotnet' tool in .NET should pick those up automatically.
// Optionally, you may want to include compiler switches like
//     /debug /nowarn:162,164,168,183,219,436,1717,1718

using System;
using System.Numerics;
using System.Collections;
[assembly: DafnyAssembly.DafnySourceAttribute(@"// dafny 4.11.0.0
// Command-line arguments: translate cs --solver-path C:\Users\goldf\.dotnet\tools\z3\bin\z3.exe --no-verify patterns/cut-outs/json-parser.dfy
// json-parser.dfy

method ParseJsonToArray(json: string) returns (rows: seq<seq<string>>)
  requires |json| >= 2
  ensures |rows| >= 1
  decreases |json|
{
  rows := [];
  var i := 0;
  while i < |json| && json[i] != '['
    invariant 0 <= i <= |json|
    decreases |json| - i
  {
    i := i + 1;
  }
  if i >= |json| - 1 {
    rows := [[""""]];
    return;
  }
  i := i + 1;
  var headers: seq<string> := [];
  var first := true;
  while i < |json| - 1
    invariant 0 <= i <= |json|
    invariant |rows| >= 0
    decreases |json| - i
  {
    while i < |json| && (json[i] == ',' || json[i] == ' ' || json[i] == '\n' || json[i] == '\r' || json[i] == '\t')
      invariant 0 <= i <= |json|
      decreases |json| - i
    {
      i := i + 1;
    }
    if i >= |json| || json[i] == ']' {
      break;
    }
    if json[i] != '{' {
      i := i + 1;
      continue;
    }
    i := i + 1;
    var fields: seq<string> := [];
    var fieldNames: seq<string> := [];
    while i < |json| && json[i] != '}'
      invariant 0 <= i <= |json|
      decreases |json| - i
    {
      while i < |json| && (json[i] == ' ' || json[i] == ',' || json[i] == '\n')
        invariant 0 <= i <= |json|
        decreases |json| - i
      {
        i := i + 1;
      }
      if i >= |json| || json[i] == '}' {
        break;
      }
      if json[i] == '""' {
        var keyStart := i + 1;
        i := i + 1;
        while i < |json| && json[i] != '""'
          invariant 0 <= i <= |json|
          decreases |json| - i
        {
          i := i + 1;
        }
        var key := json[keyStart .. i];
        if i < |json| {
          i := i + 1;
        }
        fieldNames := fieldNames + [key];
        while i < |json| && (json[i] == ':' || json[i] == ' ')
          invariant 0 <= i <= |json|
          decreases |json| - i
        {
          i := i + 1;
        }
        if i < |json| && json[i] == '""' {
          var valStart := i + 1;
          i := i + 1;
          while i < |json| && json[i] != '""'
            invariant 0 <= i <= |json|
            decreases |json| - i
          {
            i := i + 1;
          }
          var val := json[valStart .. i];
          if i < |json| {
            i := i + 1;
          }
          fields := fields + [val];
        } else {
          fields := fields + [""""];
        }
      } else {
        i := i + 1;
      }
    }
    if i < |json| && json[i] == '}' {
      i := i + 1;
    }
    if first {
      headers := fieldNames;
      rows := rows + [headers];
      first := false;
    }
    rows := rows + [fields];
  }
  if |rows| == 0 {
    rows := [[""""]];
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
    public static Dafny.ISequence<Dafny.ISequence<Dafny.ISequence<Dafny.Rune>>> ParseJsonToArray(Dafny.ISequence<Dafny.Rune> json)
    {
      Dafny.ISequence<Dafny.ISequence<Dafny.ISequence<Dafny.Rune>>> rows = Dafny.Sequence<Dafny.ISequence<Dafny.ISequence<Dafny.Rune>>>.Empty;
      rows = Dafny.Sequence<Dafny.ISequence<Dafny.ISequence<Dafny.Rune>>>.FromElements();
      BigInteger _0_i;
      _0_i = BigInteger.Zero;
      while (((_0_i) < (new BigInteger((json).Count))) && (((json).Select(_0_i)) != (new Dafny.Rune('[')))) {
        _0_i = (_0_i) + (BigInteger.One);
      }
      if ((_0_i) >= ((new BigInteger((json).Count)) - (BigInteger.One))) {
        rows = Dafny.Sequence<Dafny.ISequence<Dafny.ISequence<Dafny.Rune>>>.FromElements(Dafny.Sequence<Dafny.ISequence<Dafny.Rune>>.FromElements(Dafny.Sequence<Dafny.Rune>.UnicodeFromString("")));
        return rows;
      }
      _0_i = (_0_i) + (BigInteger.One);
      Dafny.ISequence<Dafny.ISequence<Dafny.Rune>> _1_headers;
      _1_headers = Dafny.Sequence<Dafny.ISequence<Dafny.Rune>>.FromElements();
      bool _2_first;
      _2_first = true;
      while ((_0_i) < ((new BigInteger((json).Count)) - (BigInteger.One))) {
        while (((_0_i) < (new BigInteger((json).Count))) && (((((((json).Select(_0_i)) == (new Dafny.Rune(','))) || (((json).Select(_0_i)) == (new Dafny.Rune(' ')))) || (((json).Select(_0_i)) == (new Dafny.Rune('\n')))) || (((json).Select(_0_i)) == (new Dafny.Rune('\r')))) || (((json).Select(_0_i)) == (new Dafny.Rune('\t'))))) {
          _0_i = (_0_i) + (BigInteger.One);
        }
        if (((_0_i) >= (new BigInteger((json).Count))) || (((json).Select(_0_i)) == (new Dafny.Rune(']')))) {
          goto after_0;
        }
        if (((json).Select(_0_i)) != (new Dafny.Rune('{'))) {
          _0_i = (_0_i) + (BigInteger.One);
          goto continue_0;
        }
        _0_i = (_0_i) + (BigInteger.One);
        Dafny.ISequence<Dafny.ISequence<Dafny.Rune>> _3_fields;
        _3_fields = Dafny.Sequence<Dafny.ISequence<Dafny.Rune>>.FromElements();
        Dafny.ISequence<Dafny.ISequence<Dafny.Rune>> _4_fieldNames;
        _4_fieldNames = Dafny.Sequence<Dafny.ISequence<Dafny.Rune>>.FromElements();
        while (((_0_i) < (new BigInteger((json).Count))) && (((json).Select(_0_i)) != (new Dafny.Rune('}')))) {
          while (((_0_i) < (new BigInteger((json).Count))) && (((((json).Select(_0_i)) == (new Dafny.Rune(' '))) || (((json).Select(_0_i)) == (new Dafny.Rune(',')))) || (((json).Select(_0_i)) == (new Dafny.Rune('\n'))))) {
            _0_i = (_0_i) + (BigInteger.One);
          }
          if (((_0_i) >= (new BigInteger((json).Count))) || (((json).Select(_0_i)) == (new Dafny.Rune('}')))) {
            goto after_1;
          }
          if (((json).Select(_0_i)) == (new Dafny.Rune('"'))) {
            BigInteger _5_keyStart;
            _5_keyStart = (_0_i) + (BigInteger.One);
            _0_i = (_0_i) + (BigInteger.One);
            while (((_0_i) < (new BigInteger((json).Count))) && (((json).Select(_0_i)) != (new Dafny.Rune('"')))) {
              _0_i = (_0_i) + (BigInteger.One);
            }
            Dafny.ISequence<Dafny.Rune> _6_key;
            _6_key = (json).Subsequence(_5_keyStart, _0_i);
            if ((_0_i) < (new BigInteger((json).Count))) {
              _0_i = (_0_i) + (BigInteger.One);
            }
            _4_fieldNames = Dafny.Sequence<Dafny.ISequence<Dafny.Rune>>.Concat(_4_fieldNames, Dafny.Sequence<Dafny.ISequence<Dafny.Rune>>.FromElements(_6_key));
            while (((_0_i) < (new BigInteger((json).Count))) && ((((json).Select(_0_i)) == (new Dafny.Rune(':'))) || (((json).Select(_0_i)) == (new Dafny.Rune(' '))))) {
              _0_i = (_0_i) + (BigInteger.One);
            }
            if (((_0_i) < (new BigInteger((json).Count))) && (((json).Select(_0_i)) == (new Dafny.Rune('"')))) {
              BigInteger _7_valStart;
              _7_valStart = (_0_i) + (BigInteger.One);
              _0_i = (_0_i) + (BigInteger.One);
              while (((_0_i) < (new BigInteger((json).Count))) && (((json).Select(_0_i)) != (new Dafny.Rune('"')))) {
                _0_i = (_0_i) + (BigInteger.One);
              }
              Dafny.ISequence<Dafny.Rune> _8_val;
              _8_val = (json).Subsequence(_7_valStart, _0_i);
              if ((_0_i) < (new BigInteger((json).Count))) {
                _0_i = (_0_i) + (BigInteger.One);
              }
              _3_fields = Dafny.Sequence<Dafny.ISequence<Dafny.Rune>>.Concat(_3_fields, Dafny.Sequence<Dafny.ISequence<Dafny.Rune>>.FromElements(_8_val));
            } else {
              _3_fields = Dafny.Sequence<Dafny.ISequence<Dafny.Rune>>.Concat(_3_fields, Dafny.Sequence<Dafny.ISequence<Dafny.Rune>>.FromElements(Dafny.Sequence<Dafny.Rune>.UnicodeFromString("")));
            }
          } else {
            _0_i = (_0_i) + (BigInteger.One);
          }
        continue_1: ;
        }
      after_1: ;
        if (((_0_i) < (new BigInteger((json).Count))) && (((json).Select(_0_i)) == (new Dafny.Rune('}')))) {
          _0_i = (_0_i) + (BigInteger.One);
        }
        if (_2_first) {
          _1_headers = _4_fieldNames;
          rows = Dafny.Sequence<Dafny.ISequence<Dafny.ISequence<Dafny.Rune>>>.Concat(rows, Dafny.Sequence<Dafny.ISequence<Dafny.ISequence<Dafny.Rune>>>.FromElements(_1_headers));
          _2_first = false;
        }
        rows = Dafny.Sequence<Dafny.ISequence<Dafny.ISequence<Dafny.Rune>>>.Concat(rows, Dafny.Sequence<Dafny.ISequence<Dafny.ISequence<Dafny.Rune>>>.FromElements(_3_fields));
      continue_0: ;
      }
    after_0: ;
      if ((new BigInteger((rows).Count)).Sign == 0) {
        rows = Dafny.Sequence<Dafny.ISequence<Dafny.ISequence<Dafny.Rune>>>.FromElements(Dafny.Sequence<Dafny.ISequence<Dafny.Rune>>.FromElements(Dafny.Sequence<Dafny.Rune>.UnicodeFromString("")));
      }
      return rows;
    }
  }
} // end of namespace _module
