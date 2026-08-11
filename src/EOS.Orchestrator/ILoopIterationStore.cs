namespace EOS.Orchestrator;

/// <summary>
/// Autonomous-Engineering-Loop-Specification-v1.0 §19.1's Loop Iteration Lifecycle persistence —
/// owned by <c>EOS.Orchestrator</c>, mirroring <c>ITransitionRecordStore</c>'s exact ownership
/// posture and shape (WP-028 Decision 4, locked).
/// </summary>
public interface ILoopIterationStore
{
    Task EnsureTableExistsAsync(CancellationToken cancellationToken = default);

    Task InsertAsync(LoopIteration iteration, CancellationToken cancellationToken = default);

    Task UpdateStateAsync(Guid iterationId, string state, int[] stepsTraversed, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists any terminal state (<c>Completed</c> or <c>Failed</c>) together with its outcome,
    /// steps traversed, and completion timestamp in one write — CodeRabbit R1 finding #2: the
    /// prior split (<see cref="UpdateStateAsync"/> could write <c>Failed</c> but not
    /// <c>Outcome</c>/<c>CompletedAt</c>; this method could only ever write <c>Completed</c>) made
    /// a correctly-persisted failed terminal iteration impossible. <paramref name="state"/> is
    /// bound as a parameter, never hard-coded.
    /// </summary>
    Task CompleteAsync(Guid iterationId, string state, string outcome, int[] stepsTraversed, CancellationToken cancellationToken = default);

    Task<LoopIteration?> GetByIdAsync(Guid iterationId, CancellationToken cancellationToken = default);

    /// <summary>Backs <see cref="EOS.Contracts.ILoopControlClient.GetCurrentStatusAsync"/>'s "current iteration."</summary>
    Task<LoopIteration?> GetLatestAsync(CancellationToken cancellationToken = default);
}
