// Stubs: Time and Random portals
// Pre-cut {:extern} declarations for non-deterministic operations.
// Attach to patterns that need timestamps, delays, or randomness.

// Get current time as Unix timestamp (seconds since epoch)
method {:extern} GetCurrentTime() returns (timestamp: int)
  ensures timestamp > 0

// Get current time as ISO string
method {:extern} GetIsoTimestamp() returns (timestamp: string)
  ensures |timestamp| > 0

// Sleep for N milliseconds
method {:extern} Sleep(milliseconds: int)
  requires milliseconds >= 0

// Get a random integer in range [min, max)
method {:extern} RandomInt(min: int, max: int) returns (value: int)
  requires min < max
  ensures min <= value < max

// Get a random boolean
method {:extern} RandomBool() returns (value: bool)
  ensures true