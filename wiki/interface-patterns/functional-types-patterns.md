# Interface Pattern: Functional Types (Result, Maybe) — Railway-Oriented Validation

## Problem Shape
Validate input → branch on success/failure → execute on success or report error.
The architect produces `Validate(bool)→Merge(string)` which breaks the type chain
because `bool` cannot chain into `string`. The `Result<T>` pattern returns DATA from
validation so the chain continues. This pattern is extracted from
[CsharpFunctionalExtensions](https://github.com/vkhorikov/CsharpFunctionalExtensions)
by Vladimir Khorikov.

## Spec Verbs
validate, check, ensure, combine, bind, map, match, try

## Source
Repository: `https://github.com/vkhorikov/CsharpFunctionalExtensions`
Cloned to: `C:/Users/goldf/CsharpFunctionalExtensions/`

---

## Core Type Hierarchy

### Interface Layer (`IResult.cs`)

```csharp
public interface IResult
{
    bool IsFailure { get; }
    bool IsSuccess { get; }
}

public interface IValue<out T>
{
    T Value { get; }
}

public interface IError<out E>
{
    E Error { get; }
}

public interface IUnitResult<out E> : IResult, IError<E>
{
}

public interface IResult<out T, out E> : IValue<T>, IUnitResult<E>
{
}

public interface IResult<out T> : IResult<T, string>
{
}

public interface IMaybe<out T>
{
    T Value { get; }
    bool HasValue { get; }
    bool HasNoValue { get; }
}
```

**Key insight**: `IResult<T>` = `IResult<T, string>` — the default error type is `string`.
`IUnitResult<E>` carries an error but no value (for void operations that can fail).

### Concrete Types

```csharp
// No value, string error — void operation that can fail
public readonly struct Result : IResult, IError<string>
{
    public bool IsFailure { get; }
    public bool IsSuccess => !IsFailure;
    public string Error { get; }   // throws if accessed on success
}

// Value T, string error — operation returning T that can fail
public readonly struct Result<T> : IResult<T>, ISerializable
{
    public bool IsFailure { get; }
    public bool IsSuccess => !IsFailure;
    public string Error { get; }   // throws if accessed on success
    public T Value { get; }        // throws if accessed on failure

    public T GetValueOrDefault(T defaultValue = default);

    // Implicit: T → Result<T> (auto-wraps as success)
    public static implicit operator Result<T>(T value);
    // Implicit: Result<T> → Result (discards value, keeps error)
    public static implicit operator Result(Result<T> result);
    // Implicit: Result<T> → UnitResult<string> (discards value, keeps error)
    public static implicit operator UnitResult<string>(Result<T> result);
}

// Value T, custom error E — full generality
public readonly struct Result<T, E> : IResult<T, E>, ISerializable
{
    public bool IsFailure { get; }
    public bool IsSuccess => !IsFailure;
    public E Error { get; }
    public T Value { get; }

    public T GetValueOrDefault(T defaultValue = default);

    // Implicit: T → Result<T, E> (success)
    public static implicit operator Result<T, E>(T value);
    // Implicit: E → Result<T, E> (failure)
    public static implicit operator Result<T, E>(E error);
}

// No value, custom error E — void operation with typed error
public readonly struct UnitResult<E> : IUnitResult<E>, ISerializable
{
    public bool IsFailure { get; }
    public bool IsSuccess => !IsFailure;
    public E Error { get; }

    // Implicit: E → UnitResult<E> (failure)
    public static implicit operator UnitResult<E>(E error);
}

// Maybe<T> — optional value, no error channel
public readonly struct Maybe<T> : IEquatable<Maybe<T>>, IMaybe<T>
{
    public bool HasValue { get; }
    public bool HasNoValue => !HasValue;
    public T Value { get; }              // throws if HasNoValue
    public T GetValueOrThrow(string? errorMessage = null);
    public T GetValueOrDefault(T defaultValue);
    public bool TryGetValue(out T? value);

    public static Maybe<T> None { get; }
    public static Maybe<T> From(T? value);

    // Implicit: T → Maybe<T>
    public static implicit operator Maybe<T>(T? value);
}
```

### ICombine Interface (`ICombine.cs`)

```csharp
public interface ICombine
{
    ICombine Combine(ICombine value);
}
```

Used by `Result.Combine<E>` where `E : ICombine` — errors accumulate via `Combine`.

---

## Factory Methods (`Success.cs`, `Failure.cs`, `Of.cs`, `SuccessIf.cs`, `FailureIf.cs`, `Try.cs`)

```csharp
// --- Success ---
public static Result Success();
public static Result<T> Success<T>(T value);
public static Result<T, E> Success<T, E>(T value);
public static UnitResult<E> Success<E>();

// --- Failure ---
public static Result Failure(string error);
public static Result<T> Failure<T>(string error);
public static Result<T, E> Failure<T, E>(E error);

// --- Of (alias for Success, value must be non-null) ---
public static Result<T> Of<T>(T value) where T : notnull;
public static Result<T> Of<T>(Func<T> func) where T : notnull;
public static Result<T, E> Of<T, E>(T value) where T : notnull;
public static Result<T, E> Of<T, E>(Func<T> func) where T : notnull;

// --- SuccessIf / FailureIf (bool → Result) ---
// These convert a bool predicate INTO a Result — the bridge from bool-land to Result-land
public static Result SuccessIf(bool isSuccess, string error);
public static Result SuccessIf(Func<bool> predicate, string error);
public static Result<T> SuccessIf<T>(bool isSuccess, in T value, string error);
public static Result<T, E> SuccessIf<T, E>(bool isSuccess, in T value, in E error);
public static UnitResult<E> SuccessIf<E>(bool isSuccess, in E error);

public static Result FailureIf(bool isFailure, string error);
public static Result FailureIf(Func<bool> failurePredicate, string error);
public static Result<T> FailureIf<T>(bool isFailure, T value, string error);
public static Result<T, E> FailureIf<T, E>(bool isFailure, in T value, in E error);

// --- Try (exception → Result, no throw escapes) ---
public static Result Try(Action action, Func<Exception, string> errorHandler = null);
public static Result<T> Try<T>(Func<T> func, Func<Exception, string> errorHandler = null);
public static Result<T, E> Try<T, E>(Func<T> func, Func<Exception, E> errorHandler);
public static UnitResult<E> Try<E>(Action action, Func<Exception, E> errorHandler);
```

**Critical pattern**: `SuccessIf` is the entry point that converts a `bool` check into a
`Result<T>`. This is how you bridge from predicate-based validation into the Result chain
WITHOUT returning `bool` from the validator itself.

---

## Pattern 1: Railway-Oriented Bind (the core chaining primitive)

`Bind` is the monadic bind — it chains `Result<T>` → `Func<T, Result<K>>` → `Result<K>`.
If the source is a failure, the function is SKIPPED and the error propagates.

```csharp
// Result<T, E> → Func<T, Result<K, E>> → Result<K, E>
public static Result<K, E> Bind<T, K, E>(this Result<T, E> result, Func<T, Result<K, E>> func)
{
    if (result.IsFailure)
        return Result.Failure<K, E>(result.Error);  // short-circuit, skip func
    return func(result.Value);                       // continue chain
}

// Result<T> → Func<T, Result<K>> → Result<K>
public static Result<K> Bind<T, K>(this Result<T> result, Func<T, Result<K>> func)
{
    if (result.IsFailure)
        return Result.Failure<K>(result.Error);
    return func(result.Value);
}

// Result → Func<Result<K>> → Result<K>
public static Result<K> Bind<K>(this Result result, Func<Result<K>> func);

// Result<T> → Func<T, Result> → Result
public static Result Bind<T>(this Result<T> result, Func<T, Result> func);

// UnitResult<E> → Func<UnitResult<E>> → UnitResult<E>
public static UnitResult<E> Bind<E>(this UnitResult<E> result, Func<UnitResult<E>> func);

// UnitResult<E> → Func<Result<T, E>> → Result<T, E>
public static Result<T, E> Bind<T, E>(this UnitResult<E> result, Func<Result<T, E>> func);

// Result<T, E> → Func<T, UnitResult<E>> → UnitResult<E>
public static UnitResult<E> Bind<T, E>(this Result<T, E> result, Func<T, UnitResult<E>> func);
```

**Type chain**: `Result<T, E> → Bind → Result<K, E> → Bind → Result<L, E> → ...`
The error type `E` is preserved across every `Bind`. The value type changes at each step.
The chain NEVER breaks because every link returns `Result<_, E>`.

### Maybe Bind (monadic bind for optional values)

```csharp
// Maybe<T> → Func<T, Maybe<K>> → Maybe<K>
public static Maybe<K> Bind<T, K>(in this Maybe<T> maybe, Func<T, Maybe<K>> selector)
{
    if (maybe.HasNoValue)
        return Maybe<K>.None;        // short-circuit
    return selector(maybe.GetValueOrThrow());
}
```

---

## Pattern 2: Map (functor — transform the value on success only)

`Map` transforms the success value without branching. On failure, error propagates.

```csharp
// Result<T, E> → Func<T, K> → Result<K, E>
public static Result<K, E> Map<T, K, E>(this Result<T, E> result, Func<T, K> func)
{
    if (result.IsFailure)
        return Result.Failure<K, E>(result.Error);
    return Result.Success<K, E>(func(result.Value));
}

// Result<T> → Func<T, K> → Result<K>
public static Result<K> Map<T, K>(this Result<T> result, Func<T, K> func);

// Result → Func<K> → Result<K>
public static Result<K> Map<K>(this Result result, Func<K> func);

// UnitResult<E> → Func<K> → Result<K, E>
public static Result<K, E> Map<K, E>(this UnitResult<E> result, Func<K> func);

// Maybe<T> → Func<T, K> → Maybe<K>
public static Maybe<K> Map<T, K>(in this Maybe<T> maybe, Func<T, K> selector)
{
    if (maybe.HasNoValue)
        return Maybe<K>.None;
    return selector(maybe.GetValueOrThrow());
}
```

**Type chain**: `Result<T> → Map → Result<K> → Map → Result<L> → ...`
Each `Map` changes the value type. The chain stays in `Result<_>` space.

### MapError (transform the error, keep the value)

```csharp
// Result<T, E> → Func<E, E2> → Result<T, E2>
public static Result<T, E2> MapError<T, E, E2>(this Result<T, E> result, Func<E, E2> errorFactory)
{
    if (result.IsFailure)
        return Result.Failure<T, E2>(errorFactory(result.Error));
    return Result.Success<T, E2>(result.Value);
}

// Result<T, E> → Func<E, string> → Result<T>
public static Result<T> MapError<T, E>(this Result<T, E> result, Func<E, string> errorFactory);
```

---

## Pattern 3: Ensure (validation that stays in the chain)

`Ensure` is the validation primitive. It checks a predicate on the value and returns
a failure if the predicate is false — BUT it returns `Result<T>`, not `bool`.

```csharp
// Validate a predicate on the value, return failure if false
public static Result<T> Ensure<T>(this Result<T> result, Func<T, bool> predicate, string errorMessage)
{
    if (result.IsFailure)
        return result;                                    // already failed, propagate
    if (!predicate(result.Value))
        return Result.Failure<T>(errorMessage);           // validation failed
    return result;                                        // validation passed
}

// With error factory (lazy error message)
public static Result<T> Ensure<T>(this Result<T> result, Func<T, bool> predicate, Func<T, string> errorPredicate);

// Custom error type
public static Result<T, E> Ensure<T, E>(this Result<T, E> result, Func<T, bool> predicate, E error);
public static Result<T, E> Ensure<T, E>(this Result<T, E> result, Func<T, bool> predicate, Func<T, E> errorPredicate);

// Ensure with Result-returning predicate (nested validation)
public static Result<T> Ensure<T>(this Result<T> result, Func<Result> predicate);
public static Result<T> Ensure<T>(this Result<T> result, Func<Result<T>> predicate);
public static Result<T> Ensure<T>(this Result<T> result, Func<T, Result> predicate);
public static Result<T> Ensure<T>(this Result<T> result, Func<T, Result<T>> predicate);

// UnitResult variants
public static UnitResult<E> Ensure<E>(this UnitResult<E> result, Func<bool> predicate, E error);
public static UnitResult<E> Ensure<E>(this UnitResult<E> result, Func<bool> predicate, Func<E> errorPredicate);
public static UnitResult<E> Ensure<E>(this UnitResult<E> result, Func<UnitResult<E>> predicate);
```

**Why this matters for Posit**: `Ensure` returns `Result<T>`, not `bool`. The chain
`Ensure → Ensure → Bind → Map` type-checks at every step. The architect's
`Validate(bool)→Merge(string)` pattern breaks because `bool` is a dead end.

### EnsureNotNull

```csharp
public static Result<T> EnsureNotNull<T>(this Result<T?> result, string error) where T : class;
public static Result<T> EnsureNotNull<T>(this Result<T?> result, Func<string> errorFactory) where T : class;
public static Result<T, E> EnsureNotNull<T, E>(this Result<T?, E> result, E error) where T : class;
```

---

## Pattern 4: Check (side-effect validation that preserves the value)

`Check` runs a validation function but returns the ORIGINAL result's value, not the
checker's value. This is `Bind` + `Map` back to the original value.

```csharp
// Run a check, but keep the original value
public static Result<T> Check<T>(this Result<T> result, Func<T, Result> func)
{
    return result.Bind(func).Map(() => result.Value);  // bind → if ok, restore original value
}

public static Result<T> Check<T, K>(this Result<T> result, Func<T, Result<K>> func);
public static Result<T, E> Check<T, K, E>(this Result<T, E> result, Func<T, Result<K, E>> func);
public static Result<T, E> Check<T, E>(this Result<T, E> result, Func<T, UnitResult<E>> func);
public static UnitResult<E> Check<E>(this UnitResult<E> result, Func<UnitResult<E>> func);
```

**Semantics**: `Check` = "validate this sub-condition, but I still want my original T
to flow forward." The checker returns `Result` (not `bool`), and if it fails, the
failure propagates. If it succeeds, the original `T` continues.

### CheckIf (conditional check)

```csharp
public static Result<T> CheckIf<T>(this Result<T> result, bool condition, Func<T, Result> func);
public static Result<T> CheckIf<T>(this Result<T> result, Func<T, bool> predicate, Func<T, Result> func);
public static Result<T, E> CheckIf<T, K, E>(this Result<T, E> result, bool condition, Func<T, Result<K, E>> func);
public static UnitResult<E> CheckIf<E>(this UnitResult<E> result, bool condition, Func<UnitResult<E>> func);
```

---

## Pattern 5: Combine (aggregate multiple validations)

`Combine` takes multiple `Result` values and returns a single `Result`. If any fail,
the combined result is a failure with all errors concatenated.

```csharp
// Combine multiple Result (string errors, separator-joined)
public static Result Combine(IEnumerable<Result> results, string errorMessagesSeparator = null);
public static Result Combine(params Result[] results);
// → Success if all succeed; Failure with joined error messages if any fail

// Combine multiple Result<T> (discards values, checks success/failure)
public static Result Combine<T>(IEnumerable<Result<T>> results, string errorMessagesSeparator = null);
public static Result Combine<T>(params Result<T>[] results);

// Combine with custom error type (E : ICombine — errors accumulate via ICombine.Combine)
public static UnitResult<E> Combine<E>(IEnumerable<UnitResult<E>> results) where E : ICombine;
public static UnitResult<E> Combine<E>(params UnitResult<E>[] results) where E : ICombine;

// Combine with custom error composer (E need not implement ICombine)
public static UnitResult<E> Combine<E>(IEnumerable<UnitResult<E>> results, Func<IEnumerable<E>, E> composerError);
public static Result<bool, E> Combine<T, E>(IEnumerable<Result<T, E>> results, Func<IEnumerable<E>, E> composerError);

// Combine Result<T, E> and collect values on success
public static Result<IEnumerable<T>, E> Combine<T, E>(this IEnumerable<Result<T, E>> results) where E : ICombine;
public static Result<IEnumerable<T>, E> Combine<T, E>(this IEnumerable<Result<T, E>> results, Func<IEnumerable<E>, E> composerError);

// Combine Result<T> and collect values on success
public static Result<IEnumerable<T>> Combine<T>(this IEnumerable<Result<T>> results, string errorMessageSeparator = null);

// Combine with composer (transform collected values into a new type K)
public static Result<K, E> Combine<T, K, E>(this IEnumerable<Result<T, E>> results, Func<IEnumerable<T>, K> composer) where E : ICombine;
public static Result<K, E> Combine<T, K, E>(this IEnumerable<Result<T, E>> results, Func<IEnumerable<T>, K> composer, Func<IEnumerable<E>, E> composerError);
public static Result<K> Combine<T, K>(this IEnumerable<Result<T>> results, Func<IEnumerable<T>, K> composer, string errorMessageSeparator = null);
```

### FirstFailureOrSuccess (short-circuit on first failure)

```csharp
public static Result FirstFailureOrSuccess(params Result[] results)
{
    foreach (Result result in results)
    {
        if (result.IsFailure)
            return result;     // first failure stops the chain
    }
    return Success();           // all succeeded
}
```

**Type chain for Combine**: `Result<T>[] → Combine → Result<IEnumerable<T>> → Map → Result<K>`
Multiple results are aggregated into one, and the chain continues.

---

## Pattern 6: Match (the exit gate — Result → value)

`Match` is the terminal operation. It converts a `Result<T>` back into a plain value
by providing handlers for both branches. This is where the railway branches converge.

```csharp
// Result<T, E> → (onSuccess: T→K, onFailure: E→K) → K
public static K Match<T, K, E>(this Result<T, E> result, Func<T, K> onSuccess, Func<E, K> onFailure)
{
    return result.IsSuccess
        ? onSuccess(result.Value)
        : onFailure(result.Error);
}

// Result<T> → (onSuccess: T→K, onFailure: string→K) → K
public static K Match<K, T>(this Result<T> result, Func<T, K> onSuccess, Func<string, K> onFailure);

// Result → (onSuccess: T→T, onFailure: string→T) → T
public static T Match<T>(this Result result, Func<T> onSuccess, Func<string, T> onFailure);

// UnitResult<E> → (onSuccess: K, onFailure: E→K) → K
public static K Match<K, E>(this UnitResult<E> result, Func<K> onSuccess, Func<E, K> onFailure);

// Action variants (void return)
public static void Match<T, E>(this Result<T, E> result, Action<T> onSuccess, Action<E> onFailure);
public static void Match<T>(this Result<T> result, Action<T> onSuccess, Action<string> onFailure);
public static void Match(this Result result, Action onSuccess, Action<string> onFailure);
public static void Match<E>(this UnitResult<E> result, Action onSuccess, Action<E> onFailure);

// Maybe<T> → (Some: T→TE, None: TE) → TE
public static TE Match<TE, T>(in this Maybe<T> maybe, Func<T, TE> Some, Func<TE> None);
public static void Match<T>(in this Maybe<T> maybe, Action<T> Some, Action None);
```

---

## Pattern 7: Tap (side-effect on success, pass-through)

`Tap` executes a side-effect if the result is successful, then returns the original
result. The chain is unaffected.

```csharp
public static Result Tap(this Result result, Action action);
public static Result<T> Tap<T>(this Result<T> result, Action action);
public static Result<T> Tap<T>(this Result<T> result, Action<T> action);
public static Result<T, E> Tap<T, E>(this Result<T, E> result, Action action);
public static Result<T, E> Tap<T, E>(this Result<T, E> result, Action<T> action);
public static UnitResult<E> Tap<E>(this UnitResult<E> result, Action action);
```

---

## Pattern 8: Compensate (recover from failure)

`Compensate` catches a failure and provides an alternative `Result`. If the source
succeeded, it passes through unchanged.

```csharp
public static Result Compensate(this Result result, Func<string, Result> func);
public static Result<T> Compensate<T>(this Result<T> result, Func<string, Result<T>> func);
public static Result<T, E> Compensate<T, E>(this Result<T, E> result, Func<E, Result<T, E>> func);
public static UnitResult<E2> Compensate<E, E2>(this UnitResult<E> result, Func<E, UnitResult<E2>> func);
```

---

## Pattern 9: Finally (collapse Result to value regardless of state)

```csharp
public static T Finally<T>(this Result result, Func<Result, T> func);
public static K Finally<T, K>(this Result<T> result, Func<Result<T>, K> func);
public static K Finally<K, E>(this UnitResult<E> result, Func<UnitResult<E>, K> func);
public static K Finally<T, K, E>(this Result<T, E> result, Func<Result<T, E>, K> func);
```

---

## Pattern 10: Maybe → Result Conversion

```csharp
// Maybe<T> → string → Result<T>
public static Result<T> ToResult<T>(in this Maybe<T> maybe, string errorMessage)
{
    if (maybe.HasNoValue)
        return Result.Failure<T>(errorMessage);
    return Result.Success(maybe.GetValueOrThrow());
}

// Maybe<T> → E → Result<T, E>
public static Result<T, E> ToResult<T, E>(in this Maybe<T> maybe, E error);
public static Result<T, E> ToResult<T, E>(in this Maybe<T> maybe, Func<E> errorFunc);
```

---

## Pattern 11: BindZip (accumulate multiple values in the chain)

`BindZip` binds a new `Result<K>` and zips the value with the current value into a tuple.
This is how you accumulate multiple validated values without losing the chain.

```csharp
// Result<T> → Func<T, Result<K>> → Result<(T, K)>
public static Result<(T First, K Second)> BindZip<T, K>(
    this Result<T> result, Func<T, Result<K>> func);

// Result<(T1, T2)> → Func<T1, T2, Result<K>> → Result<(T1, T2, K)>
public static Result<(T1 First, T2 Second, K Third)> BindZip<T1, T2, K>(
    this Result<(T1, T2)> result, Func<T1, T2, Result<K>> func);

// Up to 7-tuple + K = 8-tuple
// Result<(T1, T2, T3, T4, T5, T6, T7)> → ... → Result<(T1, T2, T3, T4, T5, T6, T7, K)>
```

**Type chain**: `Result<T1> → BindZip → Result<(T1, T2)> → BindZip → Result<(T1, T2, T3)> → ...`
Each step adds a value to the tuple. If any step fails, the error propagates and all
subsequent steps are skipped.

---

## Pattern 12: ConvertFailure (change the value type on failure only)

```csharp
// Result<T> → Result<K> (only valid on failure; throws on success)
public Result<K> ConvertFailure<K>();
// Result<T, E> → Result<K, E> (only valid on failure; throws on success)
public Result<K, E> ConvertFailure<K>();
// Result → Result<K> (only valid on failure)
public Result<K> ConvertFailure<K>();
```

Used when you need to unify types for a later `Match`: convert the failure branch
to match the success branch's type.

---

## Complete Validation Chain Example (Railway-Oriented Programming)

```csharp
// The full pattern: validate → ensure → bind → map → match
// Every step returns Result<_>, never bool

Result<Order> result = ValidateInput(rawInput)           // Result<RawData>
    .Ensure(data => data.Fields.Count > 0, "Empty input") // Result<RawData>  (stays in chain)
    .Ensure(data => data.IsValid, "Invalid format")       // Result<RawData>  (stays in chain)
    .Bind(data => ParseOrder(data))                       // Result<Order>    (type changes)
    .Ensure(order => order.Total > 0, "Invalid total")    // Result<Order>    (stays in chain)
    .Bind(order => SaveOrder(order))                      // Result<OrderId>  (type changes)
    .Map(id => id.ToString());                            // Result<string>   (type changes)

// Exit gate: collapse Result<string> → string
string output = result.Match(
    onSuccess: idStr => $"Order saved: {idStr}",
    onFailure: error  => $"Error: {error}"
);
```

### Equivalent WRONG pattern (what the architect produces)

```csharp
// BAD: bool breaks the chain
bool isValid = Validate(data);       // bool — dead end, can't chain
string output = Merge(isValid);      // Merge expects string[][], gets bool — TYPE MISMATCH
```

### Why the Result pattern fixes this

1. `Validate` returns `Result<RawData>`, not `bool` — the validated data flows forward.
2. `Ensure` returns `Result<T>`, not `bool` — the chain stays in `Result<_>` space.
3. `Bind` changes the value type while preserving the error type — the chain type-checks.
4. `Map` transforms the value type — the chain stays in `Result<_>` space.
5. `Match` is the ONLY exit gate — it collapses `Result<T>` to a plain value at the end.
6. No step ever returns `bool` from a validation method. `bool` only appears inside
   `Ensure` predicates and `SuccessIf`/`FailureIf` constructors — it's consumed, not returned.

---

## Posit Application: Interface Signatures for Generated Code

For Posit's carapace (C# interfaces), the Result pattern translates to:

```csharp
// CORRECT: validator returns data wrapped in Result, chains into next step
interface IInputValidator {
    Result<string[][]> Validate(string[] lines);
    // returns Result<string[][]> — success carries validated data, failure carries error
    // chains into: validator.Validate(input).Bind(rows => serializer.Serialize(rows))
}

// CORRECT: transformer stays in Result space
interface IDataTransformer {
    Result<string> Transform(string[][] rows);
    // Result<string[][]> → Bind → Result<string> — chain type-checks
}

// CORRECT: error reporter is the exit gate (Match)
interface IResultReporter {
    string Report(Result<string> result);
    // collapses Result<string> → string via Match
}
```

### WRONG Posit pattern (causes TypeChainChecker failure)

```csharp
// BAD: bool return breaks the chain
interface IValidator {
    bool Validate(string[][] rows);  // bool can't chain into Merge<string[][]>
}
interface IMerger {
    string Merge(string[][] rows);   // expects string[][], but Validate produced bool
}
// Connection: Validate → Merge — TYPE MISMATCH, TypeChainChecker rejects
```

---

## Type Chain (CORRECT — Result-based)

```
string[] (input)
  → Result<string[][]>     (Validate — returns data + error)
  → Result<string[][]>     (Ensure — validates predicate, keeps data)
  → Result<string>         (Bind/Map — transforms data)
  → string                 (Match — exits Result space)
```

## Type Chain (WRONG — bool-based)

```
string[] (input)
  → bool                   (Validate — DEAD END, data lost)
  → string                 (Merge — expects string[][], gets bool — MISMATCH)
```

---

## Connection Order

```
ReadLines → Validate → Ensure → Bind(Transform) → Map(Serialize) → Match(Report) → PrintLine
```

Error path: any `Ensure`/`Bind` failure → error propagates through all subsequent steps
(they are skipped) → `Match` receives the error → `onFailure` branch → print error → exit 1.

## Key Constraints

1. **Validation methods must return `Result<T>`, not `bool`.** The `bool` is consumed
   inside `Ensure`/`SuccessIf` — it is never the return type of a validation method.
2. **The error type `E` is preserved across `Bind` and `Map`.** Changing the error type
   requires `MapError`. Changing the value type uses `Bind` or `Map`.
3. **`Match` is the only exit gate.** Never access `.Value` directly (it throws on
   failure). Use `Match` or `GetValueOrDefault` to collapse to a plain value.
4. **`Combine` aggregates multiple validations** — use it when N validators must all
   pass before the next step. Returns `Result<IEnumerable<T>>` with all values.
5. **`BindZip` accumulates multiple validated values** into a tuple without losing
   the error channel — use it when you need values from multiple previous steps.
6. **Native C# types only** (Posit constraint): use `Result<string>`, `Result<string[][]>`,
   `Result<int>`, etc. with `string` as the error type (matching `Result<T>` = `Result<T, string>`).