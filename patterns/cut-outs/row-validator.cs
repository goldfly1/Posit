// Dafny program row-validator.dfy compiled into C#
// To recompile, you will need the libraries
//     System.Runtime.Numerics.dll System.Collections.Immutable.dll
// but the 'dotnet' tool in .NET should pick those up automatically.
// Optionally, you may want to include compiler switches like
//     /debug /nowarn:162,164,168,183,219,436,1717,1718

using System;
using System.Numerics;
using System.Collections;
[assembly: DafnyAssembly.DafnySourceAttribute(@"// dafny 4.11.0.0
// Command-line arguments: translate cs --solver-path C:\Users\goldf\.dotnet\tools\z3\bin\z3.exe --no-verify patterns/cut-outs/row-validator.dfy
// row-validator.dfy

method ValidateRows(rows: seq<seq<string>>) returns (outRows: seq<seq<string>>, isValid: bool)
  requires |rows| >= 0
  ensures outRows == rows
  decreases |rows|
{
  outRows := rows;
  if |rows| == 0 {
    isValid := true;
    return;
  }
  var expected := |rows[0]|;
  var i := 1;
  var allValid := true;
  while i < |rows|
    invariant 0 <= i <= |rows|
    decreases |rows| - i
  {
    if |rows[i]| != expected {
      allValid := false;
    }
    i := i + 1;
  }
  isValid := allValid;
}

datatype ValidationResult = Valid | Invalid(errors: seq<string>)
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
    public static void ValidateRows(Dafny.ISequence<Dafny.ISequence<Dafny.ISequence<Dafny.Rune>>> rows, out Dafny.ISequence<Dafny.ISequence<Dafny.ISequence<Dafny.Rune>>> outRows, out bool isValid)
    {
      outRows = Dafny.Sequence<Dafny.ISequence<Dafny.ISequence<Dafny.Rune>>>.Empty;
      isValid = false;
      outRows = rows;
      if ((new BigInteger((rows).Count)).Sign == 0) {
        isValid = true;
        return ;
      }
      BigInteger _0_expected;
      _0_expected = new BigInteger(((rows).Select(BigInteger.Zero)).Count);
      BigInteger _1_i;
      _1_i = BigInteger.One;
      bool _2_allValid;
      _2_allValid = true;
      while ((_1_i) < (new BigInteger((rows).Count))) {
        if ((new BigInteger(((rows).Select(_1_i)).Count)) != (_0_expected)) {
          _2_allValid = false;
        }
        _1_i = (_1_i) + (BigInteger.One);
      }
      isValid = _2_allValid;
    }
  }

  public interface _IValidationResult {
    bool is_Valid { get; }
    bool is_Invalid { get; }
    Dafny.ISequence<Dafny.ISequence<Dafny.Rune>> dtor_errors { get; }
    _IValidationResult DowncastClone();
  }
  public abstract class ValidationResult : _IValidationResult {
    public ValidationResult() {
    }
    private static readonly _IValidationResult theDefault = create_Valid();
    public static _IValidationResult Default() {
      return theDefault;
    }
    private static readonly Dafny.TypeDescriptor<_IValidationResult> _TYPE = new Dafny.TypeDescriptor<_IValidationResult>(ValidationResult.Default());
    public static Dafny.TypeDescriptor<_IValidationResult> _TypeDescriptor() {
      return _TYPE;
    }
    public static _IValidationResult create_Valid() {
      return new ValidationResult_Valid();
    }
    public static _IValidationResult create_Invalid(Dafny.ISequence<Dafny.ISequence<Dafny.Rune>> errors) {
      return new ValidationResult_Invalid(errors);
    }
    public bool is_Valid { get { return this is ValidationResult_Valid; } }
    public bool is_Invalid { get { return this is ValidationResult_Invalid; } }
    public Dafny.ISequence<Dafny.ISequence<Dafny.Rune>> dtor_errors {
      get {
        var d = this;
        return ((ValidationResult_Invalid)d)._errors;
      }
    }
    public abstract _IValidationResult DowncastClone();
  }
  public class ValidationResult_Valid : ValidationResult {
    public ValidationResult_Valid() : base() {
    }
    public override _IValidationResult DowncastClone() {
      if (this is _IValidationResult dt) { return dt; }
      return new ValidationResult_Valid();
    }
    public override bool Equals(object other) {
      var oth = other as ValidationResult_Valid;
      return oth != null;
    }
    public override int GetHashCode() {
      ulong hash = 5381;
      hash = ((hash << 5) + hash) + 0;
      return (int) hash;
    }
    public override string ToString() {
      string s = "ValidationResult.Valid";
      return s;
    }
  }
  public class ValidationResult_Invalid : ValidationResult {
    public readonly Dafny.ISequence<Dafny.ISequence<Dafny.Rune>> _errors;
    public ValidationResult_Invalid(Dafny.ISequence<Dafny.ISequence<Dafny.Rune>> errors) : base() {
      this._errors = errors;
    }
    public override _IValidationResult DowncastClone() {
      if (this is _IValidationResult dt) { return dt; }
      return new ValidationResult_Invalid(_errors);
    }
    public override bool Equals(object other) {
      var oth = other as ValidationResult_Invalid;
      return oth != null && object.Equals(this._errors, oth._errors);
    }
    public override int GetHashCode() {
      ulong hash = 5381;
      hash = ((hash << 5) + hash) + 1;
      hash = ((hash << 5) + hash) + ((ulong)Dafny.Helpers.GetHashCode(this._errors));
      return (int) hash;
    }
    public override string ToString() {
      string s = "ValidationResult.Invalid";
      s += "(";
      s += Dafny.Helpers.ToString(this._errors);
      s += ")";
      return s;
    }
  }
} // end of namespace _module
