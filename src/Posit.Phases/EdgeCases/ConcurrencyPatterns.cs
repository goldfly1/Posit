namespace Posit.Phases.EdgeCases;

/// <summary>
/// Edge case patterns covering concurrency and multi-threading hazards.
/// </summary>
public static class ConcurrencyPatterns
{
    /// <summary>
    /// Gets the full set of concurrency edge case patterns.
    /// </summary>
    public static EdgeCasePattern[] All =>
    [
        new("Concurrency", "RaceConditionTwoConcurrentWrites",
            "Two concurrent writes target the same resource, producing a lost update or interleaved state.",
            "Issue two parallel writes to the same key and verify the final state is consistent and atomic."),

        new("Concurrency", "DeadlockOppositeLockOrder",
            "Two threads lock resource A then B, and B then A, producing a deadlock.",
            "Lock two resources in opposite orders on two threads and verify no deadlock (e.g. via timeout or lock ordering)."),

        new("Concurrency", "PartialFailureMidBatch",
            "One of N operations in a batch fails midway, leaving partial state behind.",
            "Fail the Kth operation in a batch of N and verify rollback, compensation, or idempotent retry restores consistency."),

        new("Concurrency", "RetryStorm",
            "A transient failure triggers excessive retries, amplifying load on an already-failing system.",
            "Induce a transient failure and verify retry policy respects max attempts and exponential backoff."),

        new("Concurrency", "TimeoutExceedsDeadline",
            "An operation runs longer than its configured deadline, blocking callers or leaking resources.",
            "Configure a short deadline and a slow operation, then verify the operation aborts and releases resources on timeout."),

        new("Concurrency", "CancellationTokenRespected",
            "A long-running operation ignores cancellation, leaving orphaned work after the caller cancels.",
            "Cancel mid-operation and verify the operation observes the token and exits promptly without orphaned tasks."),

        new("Concurrency", "IdempotencySameRequestTwice",
            "The same request issued twice (due to retry) produces a duplicated side effect.",
            "Send an identical request twice and verify the effect occurs exactly once (idempotency key or dedup)."),

        new("Concurrency", "BackgroundTaskLifecycle",
            "A background task is started, shut down, and crash-recovered across host restarts.",
            "Kill the host mid-task and verify on restart the task is detected, resumed, or safely discarded."),

        new("Concurrency", "SharedMutableStateThreadSafety",
            "A shared mutable field is read and written concurrently without synchronization.",
            "Hammer a shared counter from N threads and verify the final value equals the expected sum (no torn reads)."),

        new("Concurrency", "ConcurrentCollectionModificationDuringIteration",
            "A collection is modified while being iterated, throwing or producing undefined behavior.",
            "Iterate and concurrently add/remove from a collection and verify a snapshot, lock, or concurrent collection is used."),

        new("Concurrency", "AsyncLocalContextLeakage",
            "An AsyncLocal value set in one async flow leaks into an unrelated flow via pooled threads or callbacks.",
            "Set an AsyncLocal, complete the flow, and verify a pooled-thread reuse does not observe stale context."),

        new("Concurrency", "SemaphoreLeakOnExceptionPath",
            "A semaphore or mutex is acquired but never released because an exception skips the release.",
            "Throw after acquiring a semaphore and verify it is released (try/finally or using) so the next waiter proceeds."),
    ];
}