using Posit.Contracts.Artifacts;
using Posit.Contracts.Core;

namespace Posit.Cli.Orchestration;

/// <summary>
/// Snowballs DesignContext across phases — Architecture adds components.
/// </summary>
public static class DesignContextSnowballer
{
    public static DesignContext? Snowball(DesignContext? current, PhaseResult result)
    {
        return result.PhaseId.Value switch
        {
            "architecture" => SnowballArch(current, result.Artifacts.PayloadJson),
            _ => current
        };
    }

    private static DesignContext? SnowballArch(DesignContext? current, byte[] p)
    {
        var c = Deserialize<ArchitectureContract>(p);
        if (c is null) return current;
        var comps = c.Components.Select(x => new DesignComponent(
            x.Id, x.Name, x.Responsibility, x.Tech, x.PublicSurface, x.Internals, x.Dependencies)
        {
            Classification = x.Classification, StubNames = x.StubNames,
            MethodSignatures = x.MethodSignatures, Connections = x.Connections, SharedTypes = x.SharedTypes,
            EntryType = x.EntryType, BranchCondition = x.BranchCondition,
            TestCases = x.TestCases.Select(tc => new DesignTestCase(
                tc.Id, tc.Name, tc.TargetType, tc.Description, tc.ExpectedBehavior)).ToArray()
        }).ToArray();
        return (current ?? new DesignContext()) with
        { Components = comps, DeploymentTopology = c.DeploymentTopology };
    }

    private static T? Deserialize<T>(byte[] payload) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(payload, PositJson.Options); }
        catch { return null; }
    }
}