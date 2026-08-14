namespace Posit.Cli.Orchestration;

using Posit.Contracts.Artifacts;
using Posit.Core.State;

/// <summary>
/// Pipeline orchestrator. Runs the phase loop: find next runnable phase from
/// the dependency graph, execute via PhaseController, validate output, route
/// through FsmReducer, snowball DesignContext, persist artifacts.
/// </summary>
public sealed class PositOrchestrator(PhaseController controller, FsmReducer fsm,
    IDependencyGraphEngine graph, ArtifactRepository artifactRepo, StateStore stateStore)
{
    private readonly PhaseController _controller = controller;
    private readonly FsmReducer _fsm = fsm;
    private readonly IDependencyGraphEngine _graph = graph;
    private readonly ArtifactRepository _artifactRepo = artifactRepo;
    private readonly StateStore _stateStore = stateStore;

    public async Task<SessionState> RunAsync(SessionState state, CancellationToken ct = default)
    {
        if (state.Status == SessionStatus.Idle)
        {
            var start = _fsm.Apply(state, "session.start");
            if (start.WasRejected) { Log($"Rejected: {start.RejectionReason}"); return state; }
            state = start.State;
        }

        while (true)
        {
            if (state.Status is SessionStatus.Planning or SessionStatus.Retry or SessionStatus.CheckpointRollback)
            {
                state = AdvanceFsm(state);
                if (state.Status != SessionStatus.Active) break;
            }
            if (state.Status != SessionStatus.Active) break;

            var phaseId = state.CurrentPhaseId!.Value;
            var phase = _controller.Resolve(phaseId);
            if (phase is null) break;

            var context = await BuildContext(state, phaseId);
            await phase.InitializeAsync(context, ct);
            var result = await _controller.ExecuteAsync(context, ct);
            var validation = phase.ValidateOutput(result);

            if (validation.IsValid && phaseId.Value == "csharp-implementation")
            {
                var errors = await EnforceCarapace(result, state, ct);
                if (errors.Length > 0)
                {
                    validation = new ValidationResult { IsValid = false, Errors = errors };
                    result = result with { Status = PhaseStatus.Failed, Warnings = errors };
                }
            }

            var ok = validation.IsValid;
            state = _fsm.Apply(state, ok ? "phase.success" : "phase.failed", result).State;
            if (ok)
            {
                state = state.WithDesignContext(SnowballDesignContext(state.DesignContext, result));
                await _artifactRepo.StageAsync(result.Artifacts, ct);
            }
            await _stateStore.SaveSessionAsync(state.SessionId, state, ct);
            Log($"[{phaseId}] {(ok ? "success" : "failed")} (attempt {result.AttemptNumber})");
        }

        var complete = _fsm.Apply(state, "session.complete");
        if (!complete.WasRejected) state = complete.State;
        await _stateStore.SaveSessionAsync(state.SessionId, state, ct);
        return state;
    }

    private SessionState AdvanceFsm(SessionState state)
    {
        var (evt, next) = state.Status switch
        {
            SessionStatus.Planning => ("phase.ready", _graph.GetNextRunnable(state)),
            SessionStatus.Retry => ("retry.dispatch", (PhaseId?)null),
            SessionStatus.CheckpointRollback => ("rollback.to_phase", (PhaseId?)null),
            _ => ((string?)null, (PhaseId?)null)
        };
        if (evt is null) return state;
        if (state.Status == SessionStatus.Planning && next is not null)
            state = state.WithCurrentPhase(next.Value, PhaseStatus.Pending);
        var r = _fsm.Apply(state, evt);
        return r.WasRejected ? state : r.State;
    }

    private async Task<PhaseContext> BuildContext(SessionState state, PhaseId phaseId) => new()
    {
        SessionId = state.SessionId, PhaseId = phaseId, Prompt = BuildPromptTemplate(phaseId),
        UserRequest = state.InitialRequest?.Prompt,
        InputArtifacts = await _artifactRepo.ListBySessionAsync(state.SessionId),
        ModelRoute = GetModelForPhase(), BudgetRemaining = state.Profile.Budget,
        AttemptNumber = state.CurrentAttempt, CorrectionSignal = state.CorrectionSignal,
        DesignContext = state.DesignContext
    };

    private static ModelRoute GetModelForPhase() => new()
    {
        Tier = ModelTier.Fast, ProviderId = "ollama",
        ModelId = "deepseek-v4-flash:cloud", MaxOutputTokens = 8192, Temperature = 0.2
    };

    private static PromptTemplate BuildPromptTemplate(PhaseId phaseId) => new()
    {
        PhaseId = phaseId, Version = new PromptVersion("1.0.0"),
        SystemPrompt = $"You are executing the {phaseId.Value} phase of the Posit spec compiler. " +
            "Decompose the spec into components, classify each as dafny or io-shell, " +
            "select patterns from the registry, and fill the skeleton. Respond with valid JSON only.",
        OutputFormatSpec = "{ \"systemContext\": \"...\", \"components\": [...], ... }",
        ModelTier = ModelTier.Fast, Temperature = 0.2, MaxOutputTokens = 8192,
        OutputFormat = OutputFormat.Json, OutputSchemaRef = "ArchitectureContract",
        Status = PromptStatus.Active
    };

    private static DesignContext? SnowballDesignContext(DesignContext? current, PhaseResult result)
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

    /// <summary>Carapace enforcement: filenames, phantom module refs, missing components.</summary>
    private async Task<string[]> EnforceCarapace(PhaseResult result, SessionState state, CancellationToken ct)
    {
        var bundle = Deserialize<SourceCodeBundle>(result.Artifacts.PayloadJson);
        if (bundle is null) return [];
        var contract = await GetContract(state, ct);
        if (contract is null) return [];
        var errors = new List<string>();
        var names = contract.Components.Select(c => c.Name).ToHashSet();
        foreach (var f in bundle.Files)
        {
            var fn = Path.GetFileName(f.Path);
            var dir = Path.GetDirectoryName(f.Path);
            if (!contract.Components.Any(c => fn == $"{c.Name}.cs" || fn.StartsWith($"{c.Name}.")
                || fn.StartsWith($"{c.Name}Extern.") || (fn == "Wire.cs" && dir == c.Name)))
                errors.Add($"Carapace: '{f.Path}' matches no component");
        }
        foreach (var comp in contract.Components)
            if (!bundle.Files.Any(f => f.Path == $"{comp.Name}.cs" || f.Path.StartsWith($"{comp.Name}.")
                || f.Path.StartsWith($"{comp.Name}Extern.")))
                errors.Add($"Carapace: no files for '{comp.Name}'");
        foreach (var f in bundle.Files)
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(f.Content, @"_module_(\w+)"))
                if (!names.Contains(m.Groups[1].Value))
                    errors.Add($"Phantom ref: '{m.Groups[1].Value}' in '{f.Path}'");
        return [.. errors];
    }

    private async Task<ArchitectureContract?> GetContract(SessionState state, CancellationToken ct)
    {
        var a = (await _artifactRepo.GetByPhaseAsync(state.SessionId, new PhaseId("architecture"), ct))
            .FirstOrDefault();
        return a is null ? null : Deserialize<ArchitectureContract>(a.PayloadJson);
    }

    private static T? Deserialize<T>(byte[] payload) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(payload, PositJson.Options); }
        catch { return null; }
    }

    private static void Log(string msg) => Console.Error.WriteLine(msg);
}