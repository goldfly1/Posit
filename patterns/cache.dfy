// Pattern: Cache (Approach 3 — pre-written body with parameters)
// responsibility: Store, lookup, and invalidate key-value entries
// test: Lookup(Store(map[], 1, "hello"), 1) returns Success("hello")
// test: Lookup(map[], 1) returns Failure("not found")
// test: Contains(Store(map[], 1, "hello"), 1) returns true
// test: Contains(Invalidate(Store(map[], 1, "hello"), 1), 1) returns false
//
// Parameters:
//   maxSize: int — maximum number of entries (0 = unlimited)
//   evictionPolicy: string — "lru" or "fifo" (affects eviction order)

include "result.dfy"

// Store a key-value pair (insert or update)
method Store(cache: map<int, string>, key: int, value: string) returns (newCache: map<int, string>)
  ensures key in newCache
  ensures newCache[key] == value
  ensures forall k :: k in cache ==> k in newCache
{
  newCache := cache[key := value];
}

// Look up a key; return Success(value) or Failure("not found")
method Lookup(cache: map<int, string>, key: int) returns (result: Result<string>)
  ensures result.Success? ==> key in cache && result.value == cache[key]
  ensures result.Failure? ==> key !in cache
{
  if key in cache {
    result := Success(cache[key]);
  } else {
    result := Failure("not found");
  }
}

// Remove a key from the cache (no-op if key not present)
method Invalidate(cache: map<int, string>, key: int) returns (newCache: map<int, string>)
  ensures key !in newCache
  ensures forall k :: k in cache && k != key ==> k in newCache
  ensures forall k :: k in newCache ==> k in cache && k != key
{
  newCache := map k | k in cache && k != key :: cache[k];
}

// Remove all entries (return empty cache)
method InvalidateAll(cache: map<int, string>) returns (newCache: map<int, string>)
  ensures |newCache| == 0
  ensures forall k :: k !in newCache
{
  newCache := map[];
}

// Check if a key exists in the cache
function Contains(cache: map<int, string>, key: int): bool
{
  key in cache
}

// Count the number of entries in the cache
function Size(cache: map<int, string>): int
{
  |cache|
}