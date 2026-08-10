using EOS.Contracts;

namespace EOS.Learning;

/// <summary>
/// Learning-Engine-Specification-v1.1 §9's <c>TransitionRecord</c> persistence — owned by
/// <c>EOS.Learning</c>, matching <see cref="IPipelineRecordStore"/>'s exact ownership posture.
/// </summary>
public interface ITransitionRecordStore
{
    Task EnsureTableExistsAsync(CancellationToken cancellationToken = default);

    Task InsertAsync(TransitionRecord record, CancellationToken cancellationToken = default);

    /// <summary>§11.6's <c>IntegrityChecker.scheduled_scan()</c>: "<c>for t in TransitionRecord.all()</c>".</summary>
    Task<IReadOnlyList<TransitionRecord>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TransitionRecord>> GetByRecordIdAsync(Guid recordId, CancellationToken cancellationToken = default);
}
