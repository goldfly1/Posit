using System.Text.Json;
using System.Text.Json.Serialization;
using Posit.Contracts.Core;
using Posit.Core.Graph;
using Posit.Core.State;

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

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public PositOrchestrator(
        FsmReducer reducer,
        IDependencyGraphEngine graphEngine,
        IPhaseController phaseController,
        IEnumerable<IPhase> phases)
    {
        _reducer = reducer ?? throw new ArgumentNullException(nameof(reducer));
        _graphEngine = graphEngine ?? throw new ArgumentNullException(nameof(graphEngine));
        _phaseController = phaseController ?? throw new ArgumentNullException(nameof(phaseController));
        _phases = phases.ToDictionary(p => p.Id);
    }

    /// <summary>
    /// Start a new session with the given profile and request.
    /// </summary>
    public SessionId StartSession(ProjectProfile profile, InitialRequest? request = null)
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

            // Store artifacts on success
            if (phaseResult.Status == PhaseStatus.Success)
            {
                _artifacts[sessionId].Add(phaseResult.Artifacts);
                Console.Error.WriteLine($"[Posit] Phase '{phaseId.Value}' completed. Artifacts: {_artifacts[sessionId].Count}");
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
        }

        _sessions[sessionId] = state;
        return state;
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
                MaxOutputTokens = 16000,
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
                MaxOutputTokens = 16000,
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
        "design-review" => "kimi-2.7-code:cloud",
        "dafny-contracts" => "ollama", // deterministic — no model call
        "implementation" => "deepseek-v4-pro:cloud", // Pass 1: Dafny bodies
        "csharp-implementation" => "glm-5.2:cloud", // Pass 2: C# shells
        "qa" => "glm-5.2:cloud",
        "documentation" => "deepseek-v4-pro:cloud",
        _ => "glm-5.2:cloud" // default
    };
}