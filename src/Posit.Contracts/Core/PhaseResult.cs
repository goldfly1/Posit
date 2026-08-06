namespace Posit.Contracts.Core;

public record PhaseResult
{
    public required PhaseId PhaseId { get; init; }
    public required PhaseStatus Status { get; init; }
    public required ArtifactBundle Artifacts { get; init; }
    public required CostSnapshot Costs { get; init; }
    public ContextSummary? InheritedContext { get; init; }
    public string[] Warnings { get; init; } = [];
    public int AttemptNumber { get; init; }

    /// <summary>
    /// When true, the orchestrator should skip in-phase retries and go directly
    /// to checkpoint rollback. Used by QA when build/test fails — the code being
    /// tested was produced by Implementation, so retrying QA can't fix it.
    /// </summary>
    public bool ForceRollback { get; init; }

    /// <summary>
    /// Raw model output text for audit/debugging.
    /// </summary>
    public string? RawOutput { get; init; }
}