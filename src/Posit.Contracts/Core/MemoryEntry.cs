namespace Posit.Contracts.Core;

public record MemoryEntry
{
    public required string Key { get; init; }
    public required string Content { get; init; }
    public required float[] Embedding { get; init; }
    public required float RelevanceScore { get; init; }
}