namespace EOS.Knowledge;

/// <summary>
/// Knowledge-Management-Specification-v1.0 §15.7's <c>q1..q4</c> weights for the additive
/// quality/relationship-aware ranking pass — "externally configurable (<c>Knowledge.json</c>),
/// fully independent of Memory's own <c>w1..w4</c> weights (<see cref="RankingWeights"/>)... the
/// two weighting schemes never merge into one formula."
/// </summary>
public sealed record KnowledgeRankingWeights(double Confidence, double Reliability, double RelationshipRelevance, double DeprecationPenalty);
