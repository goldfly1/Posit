using Posit.Tools;

namespace Posit.Cli.Orchestration;

using Posit.Contracts.Artifacts;
using Posit.Core.State;

/// <summary>
/// Pipeline orchestrator. Runs the phase loop: find next runnable phase from
/// the dependency graph, execute via PhaseController, validate output, route
/// through FsmReducer, snowball DesignContext, persist artifacts.
/// </summary>
public sealed class PositOrchestrator(PhaseController controller, FsmReducer fsm,
    IDependencyGraphEngine graph, ArtifactRepository artifactRepo, StateStore stateStore,
    PatternRegistry? registry = null)
{
    private readonly PhaseController _controller = controller;
    private readonly FsmReducer _fsm = fsm;
    private readonly IDependencyGraphEngine _graph = graph;
    private readonly ArtifactRepository _artifactRepo = artifactRepo;
    private readonly StateStore _stateStore = stateStore;
    private readonly PatternRegistry? _registry = registry;

    public async Task<SessionState> RunAsync(SessionState state, CancellationToken ct = default)
    {
        if (state.Status == SessionStatus.Idle)
        {
            var start = _fsm.Apply(state, "session.start");
            if (start.WasRejected) { Log($"Rejected: {start.RejectionReason}"); return state; }
            state = start.State;
        }

        var seenFailures = new Dictionary<PhaseId, int>();
        const int maxSamePhaseFailures = 10;

        while (true)
        {
            if (state.Status is SessionStatus.Planning or SessionStatus.Retry or SessionStatus.CheckpointRollback)
            {
                state = AdvanceFsm(state);
                if (state.Status != SessionStatus.Active) break;
            }
            if (state.Status != SessionStatus.Active) break;

            var phaseId = state.CurrentPhaseId!.Value;

            // Circuit breaker: abort if the same phase fails too many times (rollback loop)
            if (state.CurrentAttempt == 1 && seenFailures.TryGetValue(phaseId, out var c) && c >= maxSamePhaseFailures)
            {
                Log($"[{phaseId}] ABORT: {c} failures on same phase (rollback loop)");
                state = _fsm.Apply(state, "session.abort").State;
                break;
            }

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

            // Post-Dafny type chain check: after dafny-implementation, before C# impl.
            // Real C# types exist now. If the chain breaks, kick back to Architecture.
            if (validation.IsValid && phaseId.Value == "dafny-implementation")
            {
                var chainErrors = await CheckTypeChain(result, state, ct);
                if (chainErrors.Count > 0)
                {
                    var msgs = TypeChainChecker.FormatErrors(chainErrors);
                    validation = new ValidationResult { IsValid = false, Errors = [msgs] };
                    result = result with { Status = PhaseStatus.Failed, Warnings = [msgs] };
                }
            }

            var ok = validation.IsValid;
            state = _fsm.Apply(state, ok ? "phase.success" : "phase.failed", result).State;
            if (ok)
            {
                state = state.WithDesignContext(DesignContextSnowballer.Snowball(state.DesignContext, result));
                await _artifactRepo.StageAsync(result.Artifacts, ct);
            }
            await _stateStore.SaveSessionAsync(state.SessionId, state, ct);

            if (ok)
            {
                Log($"[{phaseId}] success (attempt {result.AttemptNumber})");
                seenFailures.Remove(phaseId);
            }
            else
            {
                seenFailures[phaseId] = (seenFailures.TryGetValue(phaseId, out var fc) ? fc : 0) + 1;
                Log($"[{phaseId}] failed (attempt {result.AttemptNumber})");
                foreach (var w in result.Warnings)
                    Log($"  → {w}");
            }
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
        SessionId = state.SessionId, PhaseId = phaseId,
        Prompt = PromptBuilder.Build(phaseId, _registry),
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
            var dir = Path.GetDirectoryName(f.Path)?.Replace('\\', '/');
            if (!contract.Components.Any(c => fn == $"{c.Name}.cs" || fn.StartsWith($"{c.Name}.")
                || fn.StartsWith($"{c.Name}Extern.") || (fn == "Wire.cs" && dir == c.Name)))
                errors.Add($"Carapace: '{f.Path}' matches no component");
        }
        foreach (var comp in contract.Components)
        {
            var prefix = $"{comp.Name}/";
            if (!bundle.Files.Any(f => f.Path.StartsWith(prefix)))
                errors.Add($"Carapace: no files for '{comp.Name}'");
        }
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

    /// <summary>
    /// Post-Dafny type chain check. Reads scanned C# signatures from the Dafny
    /// Implementation result and checks that consecutive connections have
    /// compatible types. Returns errors if the chain breaks.
    /// </summary>
    private async Task<List<TypeChainError>> CheckTypeChain(PhaseResult result, SessionState state, CancellationToken ct)
    {
        var contract = await GetContract(state, ct);
        if (contract is null) return [];

        // The Dafny implementation result contains DafnyVerificationResult[] with
        // TranslatedCSharpPath for each component. Scan those files.
        var scannedSigs = new Dictionary<string, List<CsMethodSignature>>();
        var dafnyResults = Deserialize<DafnyVerificationResult[]>(result.Artifacts.PayloadJson);
        if (dafnyResults is null) return [];

        foreach (var dr in dafnyResults)
        {
            if (dr.IsVerified && !string.IsNullOrWhiteSpace(dr.TranslatedCSharpPath) && File.Exists(dr.TranslatedCSharpPath))
            {
                var content = await File.ReadAllTextAsync(dr.TranslatedCSharpPath, ct);
                var sigs = TranslatedCSharpScanner.ScanContent(content);
                if (sigs.Count > 0)
                    scannedSigs[dr.ModuleName] = sigs;
            }
        }

        return TypeChainChecker.Check(contract, scannedSigs);
    }

    private static T? Deserialize<T>(byte[] payload) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(payload, PositJson.Options); }
        catch { return null; }
    }

    private static void Log(string msg) => Console.Error.WriteLine(msg);
}