using Posit.Contracts.Core;

namespace Posit.Core.State;

/// <summary>
/// Known phase IDs for the Posit C#-direct pipeline.
/// Order: Architecture → C# Implementation → QA
/// </summary>
public static class KnownPhases
{
    public static readonly PhaseId Ideation = new("ideation");
    public static readonly PhaseId Architecture = new("architecture");
    public static readonly PhaseId ApiDefinition = new("api-definition");
    public static readonly PhaseId DesignReview = new("design-review");
    public static readonly PhaseId CSharpImplementation = new("csharp-implementation");
    public static readonly PhaseId Qa = new("qa");
    public static readonly PhaseId Deployment = new("deployment");
    public static readonly PhaseId Observability = new("observability");
    public static readonly PhaseId Documentation = new("documentation");

    public static readonly PhaseId[] AllPhases =
    [
        Ideation,
        Architecture,
        ApiDefinition,
        DesignReview,
        CSharpImplementation,
        Qa,
        Deployment,
        Observability,
        Documentation
    ];
}