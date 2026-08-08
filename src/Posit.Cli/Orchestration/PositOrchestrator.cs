using System.Text.Json;
using Posit.Contracts.Core;
using Posit.Contracts.Serialization;
using static Posit.Contracts.Serialization.PositJson;
using Posit.Core.Graph;
using Posit.Core.State;
using Posit.Data.Repositories;

namespace Posit.Cli.Orchestration;

/// <summary>
/// Lean pipeline orchestrator. Runs phases in dependency order,
/// handles retry/rollback via the FSM, and collects artifacts.
/// No audit, no vector memory, no cost tracking — just the spine.
/// </summary>
public sealed class PositOrchestrator
{
    private readonly FsmReducer _reducer;
    private readonly IDependencyGraphEngine _graphEngine;
    private readonly IPhaseController _phaseController;
    private readonly IReadOnlyDictionary<PhaseId, IPhase> _phases;
    private readonly Dictionary<SessionId, SessionState> _sessions = new();
    private readonly Dictionary<SessionId, List<ArtifactBundle>> _artifacts = new();
    private readonly ArtifactRepository? _artifactRepo;
    private readonly StateStore? _stateStore;

    private static readonly JsonSerializerOptions JsonOptions = Options;

    public PositOrchestrator(
        FsmReducer reducer,
        IDependencyGraphEngine graphEngine,
        IPhaseController phaseController,
        IEnumerable<IPhase> phases,
        ArtifactRepository? artifactRepo = null,
        StateStore? stateStore = null)
    {
        _reducer = reducer ?? throw new ArgumentNullException(nameof(reducer));
        _graphEngine = graphEngine ?? throw new ArgumentNullException(nameof(graphEngine));
        _phaseController = phaseController ?? throw new ArgumentNullException(nameof(phaseController));
        _phases = phases.ToDictionary(p => p.Id);
        _artifactRepo = artifactRepo;
        _stateStore = stateStore;
    }

    /// <summary>
    /// Start a new session with the given profile and request.
    /// </summary>
    public async Task<SessionId> StartSessionAsync(ProjectProfile profile, InitialRequest? request = null)
    {
        var sessionId = SessionId.New();
        var state = SessionState.Create(sessionId, profile, request);

        // Build dependency graph from phase dependencies
        var phaseIds = profile.Phases;
        // Only include dependencies that are in the profile's phase set.
        // This allows running a subset of phases (e.g., just architecture)
        // without requiring all upstream phases to be included.
        var phaseSet = phaseIds.ToHashSet();
        var deps = phaseIds
            .Select(p => _phases.TryGetValue(p, out var phase)
                ? phase.Dependencies.Where(d => phaseSet.Contains(d)).ToArray()
                : [])
            .ToArray();
        var graph = _graphEngine.Build(phaseIds, deps);

        if (_graphEngine.HasCycles(graph, out var cycle))
            throw new InvalidOperationException($"Dependency graph has cycles: {string.Join(",", cycle.Select(c => c.Value))}");

        state = state.WithDependencyGraph(graph);

        var startResult = _reducer.Apply(state, "session.start");
        if (startResult.WasRejected)
            throw new InvalidOperationException(startResult.RejectionReason);

        _sessions[sessionId] = startResult.State;
        _artifacts[sessionId] = [];

        // Audit log: session started
        await AuditRepository.LogEventAsync(sessionId.Value, "session.started", null, "info",
            new { profileId = profile.Id.Value, phaseCount = profile.Phases.Length });

        return sessionId;
    }

    /// <summary>
    /// Run the pipeline to completion (or until a gate/abort).
    /// Each iteration: find next runnable phase → execute → validate → advance.
    /// </summary>
    public async Task<SessionState> RunAsync(SessionId sessionId, CancellationToken ct = default)
    {
        if (!_sessions.TryGetValue(sessionId, out var state))
            throw new InvalidOperationException("Session not found");

        while (state.Status is SessionStatus.Planning or SessionStatus.Active or
               SessionStatus.Retry or SessionStatus.CheckpointRollback)
        {
            if (state.Status == SessionStatus.Planning)
            {
                // Find next runnable phase
                var nextPhase = _graphEngine.GetNextRunnable(state);
                if (nextPhase is null)
                {
                    // No more phases — session complete
                    var completeResult = _reducer.Apply(state, "session.complete");
                    state = completeResult.State;
                    break;
                }

                state = state.WithCurrentPhase(nextPhase.Value, PhaseStatus.Pending);
                var readyResult = _reducer.Apply(state, "phase.ready");
                if (readyResult.WasRejected)
                    throw new InvalidOperationException(readyResult.RejectionReason);
                state = readyResult.State;
            }

            if (state.Status == SessionStatus.Retry)
            {
                var retryResult = _reducer.Apply(state, "retry.dispatch");
                state = retryResult.State;
            }

            if (state.Status == SessionStatus.CheckpointRollback)
            {
                var rollbackResult = _reducer.Apply(state, "rollback.to_phase");
                state = rollbackResult.State;
            }

            if (state.Status != SessionStatus.Active)
                continue;

            // Execute the current phase
            var phaseId = state.CurrentPhaseId!.Value;
            if (!_phases.TryGetValue(phaseId, out var phase))
                throw new InvalidOperationException($"Phase '{phaseId.Value}' not registered");

            var context = BuildContext(state);
            Console.Error.WriteLine($"[Posit] === Phase '{phaseId.Value}' attempt {state.CurrentAttempt} ===");

            var phaseResult = await _phaseController.ExecuteAsync(phase, context, ct);

            // Validate the output
            if (phaseResult.Status == PhaseStatus.Success)
            {
                var validation = await phase.ValidateOutputAsync(phaseResult.Artifacts, ct);
                if (!validation.IsValid)
                {
                    Console.Error.WriteLine($"[Posit] Validation failed: {string.Join(", ", validation.Errors)}");
                    phaseResult = phaseResult with
                    {
                        Status = PhaseStatus.Failed,
                        Warnings = [..phaseResult.Warnings, ..validation.Errors]
                    };
                }
                else if (phaseId.Value == "csharp-implementation")
                {
                    var carapaceCheck = ValidateCSharpCarapace(state, phaseResult.Artifacts);
                    if (!carapaceCheck.IsValid)
                    {
                        Console.Error.WriteLine($"[Posit] Carapace enforcement failed: {string.Join(", ", carapaceCheck.Errors)}");
                        phaseResult = phaseResult with
                        {
                            Status = PhaseStatus.Failed,
                            Warnings = [..phaseResult.Warnings, ..carapaceCheck.Errors]
                        };
                    }
                }
            }

            // Route the result through the FSM
            var eventName = phaseResult.Status == PhaseStatus.Success ? "phase.success" : "phase.failed";
            var fsmResult = _reducer.Apply(state, eventName, phaseResult);

            if (fsmResult.WasRejected)
            {
                Console.Error.WriteLine($"[Posit] FSM rejected: {fsmResult.RejectionReason}");
                state = fsmResult.State with { Status = SessionStatus.Aborted };
                break;
            }

            state = fsmResult.State;

            // Store artifacts on success OR failure so debugging/tracing is possible.
            // On failure the artifact is NOT staged/snowballed; we just keep it in memory
            // for the next retry attempt.
            _artifacts[sessionId].Add(phaseResult.Artifacts);

            if (phaseResult.Status == PhaseStatus.Success)
            {
                Console.Error.WriteLine($"[Posit] Phase '{phaseId.Value}' completed. Artifacts: {_artifacts[sessionId].Count}");

                // Snowball: update DesignContext with this phase's output
                state = SnowballDesignContext(state, phaseId.Value, phaseResult.Artifacts);

                // Persist artifact to DB
                if (_artifactRepo is not null)
                {
                    try { await _artifactRepo.StageAsync(phaseResult.Artifacts, ct); }
                    catch (Exception ex) { Console.Error.WriteLine($"[Posit] Artifact persist failed (ignored): {ex.Message}"); }
                }

                // Audit log
                await AuditRepository.LogEventAsync(sessionId.Value, "phase.completed", phaseId.Value, "info",
                    new { artifactId = phaseResult.Artifacts.Id.Value, kind = phaseResult.Artifacts.Kind.ToString() }, ct);
            }
            else
            {
                Console.Error.WriteLine($"[Posit] Phase '{phaseId.Value}' failed (attempt {state.CurrentAttempt}). Warnings: {string.Join(", ", phaseResult.Warnings)}");

                if (state.Status == SessionStatus.CheckpointRollback && state.CurrentAttempt > state.Profile.MaxRetriesPerPhase)
                {
                    Console.Error.WriteLine($"[Posit] Phase '{phaseId.Value}' exhausted retries. Aborting.");
                    state = state with { Status = SessionStatus.Aborted };
                    break;
                }
            }

            _sessions[sessionId] = state;

            // Persist session state to DB
            if (_stateStore is not null)
            {
                try { await _stateStore.SaveSessionAsync(sessionId, state, ct); }
                catch (Exception ex) { Console.Error.WriteLine($"[Posit] State persist failed (ignored): {ex.Message}"); }
            }
        }

        _sessions[sessionId] = state;
        return state;
    }

    /// <summary>
    /// Resume a persisted session from its last saved state.
    /// Loads artifacts from the repository so downstream phases have inputs.
    /// </summary>
    public async Task<bool> ResumeAsync(SessionId sessionId, CancellationToken ct = default)
    {
        if (_stateStore is null)
        {
            Console.Error.WriteLine("[Posit] Resume requires a StateStore (DB not available).");
            return false;
        }

        var state = await _stateStore.LoadSessionAsync(sessionId, ct);
        if (state is null)
        {
            Console.Error.WriteLine($"[Posit] Session {sessionId.Value} not found in state store.");
            return false;
        }

        _sessions[sessionId] = state;
        _artifacts[sessionId] = [];

        if (_artifactRepo is not null)
        {
            var artifacts = await _artifactRepo.ListBySessionAsync(sessionId, ct);
            _artifacts[sessionId] = [.. artifacts];
            Console.Error.WriteLine($"[Posit] Resume: loaded {artifacts.Length} artifacts");
        }

        Console.Error.WriteLine($"[Posit] Resuming session {sessionId.Value} at status {state.Status}, phase {state.CurrentPhaseId?.Value ?? "(none)"}, attempt {state.CurrentAttempt}");
        return true;
    }

    /// <summary>
    /// Get all artifacts produced by a session.
    /// </summary>
    public IReadOnlyList<ArtifactBundle> GetArtifacts(SessionId sessionId)
        => _artifacts.TryGetValue(sessionId, out var list) ? list : [];

    /// <summary>
    /// Get the current session state.
    /// </summary>
    public SessionState? GetState(SessionId sessionId)
        => _sessions.TryGetValue(sessionId, out var state) ? state : null;

    /// <summary>
    /// Load a session and its artifacts from the repository without running.
    /// Used by CLI resume/status commands.
    /// </summary>
    public async Task LoadSessionArtifactsAsync(SessionId sessionId, CancellationToken ct = default)
    {
        if (_stateStore is null || _artifactRepo is null)
            return;

        var state = await _stateStore.LoadSessionAsync(sessionId, ct);
        if (state is null)
            return;

        _sessions[sessionId] = state;
        var artifacts = await _artifactRepo.ListBySessionAsync(sessionId, ct);
        _artifacts[sessionId] = [.. artifacts];
    }

    private PhaseContext BuildContext(SessionState state)
    {
        var phaseId = state.CurrentPhaseId!.Value;
        var artifacts = _artifacts[state.SessionId].ToArray();

        return new PhaseContext
        {
            SessionId = state.SessionId,
            PhaseId = phaseId,
            Prompt = new PromptTemplate
            {
                PhaseId = phaseId,
                Version = new PromptVersion("1.0"),
                SystemPrompt = "", // Phase loads its own prompt
                OutputFormatSpec = "json",
                ModelTier = ModelTier.Standard,
                Temperature = 0.2,
                MaxOutputTokens = 64000,
                OutputFormat = OutputFormat.Json,
                OutputSchemaRef = "",
                Status = PromptStatus.Active
            },
            UserRequest = state.InitialRequest?.Prompt,
            InputArtifacts = artifacts,
            ModelRoute = new ModelRoute
            {
                Tier = ModelTier.Standard,
                ProviderId = "ollama",
                ModelId = GetModelForPhase(phaseId),
                MaxOutputTokens = 64000,
                Temperature = 0.2
            },
            BudgetRemaining = state.Profile.Budget,
            AttemptNumber = state.CurrentAttempt,
            CorrectionSignal = state.CorrectionSignal,
            DesignContext = state.DesignContext
        };
    }

    /// <summary>
    /// Map phase IDs to model IDs. All through Ollama on 11434.
    /// </summary>
    private static string GetModelForPhase(PhaseId phaseId) => phaseId.Value switch
    {
        "ideation" => "deepseek-v4-pro:cloud",
        "architecture" => "deepseek-v4-pro:cloud",
        "design-review" => "kimi-k2.7-code:cloud",
        "dafny-contracts" => "ollama", // deterministic — no model call
        "implementation" => "deepseek-v4-pro:cloud", // legacy alias
        "dafny-implementation" => "deepseek-v4-pro:cloud", // Pass 1: Dafny bodies
        "csharp-implementation" => "glm-5.2:cloud", // Pass 2: C# shells
        "qa" => "glm-5.2:cloud",
        "documentation" => "deepseek-v4-pro:cloud",
        _ => "glm-5.2:cloud" // default
    };

    /// <summary>
    /// Carapace enforcement for C# Implementation output. Every generated file must
    /// live under an authorized component directory from the Architecture contract.
    /// New directories invented by the model (e.g. "MigrationRunner") are rejected.
    /// A component directory may not contain another authorized component as a
    /// subdirectory — each component is a top-level directory only.
    /// </summary>
    private static ValidationResult ValidateCSharpCarapace(SessionState state, ArtifactBundle artifact)
    {
        var errors = new List<string>();
        var allowedRoots = new HashSet<string>(
            (state.DesignContext?.Components ?? [])
            .Select(c => c.Name),
            StringComparer.OrdinalIgnoreCase);

        // Also allow a small shared utilities directory if explicitly declared.
        allowedRoots.Add("Shared");

        try
        {
            var json = System.Text.Encoding.UTF8.GetString(artifact.PayloadJson);
            var bundle = JsonSerializer.Deserialize<SourceCodeBundle>(json, Options);
            if (bundle?.Files is null)
                return new ValidationResult { IsValid = true };

            foreach (var file in bundle.Files)
            {
                var rel = file.Path?.Replace('\\', '/').TrimStart('/') ?? "";
                var parts = rel.Split('/').Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
                if (parts.Length == 0)
                    continue;

                var firstDir = parts[0];
                if (!allowedRoots.Contains(firstDir))
                {
                    errors.Add($"carapace.off_list.directory: '{firstDir}' is not an authorized component directory ({string.Join(", ", allowedRoots.OrderBy(n => n))})");
                    continue;
                }

                // C# Implementation inlays FUNCTION only. Project/solution/MSBuild files
                // are prefabricated by the verifier from the architecture contract.
                var lastPart = parts[^1];
                var ext = Path.GetExtension(lastPart).ToLowerInvariant();
                if (ext is ".csproj" or ".sln" or ".props" or ".targets")
                {
                    errors.Add($"carapace.prefab_file: '{rel}' .csproj/.sln/.props/.targets are prefabricated, not emitted by C# Implementation");
                }

                // Project file bodies smuggled inside .cs files are also rejected.
                if (file.Content?.TrimStart().StartsWith("\u003cProject", StringComparison.OrdinalIgnoreCase) == true)
                {
                    errors.Add($"carapace.prefab_file: '{rel}' content is a project file body, not C# source");
                }

                // No other authorized component may appear as an intermediate directory.
                for (int i = 1; i < parts.Length - 1; i++)
                {
                    var dir = parts[i];
                    if (allowedRoots.Contains(dir) && !string.Equals(dir, firstDir, StringComparison.OrdinalIgnoreCase))
                    {
                        errors.Add($"carapace.nested_component: '{rel}' nests component '{dir}' inside '{firstDir}'; each component must be a top-level directory");
                        break;
                    }
                }
            }

            var presentRoots = new HashSet<string>(
                bundle.Files
                    .Where(f => !string.IsNullOrWhiteSpace(f.Path))
                    .Select(f => f.Path!.Replace('\\', '/').TrimStart('/').Split('/')[0]),
                StringComparer.OrdinalIgnoreCase);

            // Components that require a C# stub cap (io-shell or Dafny components with {:extern} stubs)
            // must be represented by at least one file. Pure Dafny components without stubs are supplied
            // by the verifier from the DafnyVerification artifact, not by C# Implementation.
            var componentsNeedingCSharp = (state.DesignContext?.Components ?? [])
                .Where(c => c.Classification == ModuleClassification.IoShell || (c.StubNames?.Length > 0))
                .Select(c => c.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var component in componentsNeedingCSharp)
            {
                if (!presentRoots.Contains(component))
                {
                    errors.Add($"carapace.missing_component: '{component}' requires a C# stub cap but has no files in the C# Implementation output");
                }
            }
        }
        catch (Exception ex)
        {
            errors.Add($"carapace.validation.error: {ex.Message}");
        }

        return new ValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors.ToArray()
        };
    }

    /// <summary>
    /// Snowball: update DesignContext with each phase's output.
    /// Architecture populates components. Dafny Contracts adds contract entries.
    /// Dafny Implementation adds verification results. This is the compact
    /// structured context that downstream phases read instead of raw JSON artifacts.
    /// </summary>
    private static SessionState SnowballDesignContext(SessionState state, string phaseId, ArtifactBundle artifact)
    {
        var current = state.DesignContext ?? new DesignContext();

        try
        {
            var json = System.Text.Encoding.UTF8.GetString(artifact.PayloadJson);
            var opts = Options;

            if (phaseId == "architecture" && artifact.Kind == ArtifactKind.ArchitectureContract)
            {
                var arch = JsonSerializer.Deserialize<ArchitectureContract>(json, opts);
                if (arch?.Components is not null)
                {
                    current = current with
                    {
                        Components = arch.Components.Select(c => new DesignComponent(
                            c.Id, c.Name, c.Responsibility, c.Tech,
                            c.PublicSurface, c.Internals, c.Dependencies)
                        {
                            Classification = c.Classification,
                            PatternName = c.PatternName,
                            StubNames = c.StubNames,
                            DafnyContractPath = c.DafnyContractPath,
                            TestCases = c.TestCases?.Select(tc => new DesignTestCase(
                                tc.Id, tc.Name, tc.TargetType, tc.Description, tc.ExpectedBehavior)).ToArray() ?? []
                        }).ToArray(),
                        DataStores = arch.DataStores?.Select(ds => new DesignDataStore(
                            ds.Id, ds.Name, ds.Kind.ToString(), ds.Schema)).ToArray() ?? [],
                        DeploymentTopology = arch.DeploymentTopology,
                        QualityAttributes = arch.QualityAttributes?.Select(qa =>
                            new DesignQualityAttribute(qa.Attribute, qa.Target)).ToArray() ?? []
                    };
                    Console.Error.WriteLine($"[Posit] Snowball: DesignContext updated with {current.Components.Length} components from Architecture");
                }
            }
            else if (phaseId == "dafny-contracts" && artifact.Kind == ArtifactKind.DafnyContract)
            {
                var contracts = JsonSerializer.Deserialize<DafnyContractResult[]>(json, opts);
                if (contracts is not null)
                {
                    current = current with
                    {
                        DafnyContracts = contracts.Select(c => new DafnyContractEntry
                        {
                            ModuleName = c.ModuleName,
                            DafnySource = c.DafnySource,
                            DafnyPath = !string.IsNullOrWhiteSpace(c.DafnyPath) ? c.DafnyPath : Z3Runner.GetDafnyStagingPath($"skeleton-{c.ModuleName}"),
                            IsVerified = c.IsVerified,
                            VerificationOutput = c.VerificationOutput
                        }).ToArray()
                    };
                    Console.Error.WriteLine($"[Posit] Snowball: DesignContext updated with {current.DafnyContracts.Length} Dafny contracts");
                }
            }
            else if (phaseId == "dafny-implementation" && artifact.Kind == ArtifactKind.DafnyVerification)
            {
                var results = JsonSerializer.Deserialize<DafnyVerificationResult[]>(json, opts);
                if (results is not null)
                {
                    // Update DafnyContracts with verification + translation results
                    var existing = current.DafnyContracts?
                        .GroupBy(c => c.ModuleName)
                        .ToDictionary(g => g.Key, g => g.Last()) ?? new();
                    foreach (var r in results)
                    {
                        existing[r.ModuleName] = new DafnyContractEntry
                        {
                            ModuleName = r.ModuleName,
                            DafnySource = r.DafnySource,
                            DafnyPath = r.DafnyPath,
                            IsVerified = r.IsVerified,
                            VerificationOutput = r.VerificationOutput,
                            TranslatedCSharpPath = r.TranslatedCSharpPath
                        };
                    }
                    current = current with { DafnyContracts = existing.Values.ToArray() };
                    Console.Error.WriteLine($"[Posit] Snowball: DesignContext updated with {results.Length} Dafny verification results");
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Posit] Snowball failed for {phaseId} (ignored): {ex.Message}");
        }

        return state.WithDesignContext(current);
    }
}