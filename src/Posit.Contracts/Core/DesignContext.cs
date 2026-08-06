namespace Posit.Contracts.Core;

/// <summary>
/// Compact, structured design summary that accumulates across phases.
/// Each design phase (Architecture, API Definition, Pseudocode) adds its piece.
/// Implementation reads the full accumulated context to avoid losing key
/// decisions that would otherwise be lost to context reset between phases.
/// </summary>
public record DesignContext
{
    // From Architecture
    public DesignComponent[] Components { get; init; } = [];
    public DesignDataStore[] DataStores { get; init; } = [];
    public DesignInterface[] Interfaces { get; init; } = [];
    public DesignQualityAttribute[] QualityAttributes { get; init; } = [];
    public string? DeploymentTopology { get; init; }

    // From API Definition
    public DesignEndpoint[] Endpoints { get; init; } = [];
    public DesignAuthScheme[] AuthSchemes { get; init; } = [];
    public string? VersioningPolicy { get; init; }

    // From Pseudocode
    public DesignModule[] Modules { get; init; } = [];
    public DesignDataFlow[] DataFlows { get; init; } = [];
    public DesignInterfaceMapping[] InterfaceMappings { get; init; } = [];
    public string? ProjectStructure { get; init; }
    public string? TestPlanOutline { get; init; }

    // From Dafny Contracts Phase — skeleton .dfy source per module
    public DafnyContractEntry[] DafnyContracts { get; init; } = [];

    // From Implementation — the REAL compiled API
    public string? CompiledApi { get; init; }

    public bool IsEmpty =>
        Components.Length == 0 &&
        DataStores.Length == 0 &&
        Interfaces.Length == 0 &&
        Endpoints.Length == 0 &&
        AuthSchemes.Length == 0 &&
        Modules.Length == 0 &&
        DataFlows.Length == 0 &&
        InterfaceMappings.Length == 0 &&
        DafnyContracts.Length == 0 &&
        string.IsNullOrWhiteSpace(CompiledApi);
}

// Compact summaries of each artifact type
public record DesignComponent(string Id, string Name, string Responsibility, string Tech, string[] PublicSurface, string Internals, string[] Dependencies)
{
    public DesignTestCase[] TestCases { get; init; } = [];

    /// <summary>
    /// How the architect classified this module for the Dafny-first pipeline.
    /// dafny = pure logic (Z3 verified). io-shell = side effects (C# only).
    /// mixed = split into both.
    /// </summary>
    public ModuleClassification Classification { get; init; } = ModuleClassification.IoShell;

    /// <summary>
    /// Dafny contract source (.dfy skeleton) written by the architect.
    /// Only populated for dafny and mixed modules.
    /// </summary>
    public string? DafnyContractSource { get; init; }

    /// <summary>
    /// True when this module has been verified by Dafny. Verified modules
    /// skip QA test stubs and edge case patterns — the proof IS the test.
    /// </summary>
    public bool IsVerified { get; init; }
}

public record DesignTestCase(string Id, string Name, string TargetType, string Description, string ExpectedBehavior);
public record DesignDataStore(string Id, string Name, string Kind, string Schema);
public record DesignInterface(string Id, string Name, string Protocol, string AuthScheme, string Direction, string SchemaRef);
public record DesignQualityAttribute(string Attribute, string Target);
public record DesignEndpoint(string Path, string Method, string ResponseSchemaRef);
public record DesignAuthScheme(string Name, string Type, string Description);

/// <summary>
/// A module from the pseudocode phase, enriched with Dafny classification.
/// </summary>
public record DesignModule(string Id, string Name, string Responsibility, string ProjectPath, string OutputType, string[] PublicSurface, string Internals, string[] Dependencies)
{
    /// <summary>
    /// How the architect classified this module. Determines whether it goes
    /// through the Dafny pipeline (dafny) or the C# pipeline (io-shell).
    /// </summary>
    public ModuleClassification Classification { get; init; } = ModuleClassification.IoShell;

    /// <summary>
    /// True when Z3 has verified this module's Dafny contracts.
    /// Set by the Dafny Contracts phase — NOT by the model.
    /// </summary>
    public bool IsVerified { get; init; }
}

public record DesignDataFlow(string Id, string From, string To, string Payload, string Frequency);
public record DesignInterfaceMapping(string InterfaceId, string ModuleId, string RealizedAs);

/// <summary>
/// A verified (or pending verification) Dafny contract skeleton for a module.
/// Produced by the Dafny Contracts phase from the architect's .dfy skeletons.
/// </summary>
public record DafnyContractEntry
{
    public required string ModuleName { get; init; }
    public required string DafnySource { get; init; }
    public required bool IsVerified { get; init; }
    public string? VerificationOutput { get; init; }
    public string? TranslatedCSharp { get; init; }
}