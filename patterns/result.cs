// Dafny program result.dfy compiled into C#
// To recompile, you will need the libraries
//     System.Runtime.Numerics.dll System.Collections.Immutable.dll
// but the 'dotnet' tool in .NET should pick those up automatically.
// Optionally, you may want to include compiler switches like
//     /debug /nowarn:162,164,168,183,219,436,1717,1718

using System;
using System.Numerics;
using System.Collections;
[assembly: DafnyAssembly.DafnySourceAttribute(@"// dafny 4.11.0.0
// Command-line arguments: translate cs C:/Users/goldf/Posit/patterns/result.dfy --no-verify --allow-external-contracts --allow-warnings
// result.dfy

predicate IsSuccess<T>(r: Result<T>)
  decreases r
{
  r.Success?
}

predicate IsFailure<T>(r: Result<T>)
  decreases r
{
  r.Failure?
}

function UnwrapOr<T>(r: Result<T>, default: T): T
  ensures r.Success? ==> UnwrapOr(r, default) == r.value
  decreases r
{
  if r.Success? then
    r.value
  else
    default
}

function MapResult<T, U>(r: Result<T>, f: T -> U): Result<U>
  ensures r.Success? ==> MapResult(r, f).Success?
  ensures r.Success? ==> MapResult(r, f).value == f(r.value)
  ensures r.Failure? ==> MapResult(r, f).Failure?
  ensures r.Failure? ==> MapResult(r, f).error == r.error
  decreases r
{
  if r.Success? then
    Success(f(r.value))
  else
    Failure(r.error)
}

datatype Result<T> = Success(value: T) | Failure(error: string)
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
  public static Func<U1, U2, UResult> DowncastClone<T1, T2, TResult, U1, U2, UResult>(this Func<T1, T2, TResult> F, Func<U1, T1> ArgConv1, Func<U2, T2> ArgConv2, Func<TResult, UResult> ResConv) {
    return (arg1, arg2) => ResConv(F(ArgConv1(arg1), ArgConv2(arg2)));
  }
}
// end of class FuncExtensions
namespace _module {

  public partial class __default {
    public static bool IsSuccess<__T>(_IResult<__T> r) {
      return (r).is_Success;
    }
    public static bool IsFailure<__T>(_IResult<__T> r) {
      return (r).is_Failure;
    }
    public static __T UnwrapOr<__T>(_IResult<__T> r, __T @default)
    {
      if ((r).is_Success) {
        return (r).dtor_value;
      } else {
        return @default;
      }
    }
    public static _IResult<__U> MapResult<__T, __U>(_IResult<__T> r, Func<__T, __U> f)
    {
      if ((r).is_Success) {
        return _module.Result<__U>.create_Success(Dafny.Helpers.Id<Func<__T, __U>>(f)((r).dtor_value));
      } else {
        return _module.Result<__U>.create_Failure((r).dtor_error);
      }
    }
  }

  public interface _IResult<T> {
    bool is_Success { get; }
    bool is_Failure { get; }
    T dtor_value { get; }
    Dafny.ISequence<Dafny.Rune> dtor_error { get; }
    _IResult<__T> DowncastClone<__T>(Func<T, __T> converter0);
  }
  public abstract class Result<T> : _IResult<T> {
    public Result() {
    }
    public static _IResult<T> Default() {
      return create_Failure(Dafny.Sequence<Dafny.Rune>.Empty);
    }
    public static Dafny.TypeDescriptor<_IResult<T>> _TypeDescriptor() {
      return new Dafny.TypeDescriptor<_IResult<T>>(Result<T>.Default());
    }
    public static _IResult<T> create_Success(T @value) {
      return new Result_Success<T>(@value);
    }
    public static _IResult<T> create_Failure(Dafny.ISequence<Dafny.Rune> error) {
      return new Result_Failure<T>(error);
    }
    public bool is_Success { get { return this is Result_Success<T>; } }
    public bool is_Failure { get { return this is Result_Failure<T>; } }
    public T dtor_value {
      get {
        var d = this;
        return ((Result_Success<T>)d)._value;
      }
    }
    public Dafny.ISequence<Dafny.Rune> dtor_error {
      get {
        var d = this;
        return ((Result_Failure<T>)d)._error;
      }
    }
    public abstract _IResult<__T> DowncastClone<__T>(Func<T, __T> converter0);
  }
  public class Result_Success<T> : Result<T> {
    public readonly T _value;
    public Result_Success(T @value) : base() {
      this._value = @value;
    }
    public override _IResult<__T> DowncastClone<__T>(Func<T, __T> converter0) {
      if (this is _IResult<__T> dt) { return dt; }
      return new Result_Success<__T>(converter0(_value));
    }
    public override bool Equals(object other) {
      var oth = other as Result_Success<T>;
      return oth != null && object.Equals(this._value, oth._value);
    }
    public override int GetHashCode() {
      ulong hash = 5381;
      hash = ((hash << 5) + hash) + 0;
      hash = ((hash << 5) + hash) + ((ulong)Dafny.Helpers.GetHashCode(this._value));
      return (int) hash;
    }
    public override string ToString() {
      string s = "Result.Success";
      s += "(";
      s += Dafny.Helpers.ToString(this._value);
      s += ")";
      return s;
    }
  }
  public class Result_Failure<T> : Result<T> {
    public readonly Dafny.ISequence<Dafny.Rune> _error;
    public Result_Failure(Dafny.ISequence<Dafny.Rune> error) : base() {
      this._error = error;
    }
    public override _IResult<__T> DowncastClone<__T>(Func<T, __T> converter0) {
      if (this is _IResult<__T> dt) { return dt; }
      return new Result_Failure<__T>(_error);
    }
    public override bool Equals(object other) {
      var oth = other as Result_Failure<T>;
      return oth != null && object.Equals(this._error, oth._error);
    }
    public override int GetHashCode() {
      ulong hash = 5381;
      hash = ((hash << 5) + hash) + 1;
      hash = ((hash << 5) + hash) + ((ulong)Dafny.Helpers.GetHashCode(this._error));
      return (int) hash;
    }
    public override string ToString() {
      string s = "Result.Failure";
      s += "(";
      s += this._error.ToVerbatimString(true);
      s += ")";
      return s;
    }
  }
} // end of namespace _module
