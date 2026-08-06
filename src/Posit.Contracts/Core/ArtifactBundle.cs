namespace Posit.Contracts.Core;

public record ArtifactBundle
{
    public required ArtifactId Id { get; init; }
    public required SessionId SessionId { get; init; }
    public required PhaseId SourcePhase { get; init; }
    public required string SchemaVersion { get; init; }
    public required ArtifactKind Kind { get; init; }
    public ArtifactReference[] References { get; init; } = [];
    public Checksum? Checksum { get; init; }
    public required DateTimeOffset ProducedAt { get; init; }
    public required byte[] PayloadJson { get; init; }
}

public record ArtifactReference(ArtifactId Id, ArtifactKind Kind, string SchemaVersion);