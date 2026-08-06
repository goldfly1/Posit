namespace Posit.Contracts.Core;

public record DependencyGraph
{
    public required PhaseId[] PhaseIds { get; init; }
    public required PhaseId[][] Adjacency { get; init; }
    public required int[] Priorities { get; init; }
}