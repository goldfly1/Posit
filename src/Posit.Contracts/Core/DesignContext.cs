using Posit.Contracts.Artifacts;

namespace Posit.Contracts.Core;

/// <summary>
/// Compact, structured design summary that accumulates across phases.
/// Architecture adds component definitions; C# Implementation adds compiled API.
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

    // From Pseudocode (legacy — may be empty)
    public DesignModule[] Modules { get; init; } = [];
    public DesignDataFlow[] DataFlows { get; init; } = [];
    public DesignInterfaceMapping[] InterfaceMappings { get; init; } = [];
    public string? ProjectStructure { get; init; }
    public string? TestPlanOutline { get; init; }

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
        string.IsNullOrWhiteSpace(CompiledApi);

    /// <summary>
    /// Renders a compact per-module prompt block for injection into a single
    /// module's implementation prompt.
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
            if (component.TestCases is { Length: > 0 })
            {
                lines.Add("  Test Cases (from architect — these are the acceptance criteria):");
                foreach (var tc in component.TestCases)
                    lines.Add($"    - {tc.Id}: {tc.Name} (target: {tc.TargetType}) — {tc.Description} → {tc.ExpectedBehavior}");
            }
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
    /// surfaces and test cases from the architect.
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

        lines.Add("--- END DESIGN CONTEXT ---");
        return string.Join("\n", lines);
    }
}

// Compact summaries of each artifact type
public record DesignComponent(string Id, string Name, string Responsibility, string Tech, string[] PublicSurface, string Internals, string[] Dependencies)
{
    public DesignTestCase[] TestCases { get; init; } = [];
    public ModuleClassification Classification { get; init; } = ModuleClassification.IoShell;
    public string[] StubNames { get; init; } = [];
    public MethodSignature[] MethodSignatures { get; init; } = [];
    public ConnectionSpec[] Connections { get; init; } = [];
    public SharedTypeRef[] SharedTypes { get; init; } = [];
    public string? EntryType { get; init; }
    public string? BranchCondition { get; init; }
}

public record DesignTestCase(string Id, string Name, string TargetType, string Description, string ExpectedBehavior);
public record DesignDataStore(string Id, string Name, string Kind, string Schema);
public record DesignInterface(string Id, string Name, string Protocol, string AuthScheme, string Direction, string SchemaRef);
public record DesignQualityAttribute(string Attribute, string Target);
public record DesignEndpoint(string Path, string Method, string ResponseSchemaRef);
public record DesignAuthScheme(string Name, string Type, string Description);

public record DesignModule(string Id, string Name, string Responsibility, string ProjectPath, string OutputType, string[] PublicSurface, string Internals, string[] Dependencies)
{
    public ModuleClassification Classification { get; init; } = ModuleClassification.IoShell;
}

public record DesignDataFlow(string Id, string From, string To, string Payload, string Frequency);
public record DesignInterfaceMapping(string InterfaceId, string ModuleId, string RealizedAs);