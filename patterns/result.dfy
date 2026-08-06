// Pattern: Success/Failure Result
// The most common building block. Used by parser, validator, repository.
// Attach this to any module that can fail.

datatype Result<T> =
  | Success(value: T)
  | Failure(error: string)

// Predicate: success check
predicate IsSuccess<T>(r: Result<T>) {
  r.Success?
}

// Predicate: failure check
predicate IsFailure<T>(r: Result<T>) {
  r.Failure?
}

// Helper: unwrap value on success, default on failure
function UnwrapOr<T>(r: Result<T>, default: T): T
  ensures r.Success? ==> UnwrapOr(r, default) == r.value
{
  if r.Success? then r.value else default
}

// Helper: map over success
function MapResult<T, U>(r: Result<T>, f: T -> U): Result<U>
  ensures r.Success? ==> MapResult(r, f).Success?
  ensures r.Success? ==> MapResult(r, f).value == f(r.value)
  ensures r.Failure? ==> MapResult(r, f).Failure?
  ensures r.Failure? ==> MapResult(r, f).error == r.error
{
  if r.Success? then Success(f(r.value)) else Failure(r.error)
}