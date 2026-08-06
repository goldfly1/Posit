namespace Posit.Contracts.Core;

public enum SessionStatus
{
    Idle,
    Planning,
    Active,
    Validating,
    Retry,
    CheckpointRollback,
    Recovery,
    ReviewGate,
    Paused,
    Completed,
    Aborted,
    Abandoned
}

public enum PhaseStatus
{
    Pending,
    Running,
    Success,
    Failed,
    NeedsReview,
    Aborted
}

public enum Severity
{
    Info,
    Warn,
    Error
}