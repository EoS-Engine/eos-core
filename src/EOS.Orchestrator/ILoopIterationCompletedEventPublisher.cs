namespace EOS.Orchestrator;

/// <summary>
/// Autonomous-Engineering-Loop-Specification-v1.0 §17's <c>LoopIterationCompleted</c> event —
/// payload frozen exactly as specified: "iteration_id, steps_traversed, outcome".
/// </summary>
public interface ILoopIterationCompletedEventPublisher
{
    void PublishLoopIterationCompleted(Guid iterationId, int[] stepsTraversed, string outcome);
}
