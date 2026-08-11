namespace EOS.Orchestrator;

/// <summary>
/// Autonomous-Engineering-Loop-Specification-v1.0 §17's <c>LoopIterationStarted</c> event —
/// payload frozen exactly as specified: "iteration_id, trigger_source, entry_step".
/// </summary>
public interface ILoopIterationStartedEventPublisher
{
    void PublishLoopIterationStarted(Guid iterationId, string triggerSource, int entryStep);
}
