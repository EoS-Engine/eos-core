namespace EOS.Reasoning;

/// <summary>
/// WP-019 frozen plan, Configuration Changes: Context Expansion cap (§12.4, default 1, the
/// configurable value itself per the plan's resolved ambiguity) and the Low Confidence floor
/// (§21), both sourced from <c>Thresholds.json</c> via <c>ThresholdsOptions</c> at the
/// composition root.
/// </summary>
public sealed record ReasoningEngineOptions(int ContextExpansionCap, double LowConfidenceFloor);
