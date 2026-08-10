using EOS.Contracts;

namespace EOS.Learning;

/// <summary>
/// Learning-Engine-Specification-v1.1 §11.6's <c>IntegrityChecker.scheduled_scan()</c> — real,
/// directly-callable, fully-tested code; its invocation cadence ("scheduled_scan") is out of
/// scope the same way <c>CompressionSweep</c>'s (WP-016) and <c>FitnessMonitor</c>'s own cadence
/// are: no scheduler/timer/host exists anywhere in this codebase (a pre-existing, disclosed
/// condition this WP neither introduces nor is required to close).
///
/// For every <see cref="TransitionRecord"/>: recomputes its SHA-256 <c>IntegrityHash</c> (WP-027
/// Decision 3) and independently validates <see cref="PipelineStateMachine.IsValidEdge"/>. On
/// either mismatch, emits <c>DataIntegrityViolationDetected</c> and Quarantines the associated
/// <see cref="PipelineRecord"/> — never silently repairs (§24.8: "silently 'fixing' history would
/// itself violate evidence-over-assertion").
/// </summary>
public sealed class IntegrityChecker(
    ITransitionRecordStore transitionRecordStore,
    IPipelineRecordStore pipelineRecordStore,
    IDataIntegrityViolationDetectedEventPublisher dataIntegrityViolationDetectedEventPublisher)
{
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var violationCount = 0;
        var allTransitions = await transitionRecordStore.GetAllAsync(cancellationToken);

        foreach (var transition in allTransitions)
        {
            var recomputedHash = IntegrityHashCalculator.Compute(transition);
            var validEdge = PipelineStateMachine.IsValidEdge(transition.FromStage, transition.ToStage);

            if (recomputedHash == transition.IntegrityHash && validEdge)
            {
                continue;
            }

            violationCount++;
            dataIntegrityViolationDetectedEventPublisher.PublishDataIntegrityViolationDetected(
                transition.RecordId, transition.FromStage, transition.ToStage);

            var record = await pipelineRecordStore.GetByIdAsync(transition.RecordId, cancellationToken);
            if (record is not null)
            {
                await pipelineRecordStore.UpdateStageAsync(
                    record.RecordId, record.Stage, PipelineRecordStatus.Quarantined, record.ConfidenceScore, cancellationToken);
            }
        }

        return violationCount;
    }
}
