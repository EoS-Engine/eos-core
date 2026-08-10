using EOS.Contracts;

namespace EOS.Learning;

/// <summary>
/// WP-027 Decision 5 (locked): INV-4's "cleared only by a Principal Engineer or higher authority,
/// never by the Learning Engine autonomously" — <paramref name="clearingRole"/>/<paramref
/// name="justification"/> are trusted, caller-supplied metadata, exactly matching
/// <c>LessonQuarantineCleared</c>'s own frozen payload shape (§15: "record_id, clearing_role,
/// justification"). No <c>EOS.Gates</c> dependency, no authentication/identity system — this
/// codebase has no real authentication anywhere (single-operator, offline), and every other
/// "simulated role authority" instance in this repository (e.g. <c>CompressionSweep</c>'s
/// <c>ActionRequest.Actor</c>, <c>TaskGraphBuilder</c>'s <c>ReasoningRequest.RequestingRole</c>)
/// is likewise a trusted string, never a session/token check. Both methods require an explicit
/// caller action; neither is ever invoked autonomously by anything else in this project.
/// </summary>
public sealed class QuarantineClearingService(
    IPipelineRecordStore pipelineRecordStore,
    ILessonQuarantineClearedEventPublisher lessonQuarantineClearedEventPublisher)
{
    /// <summary>
    /// Restores the record to <see cref="PipelineRecord.Stage"/> — its own, already-persisted
    /// stage. Quarantine never mutates <c>Stage</c> anywhere in this codebase (only
    /// <c>Status</c> — see <c>IntegrityChecker.RunAsync</c> and WP-026's <c>Ingestion</c>
    /// quarantine path, both of which pass the record's existing <c>Stage</c> straight through
    /// to <c>UpdateStageAsync</c> unchanged), so the record's own <c>Stage</c> field is always
    /// the correct and only valid "prior stage" — there is no legitimate reason for a caller to
    /// supply a different one, and no parameter exists here for it to do so.
    /// </summary>
    public async Task ClearAsync(
        Guid recordId, string clearingRole, string justification, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clearingRole))
        {
            throw new ArgumentException("clearingRole must not be empty.", nameof(clearingRole));
        }

        if (string.IsNullOrWhiteSpace(justification))
        {
            throw new ArgumentException("justification must not be empty.", nameof(justification));
        }

        var record = await pipelineRecordStore.GetByIdAsync(recordId, cancellationToken)
            ?? throw new InvalidOperationException($"PipelineRecord '{recordId}' does not exist.");

        if (record.Status != PipelineRecordStatus.Quarantined)
        {
            throw new InvalidOperationException($"PipelineRecord '{recordId}' is {record.Status}, not Quarantined.");
        }

        await pipelineRecordStore.UpdateStageAsync(
            recordId, record.Stage, PipelineRecordStatus.Active, record.ConfidenceScore, cancellationToken);

        lessonQuarantineClearedEventPublisher.PublishLessonQuarantineCleared(recordId, clearingRole, justification);
    }

    /// <summary>§16: "Quarantined --(confirmed poisoning/corruption)--&gt; Archived" — the other caller-invoked disposition, also requiring explicit Principal-Engineer-authority metadata, never autonomous.</summary>
    public async Task ArchiveAsync(
        Guid recordId, string clearingRole, string justification, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clearingRole))
        {
            throw new ArgumentException("clearingRole must not be empty.", nameof(clearingRole));
        }

        if (string.IsNullOrWhiteSpace(justification))
        {
            throw new ArgumentException("justification must not be empty.", nameof(justification));
        }

        var record = await pipelineRecordStore.GetByIdAsync(recordId, cancellationToken)
            ?? throw new InvalidOperationException($"PipelineRecord '{recordId}' does not exist.");

        if (record.Status != PipelineRecordStatus.Quarantined)
        {
            throw new InvalidOperationException($"PipelineRecord '{recordId}' is {record.Status}, not Quarantined.");
        }

        await pipelineRecordStore.UpdateStageAsync(
            recordId, record.Stage, PipelineRecordStatus.Archived, record.ConfidenceScore, cancellationToken);
    }
}
