namespace Posit.Dt.Data;

/// <summary>
/// Implementation trace row. Combines dafny_results with prompts_log parse status.
/// </summary>
public sealed class PositImplTraceEntry
{
    public int PhaseAttempt { get; set; }
    public string PhaseId { get; set; } = "";
    public string? ModuleName { get; set; }
    public bool IsVerified { get; set; }
    public string? VerificationOutput { get; set; }
    public int PromptLength { get; set; }
    public int ResponseLength { get; set; }
    public string? CompilerErrors { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsDafny { get; set; }
}
