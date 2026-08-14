namespace EOS.Orchestrator;

/// <summary>
/// Autonomous-Engineering-Loop-Specification-v1.0 §13.1/§17's <c>LoopIterationEvaluated</c> event
/// — payload frozen exactly as specified: "iteration_id, loop_health_score". WP-029 Decision 1
/// (locked): no aggregation formula exists for <c>loop_health_score</c> in the current repository,
/// so <paramref name="loopHealthScore"/> is <c>null</c> for every emission until a future approved
/// ADR/specification defines the KPI availability threshold and aggregation formula — the event's
/// own nullable contract (matching <see cref="EOS.Contracts.LoopStatus.LoopHealthScore"/>'s
/// existing <c>double?</c> shape) is not changed to require a non-null value.
/// </summary>
public interface ILoopIterationEvaluatedEventPublisher
{
    void PublishLoopIterationEvaluated(Guid iterationId, double? loopHealthScore);
}
