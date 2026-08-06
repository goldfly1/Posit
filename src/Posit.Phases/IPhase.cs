using Posit.Contracts.Core;

namespace Posit.Phases;

/// <summary>
/// Contract every pipeline phase implements. Each phase is a context-reset
/// boundary: it receives a PhaseContext with accumulated design context and
/// prior artifacts, executes its work, and produces a PhaseResult with an
/// ArtifactBundle. The FSM validates the output before advancing.
/// </summary>
public interface IPhase
{
    PhaseId Id { get; }
    PhaseName Name { get; }
    PhaseId[] Dependencies { get; }
    ArtifactSchema OutputSchema { get; }

    Task InitializeAsync(PhaseContext context, CancellationToken ct);
    Task<PhaseResult> ExecuteAsync(PhaseContext context, CancellationToken ct);
    Task<ValidationResult> ValidateOutputAsync(ArtifactBundle output, CancellationToken ct);
    Task<ValidationResult> ValidateOutputAsync(ArtifactBundle output, PhaseContext context, CancellationToken ct)
        => ValidateOutputAsync(output, ct);
}