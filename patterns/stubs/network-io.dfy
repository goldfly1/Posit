// Stubs: Network I/O portals
// Pre-cut {:extern} declarations for HTTP and network operations.
// Attach to patterns that make web requests or API calls.

// HTTP GET request
method {:extern} HttpGet(url: string) returns (response: string)
  requires |url| > 0
  ensures |response| >= 0

// HTTP POST request
method {:extern} HttpPost(url: string, body: string) returns (response: string)
  requires |url| > 0
  ensures |response| >= 0

// HTTP PUT request
method {:extern} HttpPut(url: string, body: string) returns (response: string)
  requires |url| > 0
  ensures |response| >= 0

// HTTP DELETE request
method {:extern} HttpDelete(url: string) returns (response: string)
  requires |url| > 0
  ensures |response| >= 0

// Open a TCP connection
method {:extern} OpenSocket(host: string, port: int) returns (socketId: int)
  requires |host| > 0
  requires port > 0
  ensures socketId >= 0

// Send data over socket
method {:extern} SocketSend(socketId: int, data: string)
  requires socketId >= 0

// Receive data from socket
method {:extern} SocketRecv(socketId: int) returns (data: string)
  requires socketId >= 0
  ensures |data| >= 0

// Close socket
method {:extern} CloseSocket(socketId: int)
  requires socketId >= 0