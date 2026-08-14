namespace Posit.Phases;

/// <summary>
/// Dispatches execution to the correct phase by PhaseId.
/// The orchestrator registers phases, then calls ExecuteAsync.
/// </summary>
public sealed class PhaseController
{
    private readonly Dictionary<PhaseId, IPhase> _phases = new();

    public void Register(IPhase phase) => _phases[phase.Id] = phase;

    public IPhase? Resolve(PhaseId id) =>
        _phases.TryGetValue(id, out var p) ? p : null;

    public Task<PhaseResult> ExecuteAsync(PhaseContext context, CancellationToken ct = default)
    {
        if (!_phases.TryGetValue(context.PhaseId, out var phase))
            throw new InvalidOperationException($"No phase registered for '{context.PhaseId}'");
        return phase.ExecuteAsync(context, ct);
    }
}