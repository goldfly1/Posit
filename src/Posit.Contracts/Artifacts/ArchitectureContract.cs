using System.Text.Json;

namespace Posit.Contracts.Artifacts;

public record ArchitectureContract
{
    public required string SystemContext { get; init; }
    public required Component[] Components { get; init; }
    public DataStore[] DataStores { get; init; } = [];
    public InterfaceSpec[] Interfaces { get; init; } = [];
    public string DeploymentTopology { get; init; } = "";
    public QualityAttribute[] QualityAttributes { get; init; } = [];
    public ArchDecision[] Decisions { get; init; } = [];
    public Risk[] OpenRisks { get; init; } = [];
}

public record Component(
    string Id,
    string Name,
    string Responsibility,
    string[] PublicSurface,
    string Internals,
    string[] Dependencies,
    int Layer,
    string Tech)
{
    public ComponentTestCase[] TestCases { get; init; } = [];
    public ModuleClassification Classification { get; init; } = ModuleClassification.IoShell;
    public string? CSharpInterfacePath { get; init; }
    public string[] StubNames { get; init; } = [];
    public MethodSignature[] MethodSignatures { get; init; } = [];
    public ConnectionSpec[] Connections { get; init; } = [];
    public SharedTypeRef[] SharedTypes { get; init; } = [];

    /// <summary>
    /// The C# interface written by the architect — interface declaration, record types,
    /// method signatures with XML doc contract intent. Written to .cs on disk as the
    /// carapace. Null for io-shell components.
    /// </summary>
    public string? CSharpInterface { get; init; }

    /// <summary>
    /// Data flow spec: how the orchestrator component reads input.
    /// "file" = args[0] is a file path (use ReadFile/ReadLines).
    /// "stdin" = read from Console.ReadLine().
    /// null/empty = default to "file" for backward compatibility.
    /// </summary>
    public string? EntryType { get; init; }

    /// <summary>
    /// Data flow spec: branching condition for error paths.
    /// e.g. "if !isValid: print error, exit 1".
    /// The wiring generator reads this to emit if-branches.
    /// null = no branching (linear pipeline).
    /// </summary>
    public string? BranchCondition { get; init; }
}

public record MethodSignature(
    string Name,
    MethodParam[] Params,
    string ReturnType)
{
    public string? PatternMethod { get; init; }
}

public record MethodParam(string Name, string Type);

public record ConnectionSpec(
    string FromMethod,
    string ToComponent,
    string ToMethod,
    string[] ArgMappings)
{
    public string? ReturnType { get; init; }
    public string? ReturnUsage { get; init; }
}

public record SharedTypeRef(string TypeName, string DefinedInModule);

public record ComponentTestCase(
    string Id,
    string Name,
    string TargetType,
    string Description,
    string ExpectedBehavior)
{
    /// <summary>
    /// Exact input for this test case (file content or stdin line, depending on
    /// entryType). Empty = QA generates input heuristically from the description.
    /// </summary>
    public string Input { get; init; } = "";

    /// <summary>
    /// Exact stdout expected when the program processes Input. This is the answer
    /// key for the judge's exact-match layer. Null = no exact match available
    /// (bot heuristics apply). Empty string = the expected output IS the empty
    /// string (T12 tc3: empty inputs legitimately produce empty merged config).
    /// </summary>
    public string? ExpectedOutput { get; init; }

    /// <summary>
    /// Expected exit code for this test case. 0 = success, 1 = error.
    /// </summary>
    public int ExpectedExitCode { get; init; } = 0;
}

public record DataStore(string Id, string Name, DataStoreKind Kind, string Schema, PersistenceKind Persistence);
public record InterfaceSpec(string Id, string Name, InterfaceDirection Direction, string Protocol, string SchemaRef, AuthSpec Auth);
public enum InterfaceDirection { Inbound, Outbound }
public record AuthSpec(string Scheme, string Details);
public record QualityAttribute(string Attribute, string Target, string Rationale);
public record ArchDecision(string Id, string Title, DecisionStatus Status, string Context, string Decision, string Consequences);
public enum DecisionStatus { Proposed, Accepted, Superseded }
public record Risk(string Id, string Description, RiskSeverity Severity, string Mitigation);