using Posit.Contracts.Core;

namespace Posit.Core.State;

public sealed record FsmTransitionResult(
    SessionState State,
    string[] Events,
    bool WasRejected,
    string? RejectionReason = null);

/// <summary>
/// Deterministic FSM reducer for the Posit pipeline.
/// Inherits Shepherd's state model and escalation chain.
/// Handles retry, rollback, and correction signal routing.
/// </summary>
public sealed class FsmReducer
{
    public FsmTransitionResult Apply(SessionState state, string eventName, PhaseResult? result = null)
    {
        try
        {
            return eventName switch
            {
                "session.start" => ApplySessionStart(state),
                "phase.ready" => ApplyPhaseReady(state),
                "phase.output_produced" => ApplyPhaseOutputProduced(state),
                "phase.success" => ApplyPhaseSuccess(state, result),
                "phase.failed" => ApplyPhaseFailed(state, result),
                "retry.dispatch" => ApplyRetryDispatch(state),
                "rollback.to_phase" => ApplyRollbackToPhase(state),
                "rollback.to_architecture" => ApplyRollbackToArchitecture(state),
                "session.complete" => ApplySessionComplete(state),
                "session.abort" => ApplySessionAbort(state),
                _ => Reject(state, eventName, $"Unknown event: {eventName}")
            };
        }
        catch (Exception ex)
        {
            return Reject(state, eventName, $"Reducer exception: {ex.Message}");
        }
    }

    private FsmTransitionResult ApplySessionStart(SessionState state)
    {
        if (state.Status != SessionStatus.Idle)
            return Reject(state, "session.start", "Session already active");

        var newState = state with
        {
            Status = SessionStatus.Planning,
            StartedAt = DateTimeOffset.UtcNow
        };

        return Ok(newState, ["session.started"]);
    }

    private FsmTransitionResult ApplyPhaseReady(SessionState state)
    {
        if (state.Status != SessionStatus.Planning)
            return Reject(state, "phase.ready", "Invalid event for state");

        // The next phase to run is determined by the dependency graph
        // The orchestrator sets CurrentPhaseId before dispatching this event
        if (state.CurrentPhaseId is null)
            return Reject(state, "phase.ready", "No current phase set");

        var newState = state with
        {
            Status = SessionStatus.Active,
            CurrentPhaseStatus = PhaseStatus.Running,
            LastAdvancedAt = DateTimeOffset.UtcNow
        };

        return Ok(newState, ["phase.started"]);
    }

    private FsmTransitionResult ApplyPhaseOutputProduced(SessionState state)
    {
        if (state.Status != SessionStatus.Active)
            return Reject(state, "phase.output_produced", "Invalid event for state");

        var newState = state with { Status = SessionStatus.Validating };
        return Ok(newState, ["phase.output_produced"]);
    }

    private FsmTransitionResult ApplyPhaseSuccess(SessionState state, PhaseResult? result)
    {
        if (state.Status is not (SessionStatus.Active or SessionStatus.Validating))
            return Reject(state, "phase.success", "Invalid event for state");

        var phaseId = state.CurrentPhaseId!.Value;
        var newState = state
            .WithCompletedPhase(phaseId)
            .WithStatus(SessionStatus.Planning);

        if (result is not null)
        {
            newState = newState.WithCosts(result.Costs);
        }

        return Ok(newState, ["phase.completed", "validation.succeeded"]);
    }

    private FsmTransitionResult ApplyPhaseFailed(SessionState state, PhaseResult? result)
    {
        if (state.Status is not (SessionStatus.Active or SessionStatus.Validating))
            return Reject(state, "phase.failed", "Invalid event for state");

        var attempt = state.CurrentAttempt;
        var errors = result?.Warnings ?? [];

        if (attempt <= state.Profile.MaxRetriesPerPhase)
        {
            var newState = state
                .WithStatus(SessionStatus.Retry)
                .WithCorrectionSignal(errors);
            return Ok(newState, ["phase.retry_requested"]);
        }

        var rollbackState = state
            .WithStatus(SessionStatus.CheckpointRollback)
            .WithCorrectionSignal(errors);
        return Ok(rollbackState, ["phase.checkpoint_rollback"]);
    }

    private FsmTransitionResult ApplyRetryDispatch(SessionState state)
    {
        if (state.Status != SessionStatus.Retry)
            return Reject(state, "retry.dispatch", "Invalid event for state");

        var newState = state
            .WithAttemptIncremented()
            .WithStatus(SessionStatus.Active);

        return Ok(newState, ["phase.retry_started"]);
    }

    private FsmTransitionResult ApplyRollbackToPhase(SessionState state)
    {
        if (state.Status != SessionStatus.CheckpointRollback)
            return Reject(state, "rollback.to_phase", "Invalid event for state");

        var newState = state
            .WithStatus(SessionStatus.Active)
            .WithAttempt(1);

        return Ok(newState, ["rollback.completed"]);
    }

    /// <summary>
    /// Rollback to Architecture phase. Used when Dafny Contracts skeleton
    /// verification fails and the correction signal needs to go back to the
    /// architect. Removes Architecture from completed phases so the dependency
    /// graph re-schedules it. Increments LoopbackCount (max 2).
    /// </summary>
    private FsmTransitionResult ApplyRollbackToArchitecture(SessionState state)
    {
        if (state.Status != SessionStatus.CheckpointRollback)
            return Reject(state, "rollback.to_architecture", "Invalid event for state");

        if (state.LoopbackCount >= 2)
        {
            // Exhausted loopbacks — don't roll back, let the module downgrade to io-shell
            var newState = state
                .WithStatus(SessionStatus.Active)
                .WithAttempt(1);
            return Ok(newState, ["rollback.exhausted", "module.downgraded"]);
        }

        // Remove architecture from completed phases so it gets re-scheduled
        var newCompleted = state.CompletedPhases
            .Where(p => p.Value != "architecture")
            .Where(p => p.Value != "dafny-contracts") // dafny-contracts too — it depends on architecture
            .ToArray();

        var rollbackState = state with
        {
            Status = SessionStatus.Planning,
            CompletedPhases = newCompleted,
            CurrentPhaseId = null,
            CurrentPhaseStatus = null,
            CurrentAttempt = 0,
            LoopbackCount = state.LoopbackCount + 1
        };

        return Ok(rollbackState, ["rollback.to_architecture", "phase.checkpoint_rollback"]);
    }

    private FsmTransitionResult ApplySessionComplete(SessionState state)
    {
        var newState = state with { Status = SessionStatus.Completed };
        return Ok(newState, ["session.completed"]);
    }

    private FsmTransitionResult ApplySessionAbort(SessionState state)
    {
        var newState = state with { Status = SessionStatus.Aborted };
        return Ok(newState, ["session.aborted"]);
    }

    private static FsmTransitionResult Ok(SessionState state, string[] events) =>
        new(state, events, false);

    private static FsmTransitionResult Reject(SessionState state, string eventName, string reason) =>
        new(state, [], true, reason);
}