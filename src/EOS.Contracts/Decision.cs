namespace EOS.Contracts;

public sealed record Decision(
    Guid DecisionId,
    Guid RequestId,
    ReasoningType ReasoningTypeApplied,
    string SelectedHypothesis,
    string[] RejectedHypotheses,
    string[] EvidenceRefs,
    double Confidence,
    Explanation Explanation,
    string TradeOffs,
    double RiskScore,
    bool Reproducible,
    DateTimeOffset OccurredAt);
