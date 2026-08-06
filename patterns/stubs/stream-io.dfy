// Stubs: Stream I/O portals
// Pre-cut {:extern} declarations for stream operations.
// Attach to patterns that process data incrementally.

// Open a stream for reading
method {:extern} OpenStream(path: string) returns (streamId: int)
  requires |path| > 0
  ensures streamId >= 0

// Read next chunk from stream
method {:extern} ReadChunk(streamId: int, maxBytes: int) returns (chunk: string)
  requires streamId >= 0
  requires maxBytes > 0
  ensures |chunk| >= 0

// Close a stream
method {:extern} CloseStream(streamId: int)
  requires streamId >= 0

// Check if stream has more data
method {:extern} HasMore(streamId: int) returns (hasMore: bool)
  requires streamId >= 0