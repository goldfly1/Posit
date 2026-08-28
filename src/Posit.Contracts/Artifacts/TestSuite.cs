namespace Posit.Contracts.Artifacts;

/// <summary>
/// Test suite artifact produced by the QA phase. Contains test files
/// for the CLI component and expected output maps for exact comparison.
/// </summary>
public record TestSuite
{
    public SourceCodeFile[] TestFiles { get; init; } = [];
    public required string Summary { get; init; }
    /// <summary>
    /// Expected output per test case, keyed by test case ID (e.g. "tc1", "tc2").
    /// Empty = no exact match available (use fuzzy comparison).
    /// </summary>
    public Dictionary<string, string> ExpectedOutputs { get; init; } = [];
    /// <summary>
    /// Expected exit code per test case, keyed by test case ID.
    /// </summary>
    public Dictionary<string, int> ExpectedExitCodes { get; init; } = [];
}