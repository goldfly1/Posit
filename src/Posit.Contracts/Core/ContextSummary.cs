namespace Posit.Contracts.Core;

public record ContextSummary
{
    public required ProjectId ProjectId { get; init; }
    public required PhaseId LastCompletedPhase { get; init; }
    public required string CompressedDecisions { get; init; }
    public required ArtifactReference[] CriticalArtifacts { get; init; }
    public required ModelRoute PreferredRoute { get; init; }
    public required CostSnapshot RunningTotal { get; init; }
}