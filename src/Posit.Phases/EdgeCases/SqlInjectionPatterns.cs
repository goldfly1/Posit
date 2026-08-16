namespace Posit.Phases.EdgeCases;

/// <summary>
/// Edge case patterns covering SQL injection attack vectors.
/// </summary>
public static class SqlInjectionPatterns
{
    private const string Guidance =
        "Insert this payload via the input boundary and verify the output does not contain raw SQL or unhandled exceptions.";

    /// <summary>
    /// Gets the full set of SQL injection edge case patterns.
    /// </summary>
    public static EdgeCasePattern[] All =>
    [
        new("SqlInjection", "ClassicOrOneEqualsOne",
            "Classic tautology: ' OR '1'='1 attempts to bypass authentication or WHERE clauses.",
            Guidance),

        new("SqlInjection", "DropTableComment",
            "Statement termination with comment: '; DROP TABLE Users-- attempts to execute destructive stacked queries.",
            Guidance),

        new("SqlInjection", "UnionSelect",
            "UNION SELECT payload: ' UNION SELECT username, password FROM users-- attempts to exfiltrate data.",
            Guidance),

        new("SqlInjection", "StackedQueries",
            "Stacked queries: ; INSERT INTO admins VALUES('hacker','pwned')-- chains an additional statement.",
            Guidance),

        new("SqlInjection", "BlindBooleanBased",
            "Boolean-based blind: ' AND 1=1-- vs ' AND 1=2-- infers data through response differences.",
            Guidance),

        new("SqlInjection", "BlindTimeBased",
            "Time-based blind: '; IF(condition) WAITFOR DELAY '0:0:5)-- or SLEEP(5) infers data through latency.",
            Guidance),

        new("SqlInjection", "BlindErrorBased",
            "Error-based: ' AND 1=CAST((SELECT password) AS INT)-- forces an error leaking the payload.",
            Guidance),

        new("SqlInjection", "SecondOrderStored",
            "Stored payload: a value stored in one table is later concatenated into a query elsewhere without sanitization.",
            Guidance),

        new("SqlInjection", "ViaJsonField",
            "Injection through a JSON property value that is later interpolated into a query string.",
            Guidance),

        new("SqlInjection", "ViaQueryParam",
            "Injection through a query string parameter that flows into a dynamic SQL statement.",
            Guidance),

        new("SqlInjection", "ViaHttpHeader",
            "Injection through an HTTP header (e.g. User-Agent or X-Forwarded-For) logged into a query.",
            Guidance),

        new("SqlInjection", "CommentTermination",
            "Comment-based termination: /* ... */ or # attempts to truncate the remainder of a query.",
            Guidance),

        new("SqlInjection", "HexEncoding",
            "Hex-encoded payload: 0x27204f522731273d2731 evades naive string filters.",
            Guidance),

        new("SqlInjection", "CharEncoding",
            "CHAR() function encoding: CHAR(39)+CHAR(79)+CHAR(82) reconstructs 'OR to bypass filters.",
            Guidance),

        new("SqlInjection", "StoredProcedureInjection",
            "Injection via a stored procedure parameter that is concatenated into dynamic SQL inside the proc.",
            Guidance),
    ];
}