using Posit.Contracts.Core;

namespace Posit.Core.State;

public record ProjectProfile
{
    public required ProjectId Id { get; init; }
    public required string Name { get; init; }
    public required PhaseId[] Phases { get; init; }
    public int MaxRetriesPerPhase { get; init; } = 3;
    public required BudgetRemaining Budget { get; init; }
    public required ApprovalConfig Approvals { get; init; }
}

public record ApprovalConfig
{
    public required GateTimeoutPolicy TimeoutPolicy { get; init; }
    public required TimeSpan GateTimeout { get; init; }
}

public record InitialRequest
{
    public required string Prompt { get; init; }
    public required string Language { get; init; }
    public required string Framework { get; init; }
}