namespace Posit.Contracts.Artifacts;

/// <summary>
/// Result of QA for a single module.
/// </summary>
public record QaModuleResult
{
    public string ModuleName { get; init; } = "";
    public bool IsVerified { get; init; }
    public int TestCount { get; init; }
    public string Notes { get; init; } = "";
}

/// <summary>
/// Test suite artifact produced by the QA phase. Contains test files
/// for unverified (io-shell) modules and metadata for all modules.
/// </summary>
public record TestSuite
{
    public SourceCodeFile[] TestFiles { get; init; } = [];
    public QaModuleResult[] ModuleResults { get; init; } = [];
    public required string Summary { get; init; }
}