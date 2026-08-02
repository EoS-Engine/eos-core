namespace EOS.Contracts;

/// <summary>
/// WP-020: <see cref="CompareAsync"/>, <see cref="GetTrustSignalAsync"/>, and
/// <see cref="SummarizeAsync"/> are additive to WP-008/WP-019's <see cref="ReasonAsync"/>.
/// <c>query_history()</c> (Reasoning-Engine-Specification-v1.0 §16.1/§13.7) is intentionally
/// absent — deferred per AG-0003 (<c>docs/Architecture-Gaps/AG-0003-WP020-QueryHistory-DataAccess-Gap.md</c>):
/// no frozen document grants <c>EOS.Reasoning</c> a consumed interface or dependency capable of
/// reading the Artifact Registry/Event Catalog data its projection requires.
/// </summary>
public interface IReasoningEngineClient
{
    Task<Decision[]> ReasonAsync(ReasoningRequest request, CancellationToken cancellationToken = default);

    Task<ConfidenceGuardResult> CompareAsync(
        PipelineRecord subject, IEnumerable<PipelineRecord> candidates, CancellationToken cancellationToken = default);

    Task<TrustSignal> GetTrustSignalAsync(string sourceRole, CancellationToken cancellationToken = default);

    Task<Summary> SummarizeAsync(string content, int? sizeBudget = null, CancellationToken cancellationToken = default);
}
