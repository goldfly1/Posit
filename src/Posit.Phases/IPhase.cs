namespace Posit.Phases;

/// <summary>
/// Contract for a pipeline phase. Each phase initializes, executes,
/// and validates its output. The PhaseController dispatches by PhaseId.
/// </summary>
public interface IPhase
{
    PhaseId Id { get; }
    string Name { get; }
    PhaseId[] Dependencies { get; }
    ArtifactSchema OutputSchema { get; }

    Task InitializeAsync(PhaseContext context, CancellationToken ct = default);
    Task<PhaseResult> ExecuteAsync(PhaseContext context, CancellationToken ct = default);
    ValidationResult ValidateOutput(PhaseResult result);
}