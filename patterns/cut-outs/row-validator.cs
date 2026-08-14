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
// Command-line arguments: translate cs C:\Users\goldf\Posit\patterns\cut-outs\row-validator.dfy --no-verify --allow-external-contracts --allow-warnings --test-assumptions Externs --translate-standard-library false
// row-validator.dfy

method ValidateRows(rows: seq<seq<string>>) returns (result: ValidationResult)
  requires |rows| >= 0
  decreases |rows|
{
  var errors := [];
  if |rows| == 0 {
    result := Valid;
    return;
  }
  var expected := |rows[0]|;
  var i := 1;
  while i < |rows|
    invariant 0 <= i <= |rows|
    invariant |errors| >= 0
    decreases |rows| - i
  {
    if |rows[i]| != expected {
      errors := errors + [""field count mismatch""];
    }
    i := i + 1;
  }
  if |errors| == 0 {
    result := Valid;
  } else {
    result := Invalid(errors);
  }
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
    public static _IValidationResult ValidateRows(Dafny.ISequence<Dafny.ISequence<Dafny.ISequence<Dafny.Rune>>> rows)
    {
      _IValidationResult result = ValidationResult.Default();
      Dafny.ISequence<Dafny.ISequence<Dafny.Rune>> _0_errors;
      _0_errors = Dafny.Sequence<Dafny.ISequence<Dafny.Rune>>.FromElements();
      if ((new BigInteger((rows).Count)).Sign == 0) {
        result = _module.ValidationResult.create_Valid();
        return result;
      }
      BigInteger _1_expected;
      _1_expected = new BigInteger(((rows).Select(BigInteger.Zero)).Count);
      BigInteger _2_i;
      _2_i = BigInteger.One;
      while ((_2_i) < (new BigInteger((rows).Count))) {
        if ((new BigInteger(((rows).Select(_2_i)).Count)) != (_1_expected)) {
          _0_errors = Dafny.Sequence<Dafny.ISequence<Dafny.Rune>>.Concat(_0_errors, Dafny.Sequence<Dafny.ISequence<Dafny.Rune>>.FromElements(Dafny.Sequence<Dafny.Rune>.UnicodeFromString("field count mismatch")));
        }
        _2_i = (_2_i) + (BigInteger.One);
      }
      if ((new BigInteger((_0_errors).Count)).Sign == 0) {
        result = _module.ValidationResult.create_Valid();
      } else {
        result = _module.ValidationResult.create_Invalid(_0_errors);
      }
      return result;
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
