using System.Text.Json.Serialization;

namespace Posit.Contracts.Core;

public record PhaseContext
{
    public required SessionId SessionId { get; init; }
    public required PhaseId PhaseId { get; init; }
    public required PromptTemplate Prompt { get; init; }
    public string? UserRequest { get; init; }
    public string? InheritedDecisions { get; init; }
    public ArtifactBundle[] InputArtifacts { get; init; } = [];
    public MemoryEntry[] SemanticMemories { get; init; } = [];
    public required ModelRoute ModelRoute { get; init; }
    public required BudgetRemaining BudgetRemaining { get; init; }
    public int AttemptNumber { get; init; }
    public string[] CorrectionSignal { get; init; } = [];

    /// <summary>
    /// Accumulated design context from prior phases. Each design phase
    /// (Architecture, API Definition, Pseudocode) adds its piece. Implementation
    /// reads the full accumulated context to avoid losing key decisions.
    /// </summary>
    public DesignContext? DesignContext { get; init; }

    [JsonIgnore]
    public CancellationToken CancellationToken { get; init; }

    public PhaseContext WithAttempt(int n) => this with { AttemptNumber = n };
    public PhaseContext WithCorrectionSignal(string[] errors) => this with { CorrectionSignal = errors };
}