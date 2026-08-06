namespace Posit.Contracts.Artifacts;

public record ArchitectureContract
{
    public required string SystemContext { get; init; }
    public required Component[] Components { get; init; }
    public required DataStore[] DataStores { get; init; }
    public required InterfaceSpec[] Interfaces { get; init; }
    public required string DeploymentTopology { get; init; }
    public required QualityAttribute[] QualityAttributes { get; init; }
    public required ArchDecision[] Decisions { get; init; }
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

    /// <summary>
    /// How the architect classified this module for the Dafny-first pipeline.
    /// dafny = pure logic (Z3 verified). io-shell = side effects (C# only).
    /// mixed = split into both.
    /// </summary>
    public ModuleClassification Classification { get; init; } = ModuleClassification.IoShell;

    /// <summary>
    /// Path to the .dfy skeleton file on disk (in staging directory).
    /// Only populated for dafny and mixed modules. The file is the authority —
    /// names, types, contracts, dependencies, all tattooed on the carapace.
    /// Downstream phases read from and write to this file.
    /// </summary>
    public string? DafnyContractPath { get; init; }

    /// <summary>
    /// True when this module has been verified by Dafny. Verified modules
    /// do not need QA test stubs or edge case patterns — the proof IS the test.
    /// Set by the Dafny Contracts verification gate after successful proof.
    /// </summary>
    public bool IsVerified { get; init; }
}

public record ComponentTestCase(
    string Id,
    string Name,
    string TargetType,
    string Description,
    string ExpectedBehavior);

public record DataStore(string Id, string Name, DataStoreKind Kind, string Schema, PersistenceKind Persistence);
public record InterfaceSpec(string Id, string Name, InterfaceDirection Direction, string Protocol, string SchemaRef, AuthSpec Auth);
public enum InterfaceDirection { Inbound, Outbound }
public record AuthSpec(string Scheme, string Details);
public record QualityAttribute(string Attribute, string Target, string Rationale);
public record ArchDecision(string Id, string Title, DecisionStatus Status, string Context, string Decision, string Consequences);
public enum DecisionStatus { Proposed, Accepted, Superseded }
public record Risk(string Id, string Description, RiskSeverity Severity, string Mitigation);