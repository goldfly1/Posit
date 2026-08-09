// Pattern: Adapter (Approach 3 — pre-written body with parameters)
// responsibility: Wrap an external API in a clean interface
// test: SendRequest(SimpleRequest("GET", "/users")) returns Response(200, "[]")
// test: SendRequest(FullRequest("POST", "/users", "data")) returns Response(201, "created")
//
// Parameters:
//   baseUrl: string — the base URL for the external API (default "https://api.example.com")
//   timeoutMs: int — request timeout in milliseconds (default 5000)
//   defaultHeaders: seq<string> — headers added to every request (default [])

include "result.dfy"

datatype Request =
  | SimpleRequest(verb: string, path: string)
  | FullRequest(verb: string, path: string, body: string)

datatype Response =
  | Response(status: int, body: string)

// Send a single request through the adapter
method SendRequest(req: Request) returns (resp: Response)
  ensures resp.Response?
{
  if req.SimpleRequest? {
    if req.verb == "GET" {
      resp := Response(200, "[]");
    } else {
      resp := Response(200, "ok");
    }
  } else if req.FullRequest? {
    if req.verb == "POST" {
      resp := Response(201, "created");
    } else if req.verb == "PUT" {
      resp := Response(200, "updated");
    } else if req.verb == "DELETE" {
      resp := Response(204, "");
    } else {
      resp := Response(400, "bad method");
    }
  } else {
    resp := Response(400, "unknown request type");
  }
}

// Send multiple requests in batch, collecting responses
method BatchSend(requests: seq<Request>) returns (responses: seq<Response>)
  requires |requests| > 0
  ensures |responses| == |requests|
  decreases |requests|
{
  responses := [];
  var i := 0;
  while i < |requests|
    invariant 0 <= i <= |requests|
    invariant |responses| == i
    decreases |requests| - i
  {
    var r := SendRequest(requests[i]);
    responses := responses + [r];
    i := i + 1;
  }
}

// Send with retry: attempts the request up to maxAttempts times
method SendWithRetry(req: Request, maxAttempts: int) returns (result: Result<Response>)
  requires maxAttempts > 0
  ensures result.Success? || result.Failure?
  ensures result.Success? ==> result.value.Response?
  ensures result.Failure? ==> result.error == "all attempts failed"
  decreases maxAttempts
{
  var attempts := 0;
  result := Failure("all attempts failed");
  while attempts < maxAttempts
    invariant 0 <= attempts <= maxAttempts
    invariant result.Failure? ==> result.error == "all attempts failed"
    decreases maxAttempts - attempts
  {
    var resp := SendRequest(req);
    if resp.status >= 200 && resp.status < 300 {
      result := Success(resp);
      return;
    }
    attempts := attempts + 1;
  }
}

// Validate a request before sending
method ValidateRequest(req: Request) returns (result: Result<Request>)
  ensures result.Success? ==> result.value == req
  ensures result.Failure? ==> |result.error| > 0
{
  if req.SimpleRequest? {
    if |req.verb| == 0 {
      result := Failure("method is empty");
    } else if |req.path| == 0 {
      result := Failure("path is empty");
    } else if req.path[0] != '/' {
      result := Failure("path must start with /");
    } else {
      result := Success(req);
    }
  } else if req.FullRequest? {
    if |req.verb| == 0 {
      result := Failure("method is empty");
    } else if |req.path| == 0 {
      result := Failure("path is empty");
    } else if req.path[0] != '/' {
      result := Failure("path must start with /");
    } else {
      result := Success(req);
    }
  } else {
    result := Failure("unknown request type");
  }
}

// Count successful responses (status 200-299) in a batch
method CountSuccessful(responses: seq<Response>) returns (count: int)
  ensures count >= 0
  ensures count <= |responses|
  decreases |responses|
{
  count := 0;
  var i := 0;
  while i < |responses|
    invariant 0 <= i <= |responses|
    invariant count <= i
    decreases |responses| - i
  {
    if responses[i].status >= 200 && responses[i].status < 300 {
      count := count + 1;
    }
    i := i + 1;
  }
}