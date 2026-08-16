namespace Posit.Phases.EdgeCases;

/// <summary>
/// Edge case patterns covering HTTP API error handling and edge responses.
/// </summary>
public static class ApiErrorPatterns
{
    /// <summary>
    /// Gets the full set of API error edge case patterns.
    /// </summary>
    public static EdgeCasePattern[] All =>
    [
        new("ApiError", "400MalformedJson",
            "Request body is syntactically invalid JSON (e.g. missing brace, trailing comma).",
            "Send malformed JSON and verify a 400 Bad Request with a non-revealing error message is returned."),

        new("ApiError", "400MissingRequiredField",
            "Request body omits a required field defined by the contract.",
            "Send a payload missing a required field and verify a 400 with a field-specific validation message is returned."),

        new("ApiError", "400ExtraUnknownField",
            "Request body includes a field not present in the contract.",
            "Send an extra field and verify a 400 (strict) or graceful ignore (lenient) per the documented policy."),

        new("ApiError", "400WrongFieldType",
            "Request body provides a field with the wrong type (e.g. string where int is expected).",
            "Send a wrong-typed field and verify a 400 with a type-mismatch message is returned, not a 500."),

        new("ApiError", "401MissingToken",
            "Request omits the authentication token entirely.",
            "Send no Authorization header and verify a 401 Unauthorized with a WWW-Authenticate challenge is returned."),

        new("ApiError", "401ExpiredToken",
            "Request presents a validly-signed but expired authentication token.",
            "Send an expired token and verify a 401 with a token-expired error code is returned."),

        new("ApiError", "403InsufficientPermissions",
            "Authenticated user lacks the role or scope required for the resource.",
            "Authenticate as a low-privilege user and verify a 403 Forbidden (not 401) is returned."),

        new("ApiError", "404NonexistentResource",
            "Request targets a resource ID that has never existed.",
            "Request a random GUID and verify a 404 Not Found is returned without leaking existence information."),

        new("ApiError", "404DeletedResource",
            "Request targets a resource ID that existed but was deleted (soft or hard).",
            "Request a deleted resource and verify a 404 is returned consistently with the deletion policy."),

        new("ApiError", "404WrongIdFormat",
            "Request provides an ID that does not match the expected format (e.g. non-numeric, non-GUID).",
            "Send a malformed ID and verify a 404 (or 400) is returned, not a 500 from a parse exception."),

        new("ApiError", "409ConcurrentModification",
            "Two clients modify the same resource concurrently; the second update conflicts.",
            "Issue two updates with the same ETag/version and verify the second receives a 409 Conflict."),

        new("ApiError", "409DuplicateCreation",
            "A create request duplicates a unique constraint (e.g. already-existing username).",
            "Create the same resource twice and verify the second receives a 409 Conflict, not a 500."),

        new("ApiError", "422ValidationFailure",
            "Semantic validation fails (e.g. end date before start date) with detailed field messages.",
            "Send a structurally valid but semantically invalid payload and verify a 422 with per-field error details."),

        new("ApiError", "429RateLimitHit",
            "Request exceeds the rate limit, triggering throttling.",
            "Burst requests past the limit and verify a 429 with a Retry-After header is returned."),

        new("ApiError", "500UnhandledException",
            "An unexpected exception escapes the handler, producing an internal error.",
            "Trigger an unhandled exception and verify a 500 with a generic message (no stack trace) is returned."),

        new("ApiError", "500DatabaseDisconnect",
            "The database connection is lost mid-request.",
            "Drop the database connection and verify a 500 (or 503) is returned and the system recovers on reconnect."),

        new("ApiError", "500UpstreamTimeout",
            "A downstream or database call exceeds its timeout, surfacing as an internal error.",
            "Induce a downstream timeout and verify a 500 or 504 is returned with a bounded client-facing latency."),

        new("ApiError", "502BadGateway",
            "An upstream proxy receives an invalid response from the backing service.",
            "Return an invalid upstream response and verify a 502 Bad Gateway is returned to the client."),

        new("ApiError", "503ServiceUnavailable",
            "The service is temporarily unavailable (e.g. starting up or overloaded).",
            "Request during startup/overload and verify a 503 with a Retry-After header is returned."),

        new("ApiError", "LargePayload10MB",
            "Request body is 10MB+, potentially exceeding size limits or memory.",
            "Send a 10MB+ payload and verify a 413 (or configured limit) is returned without OOM."),

        new("ApiError", "SlowClientTimeoutDuringUpload",
            "Client uploads very slowly, holding a connection until the server-side read timeout fires.",
            "Upload at 1 byte/sec and verify the server closes the connection on read timeout and releases resources."),
    ];
}