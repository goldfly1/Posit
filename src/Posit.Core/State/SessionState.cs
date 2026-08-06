using System.Text.Json.Serialization;
using Posit.Contracts.Core;

namespace Posit.Core.State;

public record SessionState
{
    public required SessionId SessionId { get; init; }
    public required ProjectId ProjectId { get; init; }
    public required ProjectProfile Profile { get; init; }
    public InitialRequest? InitialRequest { get; init; }
    public required SessionStatus Status { get; init; }
    public PhaseId? CurrentPhaseId { get; init; }
    public PhaseStatus? CurrentPhaseStatus { get; init; }
    public int CurrentAttempt { get; init; }
    public PhaseId[] CompletedPhases { get; init; } = [];
    public PhaseId[] InProgressPhases { get; init; } = [];
    public ContextSummary? LastContextSummary { get; init; }
    public CostSnapshot RunningCosts { get; init; } = CostSnapshot.Zero;

    [JsonIgnore]
    public byte[] SigningKey { get; init; } = [];

    public string[] CorrectionSignal { get; init; } = [];

    /// <summary>
    /// Number of times implementation has looped back to Architecture.
    /// Capped at 2 to prevent infinite cycling.
    /// </summary>
    public int LoopbackCount { get; init; }

    /// <summary>
    /// Accumulated design context that snowballs across phases.
    /// Each design phase adds its piece; Implementation reads the full context.
    /// </summary>
    public DesignContext? DesignContext { get; init; }

    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? LastAdvancedAt { get; init; }

    public static SessionState Create(SessionId sessionId, ProjectProfile profile, InitialRequest? initialRequest = null)
    {
        return new SessionState
        {
            SessionId = sessionId,
            ProjectId = profile.Id,
            Profile = profile,
            InitialRequest = initialRequest,
            Status = SessionStatus.Idle,
            StartedAt = DateTimeOffset.UtcNow
        };
    }

    public SessionState WithCurrentPhase(PhaseId phaseId, PhaseStatus status) => this with
    {
        CurrentPhaseId = phaseId,
        CurrentPhaseStatus = status,
        CurrentAttempt = 1,
        LastAdvancedAt = DateTimeOffset.UtcNow
    };

    public SessionState WithAttemptIncremented() => this with
    {
        CurrentAttempt = CurrentAttempt + 1,
        LastAdvancedAt = DateTimeOffset.UtcNow
    };

    public SessionState WithCompletedPhase(PhaseId phaseId) => this with
    {
        CompletedPhases = [.. CompletedPhases, phaseId],
        CurrentPhaseId = null,
        CurrentPhaseStatus = null,
        CurrentAttempt = 0
    };

    public SessionState WithCosts(CostSnapshot add) => this with { RunningCosts = RunningCosts + add };
    public SessionState WithStatus(SessionStatus status) => this with { Status = status };
    public SessionState WithAttempt(int attempt) => this with { CurrentAttempt = attempt };
    public SessionState WithCompletedPhases(PhaseId[] phases) => this with { CompletedPhases = phases };
    public SessionState WithContextSummary(ContextSummary summary) => this with { LastContextSummary = summary };
    public SessionState WithCorrectionSignal(string[] correctionSignal) => this with { CorrectionSignal = correctionSignal };
    public SessionState WithDesignContext(DesignContext? designContext) => this with { DesignContext = designContext };
}