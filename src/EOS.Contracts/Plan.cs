namespace EOS.Contracts;

/// <summary>
/// Constitution §0.4.2's Planning Output, unchanged: "an ordered Task Graph with dependencies,
/// competency requirements, estimated resource cost, risk-adjusted confidence score" — one
/// record, not a `Plan` wrapping a separately-persisted Task Graph object (WP-023 Implementation
/// Blueprint Decision D3). Persisted via <c>PlanStore</c> as this WP's realization of the
/// Artifact Registry (Constitution Part 8, FR-PE5) for Plan artifacts specifically.
/// </summary>
public sealed record Plan(
    Guid PlanId,
    Guid GoalId,
    PlanTask[] Tasks,
    double EstimatedResourceCost,
    double RiskAdjustedConfidenceScore);
