namespace EOS.Learning;

/// <summary>
/// WP-027 Decision 1 (locked): the ROI Gate's explicit, caller-supplied input contract —
/// Constitution §0.16.2's formula requires these three values, but no frozen document or
/// existing production code defines an automated source for any of them (verified: no
/// <c>PipelineRecord</c> field, no consumed interface, no event carries them). Rather than
/// inventing a data source, the caller (whoever drives promotion — a test, or a future WP) must
/// supply them explicitly. All three are nullable so "missing" is representable without a
/// sentinel value.
/// </summary>
public sealed record RoiEvaluationInput(
    double? ManualCostSavedPerInvocation,
    double? ProjectedInvocationFrequency,
    double? BuildAndMaintenanceCost);
