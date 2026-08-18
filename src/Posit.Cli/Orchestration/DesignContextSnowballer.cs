using Posit.Contracts.Artifacts;
using Posit.Contracts.Core;

namespace Posit.Cli.Orchestration;

/// <summary>
/// Snowballs DesignContext across phases — each design phase adds its piece.
/// Implementation reads the full accumulated context to avoid losing decisions.
/// </summary>
public static class DesignContextSnowballer
{
    public static DesignContext? Snowball(DesignContext? current, PhaseResult result)
    {
        var p = result.Artifacts.PayloadJson;
        return result.PhaseId.Value switch
        {
            "architecture" => SnowballArch(current, p),
            "dafny-contracts" => SnowballContracts(current, p),
            "dafny-implementation" => SnowballImpl(current, p),
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
            Classification = x.Classification, PatternName = x.PatternName, StubNames = x.StubNames,
            DafnyContractPath = x.DafnyContractPath, ParametersJson = x.ParametersJson,
            MethodSignatures = x.MethodSignatures, Connections = x.Connections, SharedTypes = x.SharedTypes,
            EntryType = x.EntryType, BranchCondition = x.BranchCondition,
            TestCases = x.TestCases.Select(tc => new DesignTestCase(
                tc.Id, tc.Name, tc.TargetType, tc.Description, tc.ExpectedBehavior)).ToArray()
        }).ToArray();
        return (current ?? new DesignContext()) with
        { Components = comps, DeploymentTopology = c.DeploymentTopology };
    }

    private static DesignContext? SnowballContracts(DesignContext? current, byte[] p)
    {
        var cs = Deserialize<DafnyContractResult[]>(p);
        if (cs is null || cs.Length == 0) return current;
        var entries = cs.Select(c => new DafnyContractEntry { ModuleName = c.ModuleName,
            DafnyPath = c.DafnyPath, IsVerified = c.IsVerified,
            VerificationOutput = c.VerificationOutput }).ToArray();
        return (current ?? new DesignContext()) with { DafnyContracts = entries };
    }

    private static DesignContext? SnowballImpl(DesignContext? current, byte[] p)
    {
        var results = Deserialize<DafnyVerificationResult[]>(p);
        if (results is null || results.Length == 0) return current;
        var updated = (current?.DafnyContracts ?? []).Select(ec =>
        {
            var vr = results.FirstOrDefault(r => r.ModuleName == ec.ModuleName);
            return vr is not null ? ec with { IsVerified = vr.IsVerified,
                VerificationOutput = vr.VerificationOutput,
                TranslatedCSharpPath = vr.TranslatedCSharpPath } : ec;
        }).ToArray();
        return (current ?? new DesignContext()) with { DafnyContracts = updated };
    }

    private static T? Deserialize<T>(byte[] payload) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(payload, PositJson.Options); }
        catch { return null; }
    }
}