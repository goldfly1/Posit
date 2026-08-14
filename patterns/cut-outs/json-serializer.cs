// Dafny program json-serializer.dfy compiled into C#
// To recompile, you will need the libraries
//     System.Runtime.Numerics.dll System.Collections.Immutable.dll
// but the 'dotnet' tool in .NET should pick those up automatically.
// Optionally, you may want to include compiler switches like
//     /debug /nowarn:162,164,168,183,219,436,1717,1718

using System;
using System.Numerics;
using System.Collections;
[assembly: DafnyAssembly.DafnySourceAttribute(@"// dafny 4.11.0.0
// Command-line arguments: translate cs C:\Users\goldf\Posit\patterns\cut-outs\json-serializer.dfy --no-verify --allow-external-contracts --allow-warnings --test-assumptions Externs --translate-standard-library false
// json-serializer.dfy

method SerializeToJson(rows: seq<seq<string>>) returns (json: string)
  requires |rows| >= 1
  ensures |json| >= 2
  decreases |rows|
{
  if |rows| == 1 {
    json := ""[]"";
    return;
  }
  var header := rows[0];
  var sb := ""["";
  var i := 1;
  while i < |rows|
    invariant 0 <= i <= |rows|
    invariant |sb| >= 1
    decreases |rows| - i
  {
    if i > 1 {
      sb := sb + "","";
    }
    sb := sb + ""{"";
    var row := rows[i];
    var j := 0;
    while j < |row| && j < |header|
      invariant 0 <= j <= |row|
      invariant 0 <= j <= |header|
      decreases |row| - j
    {
      if j > 0 {
        sb := sb + "","";
      }
      sb := sb + ""\"""" + header[j] + ""\"":"" + ""\"""" + row[j] + ""\"""";
      j := j + 1;
    }
    sb := sb + ""}"";
    i := i + 1;
  }
  sb := sb + ""]"";
  json := sb;
}

method SerializeRow(header: seq<string>, row: seq<string>) returns (json: string)
  requires |header| >= 0
  requires |row| >= 0
  decreases |row|
{
  json := ""{"";
  var i := 0;
  while i < |row| && i < |header|
    invariant 0 <= i <= |row|
    invariant 0 <= i <= |header|
    decreases |row| - i
  {
    if i > 0 {
      json := json + "","";
    }
    json := json + ""\"""" + header[i] + ""\"":"" + ""\"""" + row[i] + ""\"""";
    i := i + 1;
  }
  json := json + ""}"";
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
    public static Dafny.ISequence<Dafny.Rune> SerializeToJson(Dafny.ISequence<Dafny.ISequence<Dafny.ISequence<Dafny.Rune>>> rows)
    {
      Dafny.ISequence<Dafny.Rune> json = Dafny.Sequence<Dafny.Rune>.Empty;
      if ((new BigInteger((rows).Count)) == (BigInteger.One)) {
        json = Dafny.Sequence<Dafny.Rune>.UnicodeFromString("[]");
        return json;
      }
      Dafny.ISequence<Dafny.ISequence<Dafny.Rune>> _0_header;
      _0_header = (rows).Select(BigInteger.Zero);
      Dafny.ISequence<Dafny.Rune> _1_sb;
      _1_sb = Dafny.Sequence<Dafny.Rune>.UnicodeFromString("[");
      BigInteger _2_i;
      _2_i = BigInteger.One;
      while ((_2_i) < (new BigInteger((rows).Count))) {
        if ((_2_i) > (BigInteger.One)) {
          _1_sb = Dafny.Sequence<Dafny.Rune>.Concat(_1_sb, Dafny.Sequence<Dafny.Rune>.UnicodeFromString(","));
        }
        _1_sb = Dafny.Sequence<Dafny.Rune>.Concat(_1_sb, Dafny.Sequence<Dafny.Rune>.UnicodeFromString("{"));
        Dafny.ISequence<Dafny.ISequence<Dafny.Rune>> _3_row;
        _3_row = (rows).Select(_2_i);
        BigInteger _4_j;
        _4_j = BigInteger.Zero;
        while (((_4_j) < (new BigInteger((_3_row).Count))) && ((_4_j) < (new BigInteger((_0_header).Count)))) {
          if ((_4_j).Sign == 1) {
            _1_sb = Dafny.Sequence<Dafny.Rune>.Concat(_1_sb, Dafny.Sequence<Dafny.Rune>.UnicodeFromString(","));
          }
          _1_sb = Dafny.Sequence<Dafny.Rune>.Concat(Dafny.Sequence<Dafny.Rune>.Concat(Dafny.Sequence<Dafny.Rune>.Concat(Dafny.Sequence<Dafny.Rune>.Concat(Dafny.Sequence<Dafny.Rune>.Concat(Dafny.Sequence<Dafny.Rune>.Concat(_1_sb, Dafny.Sequence<Dafny.Rune>.UnicodeFromString("\"")), (_0_header).Select(_4_j)), Dafny.Sequence<Dafny.Rune>.UnicodeFromString("\":")), Dafny.Sequence<Dafny.Rune>.UnicodeFromString("\"")), (_3_row).Select(_4_j)), Dafny.Sequence<Dafny.Rune>.UnicodeFromString("\""));
          _4_j = (_4_j) + (BigInteger.One);
        }
        _1_sb = Dafny.Sequence<Dafny.Rune>.Concat(_1_sb, Dafny.Sequence<Dafny.Rune>.UnicodeFromString("}"));
        _2_i = (_2_i) + (BigInteger.One);
      }
      _1_sb = Dafny.Sequence<Dafny.Rune>.Concat(_1_sb, Dafny.Sequence<Dafny.Rune>.UnicodeFromString("]"));
      json = _1_sb;
      return json;
    }
    public static Dafny.ISequence<Dafny.Rune> SerializeRow(Dafny.ISequence<Dafny.ISequence<Dafny.Rune>> header, Dafny.ISequence<Dafny.ISequence<Dafny.Rune>> row)
    {
      Dafny.ISequence<Dafny.Rune> json = Dafny.Sequence<Dafny.Rune>.Empty;
      json = Dafny.Sequence<Dafny.Rune>.UnicodeFromString("{");
      BigInteger _0_i;
      _0_i = BigInteger.Zero;
      while (((_0_i) < (new BigInteger((row).Count))) && ((_0_i) < (new BigInteger((header).Count)))) {
        if ((_0_i).Sign == 1) {
          json = Dafny.Sequence<Dafny.Rune>.Concat(json, Dafny.Sequence<Dafny.Rune>.UnicodeFromString(","));
        }
        json = Dafny.Sequence<Dafny.Rune>.Concat(Dafny.Sequence<Dafny.Rune>.Concat(Dafny.Sequence<Dafny.Rune>.Concat(Dafny.Sequence<Dafny.Rune>.Concat(Dafny.Sequence<Dafny.Rune>.Concat(Dafny.Sequence<Dafny.Rune>.Concat(json, Dafny.Sequence<Dafny.Rune>.UnicodeFromString("\"")), (header).Select(_0_i)), Dafny.Sequence<Dafny.Rune>.UnicodeFromString("\":")), Dafny.Sequence<Dafny.Rune>.UnicodeFromString("\"")), (row).Select(_0_i)), Dafny.Sequence<Dafny.Rune>.UnicodeFromString("\""));
        _0_i = (_0_i) + (BigInteger.One);
      }
      json = Dafny.Sequence<Dafny.Rune>.Concat(json, Dafny.Sequence<Dafny.Rune>.UnicodeFromString("}"));
      return json;
    }
  }
} // end of namespace _module
