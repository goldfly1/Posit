namespace Posit.Dt.Data;

/// <summary>
/// Dashboard row for a single prompt entry from posit_qa.prompts_log.
/// </summary>
public sealed class PositPromptEntry
{
    public long Id { get; set; }
    public string SessionId { get; set; } = "";
    public string PhaseId { get; set; } = "";
    public int PhaseAttempt { get; set; }
    public string? ModuleName { get; set; }
    public string AttemptKind { get; set; } = "";
    public string? ModelProvider { get; set; }
    public string? ModelId { get; set; }
    public int SystemPromptLen { get; set; }
    public int UserPromptLen { get; set; }
    public int ResponseLen { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public decimal CostUsd { get; set; }
    public long LatencyMs { get; set; }
    public string? ParseStatus { get; set; }
    public string? ParseError { get; set; }
    public DateTime CreatedAt { get; set; }

    // Full content (only populated by GetPromptDetailAsync)
    public string? SystemPrompt { get; set; }
    public string? UserPrompt { get; set; }
    public string? ResponseText { get; set; }
}
