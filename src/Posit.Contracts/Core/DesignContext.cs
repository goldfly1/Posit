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

    /// <summary>
    /// Renders a compact per-module prompt block for injection into a single
    /// module's implementation prompt. Only includes context relevant to the
    /// named module — not all modules' worth of architecture. Keeps prompts
    /// at ~2-5K instead of ~50K.
    /// </summary>
    public string ToModulePromptBlock(string moduleName)
    {
        if (IsEmpty)
            return string.Empty;

        var lines = new List<string>();
        lines.Add("--- DESIGN CONTEXT (for this module only) ---");

        var component = Components.FirstOrDefault(c =>
            c.Name.Equals(moduleName, StringComparison.OrdinalIgnoreCase) ||
            c.Name.Contains(moduleName, StringComparison.OrdinalIgnoreCase) ||
            moduleName.Contains(c.Name, StringComparison.OrdinalIgnoreCase));

        if (component is not null)
        {
            lines.Add($"Component: {component.Name} [{component.Tech}]");
            lines.Add($"  Responsibility: {component.Responsibility}");
            lines.Add($"  Classification: {component.Classification}");
            if (component.PublicSurface is { Length: > 0 })
                lines.Add($"  Public Surface: {string.Join(", ", component.PublicSurface)}");
            if (!string.IsNullOrWhiteSpace(component.Internals))
                lines.Add($"  Internals: {component.Internals}");
            if (component.Dependencies is { Length: > 0 })
                lines.Add($"  Dependencies: {string.Join(", ", component.Dependencies)}");
            if (!string.IsNullOrWhiteSpace(component.DafnyContractPath))
                lines.Add($"  Dafny Skeleton: {component.DafnyContractPath} (read this file — names, types, contracts are the authority)");
            if (component.TestCases is { Length: > 0 })
            {
                lines.Add("  Test Cases (from architect — these are the acceptance criteria):");
                foreach (var tc in component.TestCases)
                    lines.Add($"    - {tc.Id}: {tc.Name} (target: {tc.TargetType}) — {tc.Description} → {tc.ExpectedBehavior}");
            }
        }

        // Include the Dafny contract for this module if available
        var dafnyContract = DafnyContracts?.FirstOrDefault(c =>
            c.ModuleName.Equals(moduleName, StringComparison.OrdinalIgnoreCase));
        if (dafnyContract is not null)
        {
            lines.Add($"  Dafny Contract: {(dafnyContract.IsVerified ? "VERIFIED" : "UNVERIFIED")}");
            if (!string.IsNullOrWhiteSpace(dafnyContract.VerificationOutput))
                lines.Add($"  Z3 Output: {dafnyContract.VerificationOutput[..Math.Min(200, dafnyContract.VerificationOutput.Length)]}...");
        }

        // Dependency modules' public surfaces only
        var depNames = component?.Dependencies ?? [];
        if (depNames.Length > 0)
        {
            lines.Add("Dependency Modules (public surface only):");
            foreach (var depName in depNames)
            {
                var dep = Components.FirstOrDefault(c =>
                    c.Name.Equals(depName, StringComparison.OrdinalIgnoreCase));
                if (dep is not null && dep.PublicSurface is { Length: > 0 })
                    lines.Add($"  {dep.Name}: {string.Join(", ", dep.PublicSurface)}");
            }
        }

        if (!string.IsNullOrWhiteSpace(DeploymentTopology))
            lines.Add($"Deployment Topology: {DeploymentTopology}");

        lines.Add("--- END DESIGN CONTEXT ---");
        return string.Join("\n", lines);
    }

    /// <summary>
    /// Renders a QA-focused prompt block. Includes module list with public
    /// surfaces, test cases from the architect, and Dafny verification status.
    /// QA tests against the public surface and the architect's test cases,
    /// not the full architecture.
    /// </summary>
    public string ToQaPromptBlock()
    {
        if (IsEmpty)
            return string.Empty;

        var lines = new List<string>();
        lines.Add("--- DESIGN CONTEXT (for QA test generation) ---");

        if (Components.Length > 0)
        {
            lines.Add("Modules:");
            foreach (var c in Components)
            {
                lines.Add($"  - {c.Name}: {c.Responsibility} [{c.Classification}]");
                if (c.PublicSurface is { Length: > 0 })
                    lines.Add($"    Public Surface: {string.Join(", ", c.PublicSurface)}");
                if (c.TestCases is { Length: > 0 })
                {
                    lines.Add("    Test Cases (acceptance criteria from architect):");
                    foreach (var tc in c.TestCases)
                        lines.Add($"      - {tc.Id}: {tc.Name} (target: {tc.TargetType}) — {tc.Description} → {tc.ExpectedBehavior}");
                }
            }
        }

        // Dafny verification status — QA needs to know which modules are verified
        if (DafnyContracts is { Length: > 0 })
        {
            lines.Add("Dafny Verification Status:");
            foreach (var dc in DafnyContracts)
            {
                lines.Add($"  - {dc.ModuleName}: {(dc.IsVerified ? "VERIFIED (compile only, no tests)" : "UNVERIFIED (needs tests)")}");
            }
        }

        lines.Add("--- END DESIGN CONTEXT ---");
        return string.Join("\n", lines);
    }
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
    /// Path to the .dfy skeleton file on disk. The file is the authority.
    /// </summary>
    public string? DafnyContractPath { get; init; }

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
    public string DafnySource { get; init; } = "";  // kept for DB logging
    public required string DafnyPath { get; init; }  // file on disk — the authority
    public required bool IsVerified { get; init; }
    public string? VerificationOutput { get; init; }
    public string? TranslatedCSharpPath { get; init; }  // translated C# file path
}