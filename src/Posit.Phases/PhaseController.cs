using Posit.Contracts.Core;

namespace Posit.Phases;

public interface IPhaseController
{
    Task<PhaseResult> ExecuteAsync(IPhase phase, PhaseContext context, CancellationToken ct);
}

public sealed class PhaseController : IPhaseController
{
    public async Task<PhaseResult> ExecuteAsync(IPhase phase, PhaseContext context, CancellationToken ct)
    {
        await phase.InitializeAsync(context, ct);
        return await phase.ExecuteAsync(context, ct);
    }
}