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
    /// Name of the registry pattern selected for this dafny/mixed component.
    /// The object registry in patterns/ supplies the pre-cut hull shape.
    /// </summary>
    public string? PatternName { get; init; }

    /// <summary>
    /// Names of registry stub groups selected for this component's I/O portals.
    /// These are the {:extern} declarations from patterns/stubs/ that Pass 2 plugs.
    /// </summary>
    public string[] StubNames { get; init; } = [];

    /// <summary>
    /// Parameters for the selected registry pattern. Key-value pairs that
    /// customize the pre-cut pattern body (e.g., delimiter, quoteChar, hasHeader).
    /// The pipeline instantiates the pattern with these parameters.
    /// </summary>
    public string? ParametersJson { get; init; }

    /// <summary>
    /// True when this module has been verified by Dafny. Verified modules
    /// do not need QA test stubs or edge case patterns — the proof IS the test.
    /// Set by the Dafny Contracts verification gate after successful proof.
    /// </summary>
    public bool IsVerified { get; init; }

    /// <summary>
    /// Method signatures for each public surface method. The architect fills
    /// these out so the orchestrator can wire components deterministically —
    /// it knows the actual parameter types and return types, not just names.
    /// This is the tab/slot data for the trireme kit.
    /// </summary>
    public MethodSignature[] MethodSignatures { get; init; } = [];

    /// <summary>
    /// Connection specifications — how this component calls its dependencies.
    /// Each spec says: this component's method X calls dependency Y's method Z
    /// with these argument mappings. The orchestrator reads these to generate
    /// wiring code deterministically. No model judgment at wiring time.
    /// </summary>
    public ConnectionSpec[] Connections { get; init; } = [];

    /// <summary>
    /// Types shared with other modules via Dafny `include`. Each entry names
    /// the type and which module defines it. The orchestrator uses this to
    /// resolve cross-module type references during wiring.
    /// </summary>
    public SharedTypeRef[] SharedTypes { get; init; } = [];
}

/// <summary>
/// A method signature — the actual contract for a public surface method.
/// The architect fills these out instead of just listing method names.
/// </summary>
public record MethodSignature(
    string Name,
    MethodParam[] Params,
    string ReturnType,
    string? ReturnDafnyType)
{
    /// <summary>
    /// The pattern method this maps to (e.g., architect names it "RunPipeline"
    /// but the pipeline pattern provides "HandleRequest"). The orchestrator
    /// uses this to generate the actual call.
    /// </summary>
    public string? PatternMethod { get; init; }
}

public record MethodParam(string Name, string Type, string? DafnyType);

/// <summary>
/// A connection specification — how one component calls another.
/// This is the connector data that makes deterministic wiring possible.
/// The architect fills these out on the carapace.
/// </summary>
public record ConnectionSpec(
    string FromMethod,
    string ToComponent,
    string ToMethod,
    string[] ArgMappings)
{
    /// <summary>
    /// The return type from the called method, used to declare the
    /// variable that receives the result.
    /// </summary>
    public string? ReturnType { get; init; }

    /// <summary>
    /// How the return value is used by the calling method
    /// (e.g., "passes to next dependency", "stores as local", "returns to caller").
    /// </summary>
    public string? ReturnUsage { get; init; }
}

/// <summary>
/// A type shared across modules via Dafny `include`.
/// </summary>
public record SharedTypeRef(
    string TypeName,
    string DefinedInModule);

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