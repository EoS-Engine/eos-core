namespace EOS.Contracts;

/// <summary>
/// Constitution §0.4.2's Planning Output, unchanged: "an ordered Task Graph with dependencies,
/// competency requirements, estimated resource cost, risk-adjusted confidence score" — one
/// record, not a `Plan` wrapping a separately-persisted Task Graph object (WP-023 Implementation
/// Blueprint Decision D3). Persisted via <c>PlanStore</c> as this WP's realization of the
/// Artifact Registry (Constitution Part 8, FR-PE5) for Plan artifacts specifically.
/// <see cref="PreviousPlanId"/> (WP-025) satisfies Constitution Part 8 §8.3's immutable-
/// versioning rule ("a new version references the prior version's hash") for Plans produced by
/// Dynamic Replanning — <c>PlanStore.InsertAsync</c> already always creates a fresh row/PlanId,
/// so this is an additive predecessor reference, never an in-place edit of the prior Plan.
/// </summary>
public sealed record Plan(
    Guid PlanId,
    Guid GoalId,
    PlanTask[] Tasks,
    double EstimatedResourceCost,
    double RiskAdjustedConfidenceScore,
    Guid? PreviousPlanId);
