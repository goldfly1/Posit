namespace Posit.Contracts.Core;

public record PromptTemplate
{
    public required PhaseId PhaseId { get; init; }
    public required PromptVersion Version { get; init; }
    public required string SystemPrompt { get; init; }
    public string? FewShotExamples { get; init; }
    public required string OutputFormatSpec { get; init; }
    public required ModelTier ModelTier { get; init; }
    public required double Temperature { get; init; }
    public required int MaxOutputTokens { get; init; }
    public required OutputFormat OutputFormat { get; init; }
    public required string OutputSchemaRef { get; init; }
    public required PromptStatus Status { get; init; }
    public PromptVersion? SupersededBy { get; init; }
}