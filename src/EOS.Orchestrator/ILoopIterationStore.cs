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

    Task CompleteAsync(Guid iterationId, string outcome, int[] stepsTraversed, CancellationToken cancellationToken = default);

    Task<LoopIteration?> GetByIdAsync(Guid iterationId, CancellationToken cancellationToken = default);

    /// <summary>Backs <see cref="EOS.Contracts.ILoopControlClient.GetCurrentStatusAsync"/>'s "current iteration."</summary>
    Task<LoopIteration?> GetLatestAsync(CancellationToken cancellationToken = default);
}
