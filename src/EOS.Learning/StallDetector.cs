using EOS.Contracts;

namespace EOS.Learning;

/// <summary>
/// Learning-Engine-Specification-v1.1 §11.4/§16's Stall Sweep: "Any stage --(stall sweep,
/// unresolved)--&gt; Stalled". Real, directly-callable, fully-tested; no scheduler (same
/// disclosed, pre-existing "no live driver loop anywhere in this codebase" condition as
/// <c>CompressionSweep</c>/<c>FitnessMonitor</c>).
///
/// Constitution §0.12.1 defines "Sprint cycle" only as a 1-2 week *range*, and no frozen
/// document gives an exact day-count for "&gt;2 consecutive Sprint cycles" — rather than
/// inventing a specific number, <paramref name="stallWindow"/> is an explicit caller-supplied
/// parameter (the same "explicit input, not a fabricated default" pattern as the ROI Gate and
/// FitnessMonitor).
/// </summary>
public sealed class StallDetector(
    IPipelineRecordStore pipelineRecordStore, ILessonStalledEventPublisher lessonStalledEventPublisher)
{
    public async Task<int> RunAsync(DateTimeOffset now, TimeSpan stallWindow, CancellationToken cancellationToken = default)
    {
        // CodeRabbit R1 finding #9: a zero or negative window would treat every Active record as
        // stalled immediately, regardless of how recently it actually advanced.
        if (stallWindow <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(stallWindow), stallWindow, "stallWindow must be positive.");
        }

        var activeRecords = await pipelineRecordStore.GetByStatusAsync(PipelineRecordStatus.Active, cancellationToken);
        var stalledCount = 0;

        foreach (var record in activeRecords)
        {
            if (now - record.LastAdvancedAt < stallWindow)
            {
                continue;
            }

            await pipelineRecordStore.UpdateStageAsync(
                record.RecordId, record.Stage, PipelineRecordStatus.Stalled, record.ConfidenceScore, cancellationToken);
            lessonStalledEventPublisher.PublishLessonStalled(record.RecordId);
            stalledCount++;
        }

        return stalledCount;
    }
}
