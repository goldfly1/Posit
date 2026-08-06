namespace Posit.Contracts.Ai;

public sealed record GenerationResult
{
    public required string Text { get; init; }
    public required int InputTokens { get; init; }
    public required int OutputTokens { get; init; }
    public required decimal CostUsd { get; init; }
    public required TimeSpan Latency { get; init; }
    public bool Retryable { get; init; }
    public string? ErrorKind { get; init; }
}