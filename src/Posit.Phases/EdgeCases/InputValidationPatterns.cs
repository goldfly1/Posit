namespace Posit.Phases.EdgeCases;

/// <summary>
/// Edge case patterns covering input validation boundary conditions.
/// </summary>
public static class InputValidationPatterns
{
    /// <summary>
    /// Gets the full set of input validation edge case patterns.
    /// </summary>
    public static EdgeCasePattern[] All =>
    [
        new("InputValidation", "EmptyString",
            "Input is an empty string, which may bypass length checks or produce empty outputs.",
            "Pass an empty string and verify the result is handled gracefully without exceptions or corruption."),

        new("InputValidation", "WhitespaceOnly",
            "Input consists solely of whitespace characters (spaces, tabs, newlines).",
            "Pass \"   \\t\\n\" and verify trimming/validation logic does not treat it as valid content."),

        new("InputValidation", "NullInput",
            "Input is null where a reference is expected.",
            "Pass null and verify a null-guard or ArgumentNullException is raised, not a NullReferenceException."),

        new("InputValidation", "UnicodeEmoji",
            "Input contains emoji and multi-byte surrogate pairs (e.g. \"\\uD83D\\uDE00\").",
            "Pass a string containing emoji and verify string length, encoding, and rendering are correct."),

        new("InputValidation", "UnicodeRtl",
            "Input contains right-to-left (RTL) characters such as Arabic or Hebrew.",
            "Pass an RTL string and verify ordering, display, and normalization behave correctly."),

        new("InputValidation", "UnicodeZeroWidth",
            "Input contains zero-width characters (U+200B, U+200C, U+200D, U+FEFF).",
            "Pass a string with zero-width characters and verify they are stripped or handled without altering visible content."),

        new("InputValidation", "UnicodeCombining",
            "Input contains combining characters that modify preceding base characters (e.g. \"e\\u0301\").",
            "Pass a combining-character string and verify normalization (NFC/NFD) is consistent across comparisons."),

        new("InputValidation", "VeryLongString10K",
            "Input is a 10,000+ character string that may exceed buffer or length limits.",
            "Pass a 10K+ string and verify length validation rejects or truncates it without crashing."),

        new("InputValidation", "VeryLongString1M",
            "Input is a 1,000,000+ character string that may exhaust memory or time out.",
            "Pass a 1M+ string and verify the system rejects or streams it without OOM or hangs."),

        new("InputValidation", "NegativeNumber",
            "Numeric input is negative where only non-negative values are expected.",
            "Pass -1 and verify validation rejects it or the operation handles negative values correctly."),

        new("InputValidation", "Zero",
            "Numeric input is zero where zero may be a degenerate or division-by-zero case.",
            "Pass 0 and verify division, indexing, or default-selection logic does not fail."),

        new("InputValidation", "MaxInt",
            "Numeric input is int.MaxValue (2,147,483,647), the boundary of the 32-bit signed range.",
            "Pass int.MaxValue and verify no overflow occurs on increment or arithmetic."),

        new("InputValidation", "MinInt",
            "Numeric input is int.MinValue (-2,147,483,648), the boundary of the 32-bit signed range.",
            "Pass int.MinValue and verify abs(), negation, and casting do not overflow."),

        new("InputValidation", "IntegerOverflow",
            "Arithmetic on a large value overflows the integer range.",
            "Cause overflow (e.g. MaxValue + 1) and verify checked arithmetic or wrapping behavior is as designed."),

        new("InputValidation", "FloatNaN",
            "Floating-point input is NaN, which compares unequal to itself.",
            "Pass double.NaN and verify comparisons, equality, and ordering do not misbehave."),

        new("InputValidation", "FloatInfinity",
            "Floating-point input is positive or negative infinity.",
            "Pass double.PositiveInfinity and double.NegativeInfinity and verify serialization and arithmetic are bounded."),

        new("InputValidation", "FloatNegativeZero",
            "Floating-point input is negative zero (-0.0), which is equal to 0.0 but has a distinct sign bit.",
            "Pass -0.0 and verify equality, sign, and formatting are handled consistently."),

        new("InputValidation", "FloatEpsilon",
            "Floating-point input is the smallest positive representable value (double.Epsilon).",
            "Pass double.Epsilon and verify comparisons against zero and accumulation do not lose it."),

        new("InputValidation", "FloatPrecisionLoss",
            "Floating-point input causes precision loss due to binary representation (e.g. 0.1 + 0.2 != 0.3).",
            "Pass 0.1 and 0.2 and verify accumulation uses tolerance-based comparison, not exact equality."),

        new("InputValidation", "DateTimeTimezoneOffsets",
            "DateTime inputs with extreme or fractional timezone offsets (e.g. +14:00, -12:00, +05:45).",
            "Pass DateTimeOffsets with unusual offsets and verify conversion to UTC is correct."),

        new("InputValidation", "DateTimeDstTransition",
            "DateTime input falls in a DST transition gap (spring-forward) or overlap (fall-back).",
            "Pass a local time in the DST gap and verify the disambiguation policy (skip/shift/throw) is applied."),

        new("InputValidation", "DateTimePre1970",
            "DateTime input predates the Unix epoch (1970-01-01).",
            "Pass a date like 1900-01-01 and verify epoch conversion and serialization do not overflow."),

        new("InputValidation", "DateTimePost9999",
            "DateTime input is after the year 9999, exceeding the DateTime range.",
            "Pass a far-future date and verify validation rejects it or the overflow is caught."),

        new("InputValidation", "DateTimeUtcVsLocal",
            "DateTime input is labeled Unspecified when it is implicitly UTC or local.",
            "Pass a DateTime with Kind=Unspecified and verify conversion assumptions are explicit and correct."),

        new("InputValidation", "NullCollectionVsEmpty",
            "A null collection is passed where an empty collection is semantically distinct.",
            "Pass null and an empty collection separately and verify both are handled without ambiguity."),

        new("InputValidation", "DuplicateEntriesInUniqueCollection",
            "A collection claiming uniqueness contains duplicate entries.",
            "Pass duplicate entries and verify deduplication or constraint enforcement rejects them."),

        new("InputValidation", "VeryLargeCollection",
            "A collection with millions of entries causes memory pressure and slow iteration.",
            "Pass a collection of 1M+ items and verify pagination, streaming, or rejection prevents OOM."),

        new("InputValidation", "BoundaryBelowMin",
            "Value is just below the documented minimum (e.g. min-1).",
            "Pass min-1 and verify boundary validation rejects it, not the next layer."),

        new("InputValidation", "BoundaryAtMin",
            "Value is exactly at the documented minimum.",
            "Pass exactly min and verify the operation accepts and processes it correctly."),

        new("InputValidation", "BoundaryAboveMin",
            "Value is just above the documented minimum (e.g. min+1).",
            "Pass min+1 and verify the operation accepts it and behavior transitions correctly."),

        new("InputValidation", "BoundaryBelowMax",
            "Value is just below the documented maximum (e.g. max-1).",
            "Pass max-1 and verify the operation accepts it without overflow or truncation."),

        new("InputValidation", "BoundaryAtMax",
            "Value is exactly at the documented maximum.",
            "Pass exactly max and verify the operation accepts and processes it correctly."),

        new("InputValidation", "BoundaryAboveMax",
            "Value is just above the documented maximum (e.g. max+1).",
            "Pass max+1 and verify boundary validation rejects it, not the next layer."),
    ];
}